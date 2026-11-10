using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Prompts;

/// <summary>Loads versioned prompt templates embedded in this assembly and fills {{placeholders}}.</summary>
public static partial class PromptLibrary
{
    public const string SystemTemplate = "system.v1";
    public const string OutlineTemplate = "outline.v1";
    public const string SectionTemplate = "section.v2";
    public const string ProofreadTemplate = "proofread.v1";
    private const string CefrFile = "cefr.v1.json";

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var text = Load(template + ".txt");
        var rendered = Placeholder().Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var v)
                ? v
                : throw new InvalidOperationException($"Prompt {template} needs a value for '{m.Groups[1].Value}'."));
        return rendered.Trim();
    }

    public static string CefrGuidelines(CefrLevel level)
    {
        var all = JsonSerializer.Deserialize<Dictionary<string, string>>(Load(CefrFile))!;
        return all[level.ToString()];
    }

    private static string Load(string fileName) => Cache.GetOrAdd(fileName, name =>
    {
        var assembly = typeof(PromptLibrary).Assembly;
        var resource = $"{assembly.GetName().Name}.Prompts.{name}";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Prompt resource '{resource}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
