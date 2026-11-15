using System.Text.Json;
using Dapper;
using LifeOs.Api.Read;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The change feed (<c>recent_changes</c>) reads a page, not its window (code review CR122, fifth
/// review R17). It built every event of up to 366 days and folded every record's versions before
/// taking 50. Over a log of made-up events, a year of them with Oura records re-sent and meals
/// edited, each order's plan is a limit above its time's index scan with nothing between that
/// reads the whole window, and each version looks up its record's newer ones by
/// <c>event_record_idx</c>. Paged to the end, the feed is the plain fold over the whole log.
/// </summary>
public sealed class ChangeFeedPlanTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HashSet<string> NoneOff = [];

    /// <summary>What may stand between the page's limit and its scan: each passes rows on as they come, in order.</summary>
    private static readonly HashSet<string> Streaming = ["Nested Loop", "Incremental Sort", "Subquery Scan", "Result", "Index Scan", "Index Only Scan"];

    /// <summary>
    /// 14,000 standalone events, 2,000 Oura records sent three times each and 1,000 meals edited
    /// once, over the last year or so: made up, not real data.
    /// </summary>
    private static async Task<DomainTestDatabase> VolumeAsync(PostgresFixture postgres)
    {
        var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        await owner.OpenAsync(Ct);
        await owner.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            SELECT 'observation', 'declared', now() - i * interval '30 minutes', now() - i * interval '30 minutes',
                   'cli', jsonb_build_object('fixture', i)
            FROM generate_series(1, 14000) i;

            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
            SELECT 'measurement', 'observed', now() - r * interval '4 hours' - interval '1 day',
                   now() - r * interval '4 hours' + v * interval '1 minute', 'oura',
                   format('daily_activity:fixture-%s:%s:%s', r, v, md5(r || ':' || v)),
                   jsonb_build_object('fixture', r, 'version', v)
            FROM generate_series(1, 2000) r, generate_series(1, 3) v;

            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            SELECT 'measurement', 'declared', now() - r * interval '7 hours',
                   now() - r * interval '7 hours' - interval '2 days' + v * interval '1 day', 'cli',
                   jsonb_build_object('meal', jsonb_build_object('id', md5('meal' || r)::uuid, 'name', 'Meal ' || r || '.' || v, 'score', 3))
            FROM generate_series(1, 1000) r, generate_series(1, 2) v;

            ANALYZE bsk.event;
            """,
            cancellationToken: Ct));
        return db;
    }

    [Fact]
    public async Task Each_order_reads_a_page_through_its_times_index_and_looks_up_newer_versions_by_record()
    {
        await using var db = await VolumeAsync(postgres);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var since = DateTimeOffset.UtcNow.AddDays(-366);
        var cursor = new ChangeCursor(DateTimeOffset.UtcNow.AddDays(-100), Guid.Empty);

        foreach (var (byOccurred, index) in new[] { (false, "event_recorded_at_idx"), (true, "event_occurred_at_idx") })
        {
            foreach (var (before, off) in new (ChangeCursor? Before, HashSet<string> Off)[] { (null, NoneOff), (cursor, NoneOff), (null, ["health"]) })
            {
                var (sql, parameters) = ChangeReader.Query(since, null, off, 50, byOccurred, before);
                var json = await connection.QuerySingleAsync<string>(new CommandDefinition($"EXPLAIN (FORMAT JSON) {sql}", parameters, cancellationToken: Ct));
                using var plan = JsonDocument.Parse(json);
                var root = plan.RootElement[0].GetProperty("Plan");
                var shown = $"by {(byOccurred ? "occurred" : "recorded")}{(before is null ? "" : ", after a cursor")}{(off.Count > 0 ? ", health off" : "")}:\n{json}";

                // The page: a limit, then only what streams, down to the time's index scan, bounded by since.
                var path = PathTo(root, n => Name(n, "Index Name") == index);
                Assert.True(path is not null, $"No scan of {index}, {shown}");
                var limit = path!.FindLastIndex(n => Name(n, "Node Type") == "Limit");
                Assert.True(limit >= 0, $"No limit above the scan of {index}, {shown}");
                var between = path.Skip(limit + 1).Select(n => Name(n, "Node Type")).Where(t => !Streaming.Contains(t!)).ToList();
                Assert.True(between.Count == 0, $"{string.Join(", ", between)} between the limit and the scan reads the whole window, {shown}");
                var scan = path[^1];
                Assert.Equal("Backward", Name(scan, "Scan Direction"));
                Assert.Contains(byOccurred ? "occurred_at" : "recorded_at", Name(scan, "Index Cond") ?? "", StringComparison.Ordinal);
                if (before is not null)
                {
                    Assert.Contains("<=", Name(scan, "Index Cond") ?? "", StringComparison.Ordinal);
                }

                // Every other read of the log looks a record up by its key: its newer versions, and the page's counts.
                var events = Nodes(root).Where(n => Name(n, "Relation Name") == "event").ToList();
                // A bitmap scan of the log reads through the index scans under it.
                var others = events.Where(n => Name(n, "Index Name") != index)
                    .SelectMany(n => Name(n, "Node Type") == "Bitmap Heap Scan" ? Nodes(n).Where(b => Name(b, "Index Name") is not null) : [n])
                    .ToList();
                Assert.True(
                    others.Count > 0 && others.All(n => Name(n, "Index Name") == "event_record_idx" && Name(n, "Index Cond") is not null),
                    "A read of the log that isn't the page's scan or a lookup by record: "
                    + string.Join("; ", others.Select(n => $"{Name(n, "Node Type")} {Name(n, "Index Name")}{(Name(n, "Index Cond") is null ? " with no condition" : "")}"))
                    + $", {shown}");
            }
        }
    }

    [Fact]
    public async Task Paged_to_the_end_the_feed_is_each_records_newest_version_once_and_every_other_event()
    {
        await using var db = await VolumeAsync(postgres);
        var changes = TestReaders.Changes(db.ConnectionString);
        var since = DateTimeOffset.UtcNow.AddDays(-200);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var byOccurred in new[] { false, true })
        {
            var at = byOccurred ? "occurred_at" : "recorded_at";
            // The plain fold: each record's newest version by recorded_at, wherever its time falls,
            // then the window and the order.
            var expected = (await connection.QueryAsync<Guid>(new CommandDefinition(
                $"""
                SELECT id FROM (
                    SELECT DISTINCT ON (coalesce(bsk.event_record(source_id, external_id, payload), id::text))
                           id, kind, occurred_at, recorded_at
                    FROM bsk.event
                    ORDER BY coalesce(bsk.event_record(source_id, external_id, payload), id::text), recorded_at DESC, id DESC
                ) newest
                WHERE {at} >= @since AND kind <> 'ledger'
                ORDER BY {at} DESC, id DESC;
                """,
                new { since },
                cancellationToken: Ct))).ToList();

            var paged = new List<Guid>();
            ChangeCursor? before = null;
            while (true)
            {
                var rows = await changes.GetChangesAsync(since, null, NoneOff, 500, byOccurred, before, Ct);
                paged.AddRange(rows.Take(500).Select(r => r.Id));
                if (rows.Count <= 500)
                {
                    break;
                }

                var last = rows[499];
                before = new ChangeCursor(new DateTimeOffset(byOccurred ? last.OccurredAt : last.RecordedAt, TimeSpan.Zero), last.Id);
            }

            Assert.True(expected.Count > 1000, $"The window holds {expected.Count}.");
            Assert.True(expected.SequenceEqual(paged), $"By {at}: {expected.Count} in the fold, {paged.Count} paged, first apart at {expected.Zip(paged).TakeWhile(p => p.First == p.Second).Count()}.");
        }

        // A record's versions are counted, its newest the one shown.
        var newest = (await changes.GetChangesAsync(DateTimeOffset.UtcNow.AddHours(-12), null, NoneOff, 200, cancellationToken: Ct))
            .Where(r => r.Source == "oura").ToList();
        Assert.NotEmpty(newest);
        Assert.All(newest, r => Assert.Equal(3, r.Versions));
        Assert.All(newest, r => Assert.Contains("\"version\": 3", r.Payload ?? "", StringComparison.Ordinal));
    }

    private static string? Name(JsonElement node, string property)
        => node.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                foreach (var below in Nodes(child))
                {
                    yield return below;
                }
            }
        }
    }

    /// <summary>The nodes from the root down to the first that matches, or null when none does.</summary>
    private static List<JsonElement>? PathTo(JsonElement node, Func<JsonElement, bool> match)
    {
        if (match(node))
        {
            return [node];
        }

        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                if (PathTo(child, match) is { } below)
                {
                    below.Insert(0, node);
                    return below;
                }
            }
        }

        return null;
    }
}
