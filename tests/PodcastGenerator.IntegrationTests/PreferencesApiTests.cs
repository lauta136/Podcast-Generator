using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PodcastGenerator.IntegrationTests;

public class PreferencesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_returns_defaults_for_the_local_user()
    {
        var prefs = await _client.GetFromJsonAsync<JsonElement>("/api/preferences", Json);

        Assert.Equal("en", prefs.GetProperty("nativeLanguage").GetString());
        Assert.Equal("de", prefs.GetProperty("targetLanguage").GetString());
        Assert.Equal("B1", prefs.GetProperty("defaultLevel").GetString());
    }

    [Fact]
    public async Task Put_persists_changes()
    {
        var request = new
        {
            nativeLanguage = "en",
            targetLanguage = "de",
            defaultLevel = "B2",
            defaultDurationMin = 30,
            interests = new[] { "history", "cooking" },
            voiceName = "de-DE-Wavenet-H",
            voiceModelFamily = "Chirp3HD",
            playbackSpeed = 0.9,
        };

        var put = await _client.PutAsJsonAsync("/api/preferences", request, Json);
        put.EnsureSuccessStatusCode();

        var prefs = await _client.GetFromJsonAsync<JsonElement>("/api/preferences", Json);
        Assert.Equal("B2", prefs.GetProperty("defaultLevel").GetString());
        Assert.Equal(30, prefs.GetProperty("defaultDurationMin").GetInt32());
        Assert.Equal("Chirp3HD", prefs.GetProperty("voiceModelFamily").GetString());
        Assert.Equal(2, prefs.GetProperty("interests").GetArrayLength());
    }

    [Fact]
    public async Task Put_with_invalid_values_returns_validation_problem()
    {
        var request = new
        {
            nativeLanguage = "en",
            targetLanguage = "fr",
            defaultLevel = "B1",
            defaultDurationMin = 10,
            interests = Array.Empty<string>(),
            voiceModelFamily = "WaveNet",
            playbackSpeed = 1.0,
        };

        var response = await _client.PutAsJsonAsync("/api/preferences", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(problem.GetProperty("errors").TryGetProperty("TargetLanguage", out _));
    }

    [Fact]
    public async Task Health_endpoint_is_up()
    {
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
