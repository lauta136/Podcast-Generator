using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Llm;

namespace PodcastGenerator.IntegrationTests;

/// <summary>
/// Deterministic LLM for tests. Behaviour is keyed by the episode topic, so tests sharing a factory don't interfere:
/// topics containing "FailSectionTwoOnce" fail section 2 on its first attempt; "ShortOnWords" writes 60% of the target;
/// "ProofreadRewrites" makes the proofreader drop a paragraph (rejected); "ProofreadOutageOnce" fails the first proofread.
/// Every generated sentence contains the typo "Fehlerr", which the proofreader corrects to "Fehler".
/// </summary>
public sealed partial class FakeLlmProvider : ILlmProvider
{
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public int Calls(string topic, string purpose) => _calls.GetValueOrDefault($"{topic}|{purpose}");

    public Task<T> GenerateJsonAsync<T>(LlmRequest request, CancellationToken ct)
    {
        object reply = request.Purpose switch
        {
            "outline" => Outline(request),
            "section" => Section(request),
            "proofread" or "proofread-repair" => Proofread(request),
            _ => throw new NotSupportedException(request.Purpose),
        };
        return Task.FromResult((T)reply);
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken ct) => throw new NotSupportedException();

    private OutlineResponse Outline(LlmRequest request)
    {
        var topic = TopicLine().Match(request.UserPrompt).Groups[1].Value.Trim();
        Count(topic, "outline");
        var count = int.Parse(SectionCountLine().Match(request.UserPrompt).Groups[1].Value, CultureInfo.InvariantCulture);
        return new OutlineResponse
        {
            Title = topic, // lets section prompts be matched back to the topic
            Description = "Eine Testfolge.",
            Sections = [.. Enumerable.Range(1, count).Select(i => new OutlineSection { Title = $"Teil {i}", Summary = $"Part {i}", Focus = "Perfekt" })],
        };
    }

    private SectionResponse Section(LlmRequest request)
    {
        var topic = EpisodeTitleLine().Match(request.UserPrompt).Groups[1].Value;
        var number = int.Parse(SectionNumberLine().Match(request.UserPrompt).Groups[1].Value, CultureInfo.InvariantCulture);
        var target = int.Parse(TargetWordsLine().Match(request.UserPrompt).Groups[1].Value, CultureInfo.InvariantCulture);
        var attempt = Count(topic, $"section-{number}");
        Count(topic, "section");

        if (topic.Contains("FailSectionTwoOnce", StringComparison.Ordinal) && number == 2 && attempt == 1)
        {
            throw new LlmUnavailableException("Simulated provider outage");
        }

        var words = topic.Contains("ShortOnWords", StringComparison.Ordinal) && !request.UserPrompt.Contains("Go deeper", StringComparison.Ordinal)
            ? (int)(target * 0.6)
            : target;

        // Sentences of 10 words, paragraphs of 8 sentences. The first names the topic so proofreading can be matched.
        var sentences = Enumerable.Range(0, Math.Max(1, words / 10))
            .Select(i => $"Das ist Satz {i + 1} aus Teil {number} mit einem Fehlerr.")
            .Prepend($"Thema {topic}.")
            .ToList();
        var paragraphs = sentences.Chunk(8).Select(c => string.Join(" ", c)).ToList();
        return new SectionResponse { Paragraphs = paragraphs, Recap = $"Part {number} was covered." };
    }

    private ProofreadResponse Proofread(LlmRequest request)
    {
        var json = ParagraphsJson().Match(request.UserPrompt).Groups[1].Value;
        var paragraphs = JsonSerializer.Deserialize<List<string>>(json)!;
        var topic = TopicSentence().Match(paragraphs[0]).Groups[1].Value;
        var number = SectionInText().Match(string.Join(" ", paragraphs)).Groups[1].Value;
        var attempt = Count(topic, $"proofread-{number}");
        Count(topic, "proofread");

        if (topic.Contains("ProofreadOutageOnce", StringComparison.Ordinal) && number == "1" && attempt == 1)
        {
            throw new LlmUnavailableException("Simulated proofreading outage");
        }

        if (topic.Contains("ProofreadRewrites", StringComparison.Ordinal))
        {
            return new ProofreadResponse { Paragraphs = paragraphs.Skip(1).ToList() };
        }

        return new ProofreadResponse
        {
            Paragraphs = paragraphs.Select(p => p.Replace("Fehlerr", "Fehler", StringComparison.Ordinal)).ToList(),
            Corrections = [new ProofreadCorrection { Original = "einem Fehlerr", Corrected = "einem Fehler" }],
        };
    }

    private int Count(string topic, string key) => _calls.AddOrUpdate($"{topic}|{key}", 1, (_, n) => n + 1);

    [GeneratedRegex(@"paragraphs:\s*(\[.*?\])\s*Reply with", RegexOptions.Singleline)]
    private static partial Regex ParagraphsJson();

    [GeneratedRegex(@"^Thema ([^.]+)\.")]
    private static partial Regex TopicSentence();

    [GeneratedRegex(@"aus Teil (\d+)")]
    private static partial Regex SectionInText();

    [GeneratedRegex(@"Topic: (.+)")]
    private static partial Regex TopicLine();

    [GeneratedRegex(@"exactly (\d+) sections")]
    private static partial Regex SectionCountLine();

    [GeneratedRegex(@"of the episode ""(.+)""\.")]
    private static partial Regex EpisodeTitleLine();

    [GeneratedRegex(@"Write section (\d+) of")]
    private static partial Regex SectionNumberLine();

    [GeneratedRegex(@"Length: at least (\d+) words")]
    private static partial Regex TargetWordsLine();
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
