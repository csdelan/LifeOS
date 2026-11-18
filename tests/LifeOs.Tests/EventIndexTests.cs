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

    /// <summary>An item's history as the web reads it, with no agents known.</summary>
    private static string History()
        => SubjectReader.HistorySql
            .Replace("@agentSources", "'{}'", StringComparison.Ordinal)
            .Replace("@agentPeople", "'{}'", StringComparison.Ordinal)
            .Replace("@agentNames", "'{}'", StringComparison.Ordinal)
            .Replace("@agentPrefix", "'mcp:'", StringComparison.Ordinal)
            .Replace("@key", $"'{Id}'", StringComparison.Ordinal)
            .Replace("@id", $"'{Id}'", StringComparison.Ordinal)
            .Replace("@limit", "50", StringComparison.Ordinal);

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
        // Focus plan G1 (migration 0074): the Primary focus, by its level.
        new("the Primary focus", "SELECT * FROM bsk.v_focus WHERE level = 'weekly';", "event_focus_idx", true),
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
        // Code review CR78: what the Food Log and the Activity page show paid, by their days' keys.
        new("what some days' awards pay",
            NpgsqlLedgerRepository.AwardsByKeySql.Replace("@keys", "ARRAY['diet:2026-11-08','fast:2026-11-08']", StringComparison.Ordinal),
            "event_ledger_award_key_idx", true),
        // Code review CR123: a write's own awards' credit, read before and after it in place of all of history.
        new("some awards' credit by day",
            NpgsqlDomainCreditReader.AwardCreditSql
                .Replace("@tz", "'America/Los_Angeles'", StringComparison.Ordinal)
                .Replace("@currencies", "ARRAY['health_xp','environment_xp']", StringComparison.Ordinal)
                .Replace("@passive", "ARRAY['sleep']", StringComparison.Ordinal)
                .Replace("@keys", "ARRAY['completed:x','habit_day:y']", StringComparison.Ordinal),
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
        // Code review CR128: a range's meals are picked by when their versions happened, then folded by key.
        new("a range's meals", Recap(NpgsqlFoodLogRepository.MealsInRangeSql), "event_kind_occurred_idx", true, "occurred_at", "event_occurred_at_idx"),
        new("one metric reading", $"SELECT * FROM bsk.v_metric_reading WHERE reading_key = '{Id}';", "event_metric_reading_idx", true),
        new("Oura activity days", "SELECT * FROM bsk.v_activity_day;", "event_oura_activity_idx", false),
        // Code review CR128 (migration 0072): a week of the days Oura keys by day, by that key.
        new("a week of Oura activity days", Recap("SELECT * FROM bsk.v_activity_day WHERE day_key BETWEEN @from AND @to;"),
            "event_oura_activity_idx", true),
        new("Oura readiness days", "SELECT * FROM bsk.v_readiness_day;", "event_oura_readiness_idx", false),
        new("a week of Oura readiness days", Recap("SELECT * FROM bsk.v_readiness_day WHERE day_key BETWEEN @from AND @to;"),
            "event_oura_readiness_idx", true),
        new("Oura sleep days", "SELECT * FROM bsk.v_sleep_day;", "event_oura_sleep_day_idx", false),
        new("Oura sleep periods", "SELECT * FROM bsk.v_sleep_period;", "event_oura_sleep_idx", false),
        new("Oura tags", "SELECT * FROM bsk.v_oura_tag;", "event_oura_tag_idx", false),
        new("Oura workouts", "SELECT * FROM bsk.v_workout;", "event_oura_workout_idx", false),
        new("one workout", $"SELECT * FROM bsk.v_workout WHERE workout_id = '{Id}';", "event_oura_workout_idx", true),
        new("workout notes", "SELECT * FROM bsk.v_workout;", "event_workout_note_idx", false),
        new("the change feed",
            "SELECT id FROM bsk.v_event WHERE recorded_at >= now() - interval '1 day' ORDER BY recorded_at DESC LIMIT 50;",
            "event_recorded_at_idx", true),
        // Code review CR122 (migration 0070): the change feed by when it happened, and whether a
        // version is its record's newest; ChangeFeedPlanTests plans the feed's own query.
        new("the change feed by when it happened",
            "SELECT id FROM bsk.event WHERE occurred_at >= now() - interval '1 day' ORDER BY occurred_at DESC LIMIT 50;",
            "event_occurred_at_idx", true, "occurred_at"),
        new("a record's newer versions",
            $"SELECT 1 FROM bsk.event n WHERE bsk.event_record(n.source_id, n.external_id, n.payload) = 'meal:{Id}' AND (n.recorded_at, n.id) > (now(), '{Id}'::uuid);",
            "event_record_idx", true),
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
        // Agent-access plan §5.6 step 2.4: an item's history, its status changes by this index.
        new("an item's history", History(), "event_state_change_subject", true),
        new("an item's due awards", $"SELECT * FROM bsk.v_reward_due WHERE subject_id = '{Id}';", "event_state_change_subject", true),
        new("one review", $"SELECT * FROM bsk.v_review WHERE id = '{Id}';", "event_state_change_subject", true),
        new("one space", $"SELECT * FROM bsk.v_space WHERE id = '{Id}';", "event_archive_change_subject", true),
        new("a subject's events", $"SELECT * FROM bsk.v_subject_event WHERE subject_id = '{Id}';", "event_pkey", true),
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

    /// <summary>
    /// Every view that reads the log: the reads here that look it up by its key or a range of it,
    /// or why it's read whole (code review CR128, fifth review R12). HotPathBudgetTests measures
    /// what the whole ones cost on three years of use.
    /// </summary>
    private static readonly Dictionary<string, ViewReads> Views = new(StringComparer.Ordinal)
    {
        ["bsk.v_activity_day"] = Bounded("a week of Oura activity days"),
        ["bsk.v_artifact"] = Bounded("a stored file's event"),
        ["bsk.v_award_credit"] = Bounded("some awards' credit by day"),
        ["bsk.v_event"] = Bounded("the change feed"),
        ["bsk.v_exception_day"] = Whole("the domain fold reads every exception day, by design: a few a year"),
        ["bsk.v_fast"] = Bounded("one fast"),
        ["bsk.v_focus"] = Bounded("the Primary focus"),
        ["bsk.v_food_day"] = Whole(
            "it groups every meal and fast, each folded by its id, by its day; a range of days needs a pre-select of "
            + "the range's meals and fasts (HotPathBudgetTests lists the Food Log week over budget)"),
        ["bsk.v_inbox"] = Whole(
            "it folds every capture's newest triage to find what waits; bounding it needs a projection beside the log "
            + "(HotPathBudgetTests lists the Inbox count over budget)"),
        ["bsk.v_inbox_resolved"] = Whole("the same fold as v_inbox, for the Resolved list"),
        ["bsk.v_lab_visit"] = Bounded("one lab visit"),
        ["bsk.v_ledger"] = Bounded("whether an award is paid", "the ledger over a range", "a subject's ledger", "the ledger by trigger"),
        ["bsk.v_meal"] = Bounded("one meal", "a range's meals"),
        ["bsk.v_metric"] = Bounded("whether each metric is archived"),
        ["bsk.v_metric_reading"] = Bounded("one metric reading"),
        ["bsk.v_oura_tag"] = Whole("folded by Oura's id and read by day, so a range folds every tag: a few a month"),
        ["bsk.v_readiness_day"] = Bounded("a week of Oura readiness days"),
        ["bsk.v_review"] = Bounded("one review"),
        ["bsk.v_reward"] = Bounded("a Reward's redeems"),
        ["bsk.v_reward_trigger"] = Bounded("an item's due awards"),
        ["bsk.v_sleep_day"] = Whole(
            "it groups every sleep period, each folded by Oura's id, by its day (HotPathBudgetTests lists the "
            + "Activity week over budget)"),
        ["bsk.v_sleep_period"] = Whole("folded by Oura's id and read by day, for v_sleep_day and a day's periods"),
        ["bsk.v_space"] = Bounded("one space"),
        ["bsk.v_space_photo"] = Bounded("one space photo"),
        ["bsk.v_space_rating"] = Bounded("one space rating"),
        ["bsk.v_subject_event"] = Bounded("a subject's events"),
        ["bsk.v_task_timer"] = Bounded("one task's timer"),
        ["bsk.v_workout"] = Bounded("one workout"),
        ["bsk_derived.habit_occurrence_source"] = Whole(
            "every habit's check-offs: a habit's streaks and its rewards read its whole history by design"),
        ["bsk_derived.habit_planned_source"] = Whole("each Habit's Planned spells, from its state changes: a few a habit"),
        ["bsk_derived.subject_current_source"] = Bounded("a subject's status"),
    };

    /// <summary>A view's reads by key here, or why it has none.</summary>
    private sealed record ViewReads(IReadOnlyList<string> Lookups, string? Why);

    private static ViewReads Bounded(params string[] lookups) => new(lookups, null);

    private static ViewReads Whole(string why) => new([], why);

    public static TheoryData<string> ReadNames => new(Reads.Select(r => r.Name));

    [Fact]
    public async Task Every_view_over_the_event_log_is_read_by_key_here_or_says_why_not()
    {
        await using var connection = await OpenAsync();
        var views = (await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT DISTINCT format('%I.%I', vn.nspname, v.relname)
            FROM pg_depend d
            JOIN pg_rewrite r ON r.oid = d.objid
            JOIN pg_class v ON v.oid = r.ev_class
            JOIN pg_namespace vn ON vn.oid = v.relnamespace
            WHERE d.classid = 'pg_rewrite'::regclass AND d.refclassid = 'pg_class'::regclass
              AND d.refobjid = 'bsk.event'::regclass AND v.relkind IN ('v', 'm');
            """, cancellationToken: Ct))).ToHashSet(StringComparer.Ordinal);

        Assert.True(views.SetEquals(Views.Keys),
            $"Views over bsk.event not listed here: {string.Join(", ", views.Except(Views.Keys))}. "
            + $"Listed here but not over bsk.event: {string.Join(", ", Views.Keys.Except(views))}.");
        var lookups = Reads.Where(r => r.Lookup).Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(Views, v => Assert.True(
            v.Value.Why is { Length: > 0 }
                ? v.Value.Lookups.Count == 0
                : v.Value.Lookups.Count > 0 && v.Value.Lookups.All(lookups.Contains),
            $"{v.Key} names no lookup that's a read here, nor why it's read whole."));
    }

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
