using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;
using PodcastGenerator.Infrastructure.Jobs;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

/// <summary>Defects found in the backend review (missing files, leases, crash states, validation gaps).</summary>
public class RobustnessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_finished_chunk_whose_file_is_gone_is_voiced_again_on_retry()
    {
        factory.Tts.FailOnceWhen = text => text.StartsWith("Thema Rhön. Das ist Satz 1 aus Teil 2", StringComparison.Ordinal);
        var id = await EpisodeApi.CreateAsync(_client, "Rhön");
        await EpisodeApi.WaitForAsync(_client, id, "Failed");
        File.Delete(Path.Combine(factory.AudioRoot, AudioGenerator.ChunkKey(id, 0)));

        await _client.PostAsync(new Uri($"/api/episodes/{id}/retry", UriKind.Relative), null);
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        Assert.Equal(2, factory.Tts.CallsFor("Thema Rhön. Das ist Satz 1 aus Teil 1")); // lost file re-synthesized
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(new Uri($"/api/episodes/{id}/audio", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task A_missing_episode_file_is_404_not_500()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Eifel");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");
        File.Delete(Path.Combine(factory.AudioRoot, AudioGenerator.EpisodeAudioKey(id)));

        var response = await _client.GetAsync(new Uri($"/api/episodes/{id}/audio", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lease_writes_only_apply_for_the_current_owner()
    {
        var (jobId, episodeId) = await SeedRunningJobAsync(attempts: 3);
        await using var scope = factory.Services.CreateAsyncScope();
        var leases = scope.ServiceProvider.GetRequiredService<JobLeases>();

        Assert.True(await leases.ExtendLeaseAsync(new ClaimedJob(jobId, episodeId, 3), TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await leases.ExtendLeaseAsync(new ClaimedJob(jobId, episodeId, 2), TimeSpan.FromMinutes(5), CancellationToken.None));

        await leases.ReleaseAsync(new ClaimedJob(jobId, episodeId, 2), CancellationToken.None); // a previous owner
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(JobStatus.Running, (await db.GenerationJobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status);
    }

    /// <summary>
    /// State left by a crash between the two saves of the continuation step: the closing section already moved
    /// from order 1 to 2, the continuation not inserted yet. The restart must fill order 1, not leave a gap.
    /// </summary>
    [Fact]
    public async Task Continuation_after_a_crash_fills_the_gap_instead_of_moving_the_closing_section_again()
    {
        const string topic = "Hiddensee";
        var episodeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Episodes.Add(new Episode
            {
                Id = episodeId,
                UserId = SeedData.LocalUserId,
                Topic = topic,
                Title = topic,
                Level = CefrLevel.B1,
                DurationMin = 10,
                TargetWords = 1360,
                SpeakingRate = 0.85m,
                Status = EpisodeStatus.GeneratingSections,
                CreatedAt = now,
                UpdatedAt = now,
                Sections =
                [
                    Done(0, "Thema Hiddensee. Der Anfang."),
                    Done(2, "Thema Hiddensee. Tschüss!"),
                ],
            });
            db.GenerationJobs.Add(new GenerationJob { Id = Guid.NewGuid(), EpisodeId = episodeId, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await EpisodeApi.WaitForAsync(_client, episodeId, "Ready");

        await using var check = factory.Services.CreateAsyncScope();
        var sections = await check.ServiceProvider.GetRequiredService<AppDbContext>().EpisodeSections.AsNoTracking()
            .Where(s => s.EpisodeId == episodeId).OrderBy(s => s.Order).ToListAsync();
        Assert.Equal([0, 1, 2], sections.Select(s => s.Order));
        Assert.True(sections[1].IsContinuation);
        Assert.Equal("Tschüss!", sections[2].Title);
    }

    [Theory]
    [InlineData("durationMin=-5")]
    [InlineData("durationMin=500")]
    [InlineData("speakingRate=0.855")]
    public async Task Estimate_validates_its_inputs(string query)
    {
        var response = await _client.GetAsync(new Uri($"/api/episodes/estimate?{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"nativeLanguage":"en","targetLanguage":"de","defaultLevel":"B1","defaultDurationMin":10,"interests":null,"voiceModelFamily":"WaveNet","playbackSpeed":1.0}""", "Interests")]
    [InlineData("""{"nativeLanguage":"en","targetLanguage":"de","defaultLevel":"B1","defaultDurationMin":10,"interests":["a",null],"voiceModelFamily":"WaveNet","playbackSpeed":1.0}""", "Interests")]
    [InlineData("""{"nativeLanguage":"en","targetLanguage":"de","defaultLevel":"B1","defaultDurationMin":10,"interests":[],"voiceName":"de-DE-Chirp3-HD-Charon","voiceModelFamily":"WaveNet","playbackSpeed":1.0}""", "VoiceName")]
    public async Task Invalid_preferences_are_400_not_500(string json, string field)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PutAsync(new Uri("/api/preferences", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static EpisodeSection Done(int order, string text) => new()
    {
        Order = order,
        Title = text.Split(". ")[1],
        Summary = "Part",
        TargetWords = 680,
        WordCount = 4,
        Status = StepStatus.Completed,
        Attempts = 1,
        Recap = "Covered.",
        Segments = [new TranscriptSegment { Order = 0, ParagraphIndex = 0, Text = text }],
    };

    private async Task<(Guid JobId, Guid EpisodeId)> SeedRunningJobAsync(int attempts)
    {
        var now = DateTimeOffset.UtcNow;
        var episodeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Episodes.Add(new Episode
        {
            Id = episodeId,
            UserId = SeedData.LocalUserId,
            Topic = "Lease",
            Level = CefrLevel.B1,
            DurationMin = 10,
            TargetWords = 1360,
            Status = EpisodeStatus.GeneratingSections,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.GenerationJobs.Add(new GenerationJob
        {
            Id = jobId,
            EpisodeId = episodeId,
            Status = JobStatus.Running,
            LockedUntil = now.AddMinutes(10), // owned by "someone": the worker skips it
            Attempts = attempts,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (jobId, episodeId);
    }
}
