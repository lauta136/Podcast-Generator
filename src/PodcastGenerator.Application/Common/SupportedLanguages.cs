namespace PodcastGenerator.Application.Common;

public static class SupportedLanguages
{
    /// <summary>Languages episodes can be generated in.</summary>
    public static readonly IReadOnlySet<string> Target = new HashSet<string> { "de" };

    /// <summary>Learner native languages (used for future translations and UI hints).</summary>
    public static readonly IReadOnlySet<string> Native = new HashSet<string> { "en", "es" };
}
