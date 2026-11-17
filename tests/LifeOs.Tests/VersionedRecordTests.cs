using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR136: every record kept as versions is one record to the change feed. A view over
/// <c>bsk.event</c> that keeps the newest version of something by <c>recorded_at</c> reads a
/// versioned record, and <c>bsk.event_record</c> must name it, or <c>recent_changes</c>
/// (<c>ChangeReader</c>) shows each superseded version, voided ones too, as a change of its own.
/// Exception days and workout notes were left out of it, and AGENTS.md's "a new kind of them also
/// goes in bsk.event_record" was the only thing that said so. This reads the catalog: each payload
/// key such a view reads (<c>jsonb_exists(e.payload, 'key')</c>) is in <c>bsk.event_record</c>,
/// is Oura's (named by its external id), or is listed here with why.
/// </summary>
public sealed partial class VersionedRecordTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Payload keys a newest-version view reads that aren't versions of one record, with why.</summary>
    private static readonly Dictionary<string, string> NotVersions = new(StringComparer.Ordinal)
    {
        ["timer"] = "A Task's timer is a state machine: each start, pause and stop is a change of its own.",
        ["occurrence"] = "A habit's check-off for a day is an action of its own: each one, and each correction, pays or reverses XP.",
    };

    [Fact]
    public async Task Every_record_a_view_keeps_the_newest_version_of_is_named_by_event_record()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var views = (await connection.QueryAsync<(string Name, string Definition)>(new CommandDefinition("""
            SELECT DISTINCT v.relname AS name, pg_get_viewdef(v.oid) AS definition
            FROM pg_depend d
            JOIN pg_rewrite r ON r.oid = d.objid
            JOIN pg_class v ON v.oid = r.ev_class
            WHERE d.refobjid = 'bsk.event'::regclass AND v.relkind = 'v'
            ORDER BY 1;
            """, cancellationToken: Ct))).AsList();
        var recordRule = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT pg_get_functiondef('bsk.event_record(text, text, jsonb)'::regprocedure);", cancellationToken: Ct));

        var versioned = views.Where(v => NewestByRecordedAt().IsMatch(v.Definition)).ToList();
        Assert.Contains(versioned, v => v.Name == "v_meal");
        var unnamed = versioned
            .SelectMany(v => PayloadKey().Matches(v.Definition).Select(m => (View: v.Name, Key: m.Groups[1].Value)))
            .Where(x => !x.Key.StartsWith("oura_", StringComparison.Ordinal))
            .Where(x => !NotVersions.ContainsKey(x.Key))
            .Where(x => !recordRule!.Contains($"payload->'{x.Key}'", StringComparison.Ordinal)
                        && !recordRule.Contains($"payload -> '{x.Key}'", StringComparison.Ordinal))
            .Select(x => $"{x.View}: {x.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(unnamed);
    }

    [GeneratedRegex(@"recorded_at DESC")]
    private static partial Regex NewestByRecordedAt();

    [GeneratedRegex(@"jsonb_exists\(\w+\.payload, '(\w+)'")]
    private static partial Regex PayloadKey();
}
