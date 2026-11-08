using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

public class CrashRecoveryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// The state a killed API leaves behind: job still "Running" with an expired lease, one section done.
    /// The worker must take the job over and finish it without regenerating the outline or section 1.
    /// </summary>
    [Fact]
    public async Task Expired_lease_is_resumed_without_redoing_finished_sections()
    {
        const string topic = "Neuschwanstein";
        var client = factory.CreateClient(); // starts the host and its worker
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
                TargetWords = 1250,
                Status = EpisodeStatus.GeneratingSections,
                CreatedAt = now,
                UpdatedAt = now,
                Sections =
                [
                    new EpisodeSection
                    {
                        Order = 0, Title = "Teil 1", Summary = "Part 1", TargetWords = 625, WordCount = 625,
                        Status = StepStatus.Completed, Attempts = 1, Recap = "Part 1 was covered.",
                        Segments = [new TranscriptSegment { Order = 0, ParagraphIndex = 0, Text = "Schon fertig." }],
                    },
                    new EpisodeSection { Order = 1, Title = "Teil 2", Summary = "Part 2", TargetWords = 625 },
                ],
            });
            db.GenerationJobs.Add(new GenerationJob
            {
                Id = Guid.NewGuid(),
                EpisodeId = episodeId,
                Status = JobStatus.Running,
                LockedUntil = now.AddMinutes(-1), // the dead worker's lease
                Attempts = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status;
        do
        {
            await Task.Delay(100);
            var json = await client.GetFromJsonAsync<JsonElement>($"/api/episodes/{episodeId}/status", JsonDefaults.Web);
            status = json.GetProperty("status").GetString();
        }
        while (status != "Ready" && DateTime.UtcNow < deadline);

        Assert.Equal("Ready", status);
        Assert.Equal(0, factory.Llm.Calls(topic, "outline"));
        Assert.Equal(0, factory.Llm.Calls(topic, "section-1"));
        Assert.Equal(1, factory.Llm.Calls(topic, "section-2"));

        var transcript = await client.GetFromJsonAsync<JsonElement>($"/api/episodes/{episodeId}/transcript", JsonDefaults.Web);
        Assert.Equal("Schon fertig.", transcript.GetProperty("sections")[0].GetProperty("segments")[0].GetProperty("text").GetString());
    }
}
