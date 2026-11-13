using System.Text.Json;
using Dapper;
using LifeOs.Api.Read;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Every index on the event log has a read it was made for, and the planner uses it for that read.
/// An expression index whose expression differs from the query's by a cast or an operator is
/// silently never used, and a filter Postgres can't push below a view's window or a materialized
/// step only filters after the whole log is read. So each read is planned with sequential scans off
/// and must use its index; a lookup (one entry, one key, one range) must use the index's condition,
/// which a full scan of the index, what the views did before migration 0066, doesn't (code review
/// CR77). An index added without a read here fails <see cref="Every_index_on_the_event_log_has_a_read_here"/>.
/// </summary>
public sealed class EventIndexTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private const string Id = "00000000-0000-0000-0000-000000000001";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A read of the log, the index it reads through, and whether it looks up by that index's key;
    /// with <paramref name="By"/>, the index's condition must bound that column. Where more than one
    /// index can serve it, the planner's pick on an empty log may be either.
    /// </summary>
    private sealed record Read(string Name, string Sql, string Index, bool Lookup, string By = "", params string[] Or)
    {
        public IEnumerable<string> Indexes => [Index, .. Or];
    }

    /// <summary>The recap's window, a week of home-zone days.</summary>
    private static string Recap(string sql)
        => sql.Replace("@zone", "'America/Los_Angeles'", StringComparison.Ordinal)
            .Replace("@from", "'2026-11-02'", StringComparison.Ordinal)
            .Replace("@to", "'2026-11-08'", StringComparison.Ordinal);

    private static readonly Read[] Reads =
    [
        new("habit check-offs", "SELECT * FROM bsk_derived.habit_occurrence_source;", "event_activity_occurrence_idx", false),
        // bsk.is_archived makes the same lookup, but a function with a subquery is never inlined, so its plan isn't shown.
        new("whether each metric is archived", "SELECT * FROM bsk.v_metric;", "event_archive_change_subject", true),
        // Code review CR33 (migration 0060): the event that carries a stored file.
        new("a stored file's event", $"SELECT * FROM bsk.v_artifact WHERE id = '{Id}';", "event_artifact_idx", true),
        new("exception days", "SELECT * FROM bsk.v_exception_day;", "event_exception_day_idx", false),
        // Code review CR32 (migration 0059): the food views carry their partial indexes' condition.
        new("every fast", "SELECT * FROM bsk.v_fast;", "event_fast_idx", false),
        new("one fast", $"SELECT * FROM bsk.v_fast WHERE fast_key = '{Id}';", "event_fast_idx", true),
        new("the meals during fasts", "SELECT * FROM bsk.v_fast;", "event_meal_idx", false),
        new("food days", "SELECT * FROM bsk.v_food_day;", "event_food_day_idx", false),
        new("one kind over a time range",
            "SELECT id FROM bsk.event WHERE kind = 'note' AND occurred_at >= now() - interval '7 days' ORDER BY occurred_at DESC LIMIT 20;",
            "event_kind_occurred_idx", true),
        // The recap's window is a range of times, so it bounds the index's scan (code review CR77).
        new("the recap's status changes", Recap(ReviewReader.RecapStatusSql), "event_kind_occurred_idx", true, "occurred_at", "event_state_change_subject"),
        new("the recap's completed Tasks", Recap(ReviewReader.RecapCompletedSql), "event_kind_occurred_idx", true, "occurred_at", "event_state_change_subject"),
        new("the recap's journal", Recap(ReviewReader.RecapJournalSql), "event_kind_occurred_idx", true, "occurred_at"),
        new("the recap's Inbox history", Recap(ReviewReader.RecapInboxSql), "event_kind_occurred_idx", true, "occurred_at", "event_triage_item"),
        new("one lab visit", $"SELECT * FROM bsk.v_lab_visit WHERE visit_key = '{Id}';", "event_lab_visit_idx", true),
        // Code review CR34 (migration 0061): the ledger by key, time, subject and trigger.
        new("whether an award is paid", "SELECT currency, sum(amount) FROM bsk.v_ledger WHERE award_key = 'completed:x' GROUP BY currency;",
            "event_ledger_award_key_idx", true),
        new("the ledger over a range",
            "SELECT * FROM bsk.v_ledger WHERE occurred_at >= '2026-11-01' AND occurred_at < '2026-11-08';", "event_ledger_occurred_idx", true),
        new("a Reward's redeems", "SELECT * FROM bsk.v_reward;", "event_ledger_subject_idx", true),
        new("a subject's ledger", $"SELECT * FROM bsk.v_ledger WHERE subject_id = '{Id}';", "event_ledger_subject_uuid_idx", true),
        new("the ledger by trigger", "SELECT DISTINCT award_key FROM bsk.v_ledger WHERE trigger = 'domain_rank';", "event_ledger_trigger_idx", true),
        new("the ledger by several triggers",
            "SELECT e.id FROM bsk.event e WHERE e.kind = 'ledger' AND e.payload->>'award_key' IS NOT NULL AND e.payload->>'trigger' = ANY('{food_day,food_meal}');",
            "event_ledger_trigger_idx", true),
        new("every meal", "SELECT * FROM bsk.v_meal;", "event_meal_idx", false),
        new("one meal", $"SELECT * FROM bsk.v_meal WHERE meal_key = '{Id}';", "event_meal_idx", true),
        new("one metric reading", $"SELECT * FROM bsk.v_metric_reading WHERE reading_key = '{Id}';", "event_metric_reading_idx", true),
        new("Oura activity days", "SELECT * FROM bsk.v_activity_day;", "event_oura_activity_idx", false),
        new("Oura readiness days", "SELECT * FROM bsk.v_readiness_day;", "event_oura_readiness_idx", false),
        new("Oura sleep days", "SELECT * FROM bsk.v_sleep_day;", "event_oura_sleep_day_idx", false),
        new("Oura sleep periods", "SELECT * FROM bsk.v_sleep_period;", "event_oura_sleep_idx", false),
        new("Oura tags", "SELECT * FROM bsk.v_oura_tag;", "event_oura_tag_idx", false),
        new("Oura workouts", "SELECT * FROM bsk.v_workout;", "event_oura_workout_idx", false),
        new("workout notes", "SELECT * FROM bsk.v_workout;", "event_workout_note_idx", false),
        new("the change feed",
            "SELECT id FROM bsk.v_event WHERE recorded_at >= now() - interval '1 day' ORDER BY recorded_at DESC LIMIT 50;",
            "event_recorded_at_idx", true),
        // Code review CR68: a retried request finds its first write by source and request id.
        new("a retried request's first write",
            NpgsqlEventReader.FindWriteSql
                .Replace("@sourceId", "'mcp:agent'", StringComparison.Ordinal)
                .Replace("@externalId", "'request-1'", StringComparison.Ordinal),
            "event_source_external_key", true),
        new("one space photo", $"SELECT * FROM bsk.v_space_photo WHERE photo_key = '{Id}';", "event_space_photo_idx", true),
        new("one space rating", $"SELECT * FROM bsk.v_space_rating WHERE rating_key = '{Id}';", "event_space_rating_idx", true),
        new("a subject's status", $"SELECT * FROM bsk_derived.subject_current_source WHERE subject_id = '{Id}';",
            "event_state_change_subject", true),
        new("one task's timer", $"SELECT * FROM bsk.v_task_timer WHERE subject_key = '{Id}';", "event_timer_subject_idx", true),
        // Code review CR63: a capture sent again asks whether it was ever triaged.
        new("whether an item was ever triaged",
            NpgsqlEventReader.EverTriagedSql.Replace("@item", $"'{Id}'", StringComparison.Ordinal), "event_triage_item", true),
    ];

    /// <summary>Indexes no read in the code uses, and why each is kept.</summary>
    private static readonly Dictionary<string, string> Unread = new(StringComparer.Ordinal)
    {
        ["event_pkey"] = "the primary key",
        ["event_payload_gin"] = "for containment queries (payload @> ...) from psql and BI, which the code doesn't make",
    };

    public static TheoryData<string> ReadNames => new(Reads.Select(r => r.Name));

    [Fact]
    public async Task Every_index_on_the_event_log_has_a_read_here()
    {
        await using var connection = await OpenAsync();
        var indexes = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'bsk' AND tablename = 'event';",
            cancellationToken: Ct))).ToHashSet(StringComparer.Ordinal);
        var named = Reads.SelectMany(r => r.Indexes).Concat(Unread.Keys).ToHashSet(StringComparer.Ordinal);

        Assert.True(indexes.SetEquals(named),
            $"Indexes with no read here: {string.Join(", ", indexes.Except(named))}. "
            + $"Named here but not on bsk.event: {string.Join(", ", named.Except(indexes))}.");
    }

    [Theory]
    [MemberData(nameof(ReadNames))]
    public async Task Each_read_uses_its_index(string name)
    {
        var read = Reads.Single(r => r.Name == name);
        var (scans, plan) = await PlanAsync(read.Sql);
        var through = scans.Where(s => read.Indexes.Contains(s.Index)).ToList();

        Assert.True(through.Count > 0, $"{name} doesn't read through {string.Join(" or ", read.Indexes)}:\n{plan}");
        if (read.Lookup)
        {
            var whole = through.FirstOrDefault(s => s.Cond is null || !s.Cond.Contains(read.By, StringComparison.Ordinal));
            Assert.True(whole is null, $"{name} scans all of {whole?.Index} instead of looking up by it:\n{plan}");
        }
    }

    [Fact]
    public async Task Fasts_count_their_meals_from_one_fold_of_the_food_log()
    {
        var (scans, plan) = await PlanAsync("SELECT * FROM bsk.v_fast;");
        Assert.True(scans.Count(s => s.Index == "event_meal_idx") == 1, plan);
    }

    /// <summary>A scan of an index, and the condition it looks up by (null when it reads the whole index).</summary>
    private sealed record Scan(string Index, string? Cond);

    /// <summary>Every index scan in the query's plan, and the plan as text for a failure to show.</summary>
    private async Task<(IReadOnlyList<Scan> Scans, string Plan)> PlanAsync(string query)
    {
        await using var connection = await OpenAsync();
        var json = await connection.QuerySingleAsync<string>(new CommandDefinition($"EXPLAIN (FORMAT JSON) {query}", cancellationToken: Ct));
        var text = string.Join('\n', await connection.QueryAsync<string>(new CommandDefinition($"EXPLAIN {query}", cancellationToken: Ct)));
        using var document = JsonDocument.Parse(json);
        var scans = new List<Scan>();
        Walk(document.RootElement[0].GetProperty("Plan"), scans);
        return (scans, text);
    }

    private static void Walk(JsonElement node, List<Scan> scans)
    {
        if (node.TryGetProperty("Index Name", out var index))
        {
            scans.Add(new Scan(index.GetString()!, node.TryGetProperty("Index Cond", out var cond) ? cond.GetString() : null));
        }
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, scans);
            }
        }
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition("SET enable_seqscan = off;", cancellationToken: Ct));
        return connection;
    }
}
