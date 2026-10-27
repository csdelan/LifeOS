using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Home;

/// <summary>
/// A space's <c>shots</c> attribute (HOME-4, environment plan §8.3): an array of
/// <c>{id, name?, baseline?}</c>, written only by <see cref="SpaceService"/>. Read tolerantly, so
/// a hand-edited attribute can't break a page: an entry without a usable id is skipped, a
/// repeated id keeps its first entry, a blank name reads as none (the space's own), and a
/// baseline that isn't a uuid reads as none (the shot's first photo).
/// </summary>
public static class SpaceShotsJson
{
    /// <summary>The shots in <paramref name="json"/>, in order; empty when there are none or it isn't an array.</summary>
    public static IReadOnlyList<SpaceShot> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        if (node is not JsonArray array)
        {
            return [];
        }

        var shots = new List<SpaceShot>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var id = Text(item["id"]);
            if (id is null || id.Length > 40 || shots.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = Text(item["name"]);
            Guid? baseline = Guid.TryParse(Text(item["baseline"]), out var b) ? b : null;
            shots.Add(new SpaceShot(id, name, baseline));
        }

        return shots;
    }

    /// <summary>The attribute's value for <paramref name="shots"/>: names and baselines only where set.</summary>
    public static JsonArray ToJson(IEnumerable<SpaceShot> shots)
    {
        var array = new JsonArray();
        foreach (var shot in shots)
        {
            var item = new JsonObject { ["id"] = shot.Id };
            if (shot.Name is not null)
            {
                item["name"] = shot.Name;
            }

            if (shot.Baseline is { } baseline)
            {
                item["baseline"] = baseline.ToString();
            }

            array.Add(item);
        }

        return array;
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
