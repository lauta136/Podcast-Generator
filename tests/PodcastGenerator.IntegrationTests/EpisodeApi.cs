using System.Net.Http.Json;
using System.Text.Json;

namespace PodcastGenerator.IntegrationTests;

internal static class EpisodeApi
{
    public static async Task<Guid> CreateAsync(HttpClient client, string topic, int durationMin = 10)
    {
        var response = await client.PostAsJsonAsync("/api/episodes", new { topic, durationMin, level = "B1" }, JsonDefaults.Web);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> WaitForAsync(HttpClient client, Guid id, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        JsonElement status;
        do
        {
            status = await client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/status", JsonDefaults.Web);
            if (status.GetProperty("status").GetString() == expected)
            {
                return status;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Episode {id} did not reach {expected}; last status: {status}");
    }
}
