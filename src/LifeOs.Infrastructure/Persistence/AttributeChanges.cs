using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The <c>attribute_change</c> event (code review CR24, migration 0057): what a rename or an
/// attribute patch changed on a subject, from and to, appended in the transaction that makes the
/// change. A key that was added has no <c>from</c>; one that was removed has no <c>to</c>.
/// </summary>
internal static class AttributeChanges
{
    /// <summary>A title change, or null when the title stays the same.</summary>
    public static JsonObject? Title(string from, string to)
        => string.Equals(from, to, StringComparison.Ordinal)
            ? null
            : new JsonObject { ["title"] = new JsonObject { ["from"] = from, ["to"] = to } };

    /// <summary>
    /// What <paramref name="patchJson"/>, merged over the top-level keys, then
    /// <paramref name="removed"/> do to <paramref name="beforeJson"/>, as
    /// <c>(attributes || patch) - removed</c> does: each key that changed. Null when none did.
    /// </summary>
    public static JsonObject? Attributes(string? beforeJson, string patchJson, IReadOnlyCollection<string> removed)
    {
        var before = Parse(beforeJson);
        var patch = Parse(patchJson);
        var changes = new JsonObject();
        foreach (var key in patch.Select(p => p.Key).Concat(removed).Distinct(StringComparer.Ordinal))
        {
            var had = before.TryGetPropertyValue(key, out var from);
            JsonNode? to = null;
            var has = !removed.Contains(key) && patch.TryGetPropertyValue(key, out to);
            if ((!had && !has) || (had && has && JsonNode.DeepEquals(from, to)))
            {
                continue;
            }

            var change = new JsonObject();
            if (had)
            {
                change["from"] = from?.DeepClone();
            }

            if (has)
            {
                change["to"] = to?.DeepClone();
            }

            changes[key] = change;
        }

        return changes.Count == 0 ? null : new JsonObject { ["attributes"] = changes };
    }

    /// <summary>Appends <paramref name="change"/> to <paramref name="subjectId"/>'s log, in <paramref name="transaction"/>.</summary>
    public static async Task AppendAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subjectId, JsonObject change,
        EventSource source, IClock clock, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["subject_id"] = subjectId.ToString() };
        foreach (var (key, value) in change)
        {
            payload[key] = value?.DeepClone();
        }

        var now = clock.UtcNow;
        var eventId = await EventSql.InsertAsync(
            connection,
            new NewEvent(EventKinds.AttributeChange, Provenances.Declared, now, now, source.Id, PayloadJson: payload.ToJsonString()),
            transaction,
            cancellationToken);
        await EventSql.InsertEdgesAsync(
            connection, eventId, [new SubjectEventEdge(subjectId, SubjectEventRelations.Concerns, Provenances.Declared)],
            transaction, cancellationToken);
    }

    private static JsonObject Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? [];
}
