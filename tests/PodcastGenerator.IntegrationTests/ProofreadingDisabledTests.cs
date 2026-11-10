using System.Net.Http.Json;
using System.Text.Json;

namespace PodcastGenerator.IntegrationTests;

public class ProofreadingDisabledTests(NoProofreadApiFactory factory) : IClassFixture<NoProofreadApiFactory>
{
    [Fact]
    public async Task With_proofreading_off_no_proofread_requests_are_made()
    {
        const string topic = "Schwarzwald";
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/episodes", new { topic, durationMin = 10, level = "B1" }, JsonDefaults.Web);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status;
        do
        {
            await Task.Delay(100);
            status = (await client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/status", JsonDefaults.Web)).GetProperty("status").GetString();
        }
        while (status != "Ready" && DateTime.UtcNow < deadline);

        Assert.Equal("Ready", status);
        Assert.Equal(0, factory.Llm.Calls(topic, "proofread"));
        var transcript = await client.GetStringAsync(new Uri($"/api/episodes/{id}/transcript", UriKind.Relative));
        Assert.Contains("Fehlerr", transcript); // uncorrected draft
    }
}
