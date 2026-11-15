using System.Text.Encodings.Web;
using System.Text.Json;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Generation;

/// <summary>Reads and writes <see cref="Season.ArcJson"/>.</summary>
public static class SeasonArcs
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SeasonArcResponse? Read(Season season) =>
        season.ArcJson is null ? null : JsonSerializer.Deserialize<SeasonArcResponse>(season.ArcJson, Json);

    public static string Write(SeasonArcResponse arc) => JsonSerializer.Serialize(arc, Json);
}
