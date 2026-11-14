using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PodcastGenerator.Application.Llm;

namespace PodcastGenerator.IntegrationTests;

public class TranslationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_click_translates_the_whole_paragraph_once_and_then_answers_from_the_cache()
    {
        var (id, paragraph) = await FirstParagraphAsync("Thema Spessart");
        var before = factory.Llm.TranslationCalls;

        var first = await TranslateAsync(id, paragraph[2].Id);

        Assert.Equal("en", first.GetProperty("language").GetString());
        var sentences = first.GetProperty("sentences").EnumerateArray().ToList();
        Assert.Equal(paragraph.Select(s => s.Id), sentences.Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal("EN: " + paragraph[0].Text, sentences[0].GetProperty("translation").GetString());
        Assert.Equal(before + 1, factory.Llm.TranslationCalls);
        Assert.Contains("into English", factory.Llm.LastTranslationPrompt, StringComparison.Ordinal);

        // Another sentence of the same paragraph, and the same one again: no new request.
        await TranslateAsync(id, paragraph[0].Id);
        await TranslateAsync(id, paragraph[2].Id);
        Assert.Equal(before + 1, factory.Llm.TranslationCalls);
    }

    [Fact]
    public async Task Changing_the_native_language_translates_again_into_the_new_one()
    {
        var (id, paragraph) = await FirstParagraphAsync("Thema Taunus");
        await TranslateAsync(id, paragraph[0].Id);
        var before = factory.Llm.TranslationCalls;

        await SetNativeLanguageAsync("es");
        try
        {
            var spanish = await TranslateAsync(id, paragraph[0].Id);
            Assert.Equal("es", spanish.GetProperty("language").GetString());
            Assert.StartsWith("ES: ", spanish.GetProperty("sentences")[0].GetProperty("translation").GetString(), StringComparison.Ordinal);
            Assert.Equal(before + 1, factory.Llm.TranslationCalls);
        }
        finally
        {
            await SetNativeLanguageAsync("en");
        }
    }

    [Fact]
    public async Task Unknown_or_foreign_sentences_are_404()
    {
        var (id, _) = await FirstParagraphAsync("Thema Odenwald");
        var (otherId, otherParagraph) = await FirstParagraphAsync("Thema Westerwald");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(id, otherParagraph[0].Id)).StatusCode); // sentence of another episode
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(Guid.NewGuid(), otherParagraph[0].Id)).StatusCode);
        Assert.NotEqual(id, otherId);
    }

    [Fact]
    public async Task A_busy_model_is_a_clear_503_and_the_next_click_works()
    {
        var (id, paragraph) = await FirstParagraphAsync("Thema Vogelsberg");
        factory.Llm.FailNextTranslation = new LlmUnavailableException("Simulated: quota exhausted");

        var busy = await PostAsync(id, paragraph[0].Id);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        var problem = await busy.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.Contains("Try again", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Simulated", await busy.Content.ReadAsStringAsync(), StringComparison.Ordinal); // provider details stay in logs

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(id, paragraph[0].Id)).StatusCode);
    }

    private async Task<(Guid Id, List<(Guid Id, string Text)> Paragraph)> FirstParagraphAsync(string topic)
    {
        var id = await EpisodeApi.CreateAsync(_client, topic);
        await EpisodeApi.WaitForAsync(_client, id, "Ready");
        var transcript = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/transcript", JsonDefaults.Web);
        var paragraph = transcript.GetProperty("sections")[0].GetProperty("segments").EnumerateArray()
            .Where(s => s.GetProperty("paragraph").GetInt32() == 0)
            .Select(s => (s.GetProperty("id").GetGuid(), s.GetProperty("text").GetString()!))
            .ToList();
        Assert.True(paragraph.Count >= 3, "the fake writes paragraphs of 8 sentences");
        return (id, paragraph);
    }

    private Task<HttpResponseMessage> PostAsync(Guid episodeId, Guid segmentId) =>
        _client.PostAsync(new Uri($"/api/episodes/{episodeId}/segments/{segmentId}/translation", UriKind.Relative), null);

    private async Task<JsonElement> TranslateAsync(Guid episodeId, Guid segmentId)
    {
        var response = await PostAsync(episodeId, segmentId);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
    }

    private async Task SetNativeLanguageAsync(string code)
    {
        var prefs = await _client.GetFromJsonAsync<JsonElement>("/api/preferences", JsonDefaults.Web);
        var update = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(prefs.GetRawText())!;
        update["nativeLanguage"] = JsonSerializer.SerializeToElement(code);
        (await _client.PutAsJsonAsync("/api/preferences", update, JsonDefaults.Web)).EnsureSuccessStatusCode();
    }
}
