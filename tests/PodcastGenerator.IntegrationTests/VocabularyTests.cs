using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PodcastGenerator.IntegrationTests;

public class VocabularyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_word_is_saved_once_with_its_dictionary_form_meaning_and_sentence()
    {
        var (_, sentence) = await SentenceAsync("Thema Lüneburg");
        const string word = "Lüneburg"; // the fake's first sentence is "Thema Lüneburg."

        var saved = await SaveAsync(sentence.Id, $" {word}. ");
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var card = await saved.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.Equal(word, card.GetProperty("text").GetString()); // punctuation and spaces trimmed
        Assert.Equal($"Grundform {word.ToLowerInvariant()}", card.GetProperty("baseForm").GetString());
        Assert.Equal($"EN: {word}", card.GetProperty("translation").GetString());
        Assert.Equal(sentence.Text, card.GetProperty("context").GetString());

        // Same word again, in other capitals: the existing card, not a second one.
        var again = await SaveAsync(sentence.Id, word.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(card.GetProperty("id").GetGuid(), (await again.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("Wort, das nicht im Satz steht")]
    [InlineData("eins zwei drei vier fünf sechs sieben")]
    [InlineData("...")]
    public async Task Only_a_short_expression_from_the_sentence_can_be_saved(string text)
    {
        var (_, sentence) = await SentenceAsync("Thema Celle");
        var calls = factory.Llm.VocabCardCalls;

        var response = await SaveAsync(sentence.Id, text);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(calls, factory.Llm.VocabCardCalls); // refused before any LLM request
    }

    [Fact]
    public async Task Cards_are_reviewed_with_growing_gaps_and_again_comes_back_soon()
    {
        var (_, sentence) = await SentenceAsync("Thema Goslar");
        var id = await SavedIdAsync(sentence.Id, "Goslar");

        var due = await _client.GetFromJsonAsync<JsonElement>("/api/vocabulary/review", JsonDefaults.Web);
        Assert.Contains(due.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id); // new cards are due at once

        var gotIt = await ReviewAsync(id, "GotIt");
        Assert.Equal(1, gotIt.GetProperty("box").GetInt32());
        Assert.InRange(gotIt.GetProperty("dueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromHours(23), TimeSpan.FromHours(25));
        due = await _client.GetFromJsonAsync<JsonElement>("/api/vocabulary/review", JsonDefaults.Web);
        Assert.DoesNotContain(due.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        var again = await ReviewAsync(id, "Again");
        Assert.Equal(0, again.GetProperty("box").GetInt32());
        Assert.InRange(again.GetProperty("dueAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
    }

    [Fact]
    public async Task Words_outlive_the_episode_they_came_from_and_can_be_deleted()
    {
        var (episodeId, sentence) = await SentenceAsync("Thema Hameln");
        var id = await SavedIdAsync(sentence.Id, "Hameln");
        var listed = await ListAsync("Hameln");
        Assert.Equal(episodeId, listed.GetProperty("items")[0].GetProperty("episodeId").GetGuid());
        Assert.Equal(JsonValueKind.Number, listed.GetProperty("items")[0].GetProperty("startSec").ValueKind); // "hear it"

        (await _client.DeleteAsync(new Uri($"/api/episodes/{episodeId}", UriKind.Relative))).EnsureSuccessStatusCode();

        var orphan = (await ListAsync("Hameln")).GetProperty("items")[0];
        Assert.Equal(id, orphan.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, orphan.GetProperty("episodeId").ValueKind);
        Assert.Equal(sentence.Text, orphan.GetProperty("context").GetString()); // the sentence was copied

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(new Uri($"/api/vocabulary/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(0, (await ListAsync("Hameln")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Another_account_cannot_save_from_my_sentences_or_see_my_words()
    {
        var (_, sentence) = await SentenceAsync("Thema Wernigerode");
        var id = await SavedIdAsync(sentence.Id, "Wernigerode");
        await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register", new { email = "vocab-owner@example.com", password = "ein langes passwort" });
        var other = factory.CreateAnonymousClient();
        (await other.PostAsJsonAsync("/api/auth/register", new { email = "vocab-other@example.com", password = "ein langes passwort" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/vocabulary", new { segmentId = sentence.Id, text = "Wernigerode" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/vocabulary/{id}/review", new { result = "GotIt" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(new Uri($"/api/vocabulary/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/vocabulary", JsonDefaults.Web)).GetProperty("total").GetInt32());
    }

    private async Task<(Guid EpisodeId, (Guid Id, string Text) Sentence)> SentenceAsync(string topic)
    {
        var id = await EpisodeApi.CreateAsync(_client, topic);
        await EpisodeApi.WaitForAsync(_client, id, "Ready");
        var transcript = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/transcript", JsonDefaults.Web);
        var first = transcript.GetProperty("sections")[0].GetProperty("segments")[0];
        return (id, (first.GetProperty("id").GetGuid(), first.GetProperty("text").GetString()!));
    }

    private Task<HttpResponseMessage> SaveAsync(Guid segmentId, string text) =>
        _client.PostAsJsonAsync("/api/vocabulary", new { segmentId, text }, JsonDefaults.Web);

    private async Task<Guid> SavedIdAsync(Guid segmentId, string text)
    {
        var response = await SaveAsync(segmentId, text);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ReviewAsync(Guid id, string result)
    {
        var response = await _client.PostAsJsonAsync($"/api/vocabulary/{id}/review", new { result }, JsonDefaults.Web);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
    }

    private async Task<JsonElement> ListAsync(string search) =>
        await _client.GetFromJsonAsync<JsonElement>($"/api/vocabulary?search={Uri.EscapeDataString(search)}", JsonDefaults.Web);
}
