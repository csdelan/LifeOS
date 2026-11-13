using System.Text.Json.Nodes;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The <c>attribute_change</c> event (code review CR24, migration 0057): what a rename or an
/// attribute patch changed on a subject, from and to, appended in the transaction that makes the
/// change. A key that was added has no <c>from</c>; one that was removed has no <c>to</c>.
/// <para>
/// Every in-place change to a subject's row leaves one: a change asked for is
/// <c>declared</c>; one that follows from it (a repetition redated by its series' new pattern, an
/// open repetition or upcoming occurrence taking its series' new title or field) is
/// <c>derived</c>, on the subject it changed, and cites the series' own change (code review CR51).
/// </para>
/// </summary>
internal static class AttributeChanges
{
    /// <summary>A title change, or null when the title stays the same.</summary>
    public static JsonObject? Title(string from, string to)
        => string.Equals(from, to, StringComparison.Ordinal)
            ? null
            : new JsonObject { ["title"] = new JsonObject { ["from"] = from, ["to"] = to } };

    /// <summary>One attribute's change from <paramref name="from"/> to <paramref name="to"/>, either absent; null when they're the same.</summary>
    public static JsonObject? Key(string key, string? from, string? to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return null;
        }

        var change = new JsonObject();
        if (from is not null)
        {
            change["from"] = from;
        }

        if (to is not null)
        {
            change["to"] = to;
        }

        return new JsonObject { ["attributes"] = new JsonObject { [key] = change } };
    }

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

    /// <summary>
    /// Appends <paramref name="change"/> to <paramref name="subjectId"/>'s log, in
    /// <paramref name="transaction"/>, and answers its id: <c>declared</c>, or, with
    /// <paramref name="followed"/>, <c>derived</c> from that change, which it cites.
    /// </summary>
    public static async Task<Guid> AppendAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subjectId, JsonObject change,
        EventSource source, IClock clock, CancellationToken cancellationToken, Guid? followed = null)
    {
        var provenance = followed is null ? Provenances.Declared : Provenances.Derived;
        var payload = new JsonObject { ["subject_id"] = subjectId.ToString() };
        foreach (var (key, value) in change)
        {
            payload[key] = value?.DeepClone();
        }

        var now = clock.UtcNow;
        var eventId = await EventSql.InsertAsync(
            connection,
            new NewEvent(
                EventKinds.AttributeChange, provenance, now, now, source.Id, PayloadJson: payload.ToJsonString(),
                DerivedFrom: followed is { } cause ? [cause] : null),
            transaction,
            cancellationToken);
        await EventSql.InsertEdgesAsync(
            connection, eventId, [new SubjectEventEdge(subjectId, SubjectEventRelations.Concerns, provenance)],
            transaction, cancellationToken);
        return eventId;
    }

    /// <summary>
    /// The change a follow derives from when it runs apart from it: the newest
    /// <c>attribute_change</c> on <paramref name="subjectId"/> that changed one of
    /// <paramref name="keys"/>, or null when none did (the follow is then recorded as declared).
    /// </summary>
    public static Task<Guid?> CauseAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subjectId, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT e.id
            FROM bsk.subject_event se
            JOIN bsk.event e ON e.id = se.event_id
            WHERE se.subject_id = @subjectId AND se.relation = 'concerns'
              AND e.kind = 'attribute_change'
              AND jsonb_exists_any(e.payload -> 'attributes', @keys)
            ORDER BY e.recorded_at DESC, e.id DESC
            LIMIT 1;
            """,
            new { subjectId, keys = keys.ToArray() }, transaction, cancellationToken: cancellationToken));

    private static JsonObject Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? [];
}
