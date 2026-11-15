using Dapper;
using LifeOs.Api.Read;
using LifeOs.Application.Food;
using LifeOs.Application.Home;
using LifeOs.Application.Measurements;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// What the hot paths cost on three years of made-up use (code review R13, R18's query budget):
/// a Food Log week, an Activity week, the Inbox count every page's badge reads, the life-domain
/// fold, the settle the API runs as it starts and the Character page as it opens, and the writes
/// that earn (a check-off, a completed Task, a meal, a room rating, a reading). Each runs on its
/// own copy of the seeded template (<see cref="SeededTemplate"/>) through a kernel that measures
/// (<see cref="QueryMeter"/>), and is held to a budget of statements run, rows of
/// <c>bsk.event</c> read, rows every scan read (which shows a subquery run once per row of
/// another), no read of the whole log, and a time generous enough for CI's slower runners.
/// <para>
/// A read of a range reads that range, so its budget doesn't grow with history; a read of all of
/// history by design (the domain fold, the day awards' settle) is held to one pass over what it
/// reads. A path over its budget is listed in <see cref="Over"/> with what it cost when listed,
/// which it mustn't exceed, and why; the test fails when it comes within its budget, to be taken
/// off the list.
/// </para>
/// </summary>
public sealed class HotPathBudgetTests(PostgresFixture postgres)
{
    private static readonly TimeSpan ReadTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WriteTime = TimeSpan.FromSeconds(3);

    /// <summary>A week of one feature's days: under 1% of the three years here.</summary>
    private static readonly Budget Week = new(Statements: 12, EventRows: 2_500, RowsScanned: 25_000, ReadTime);

    /// <summary>
    /// The paths over their budget, each with what it cost when it was listed and why it's over.
    /// The figures are a little above what it measured (the seed's days move with the date).
    /// </summary>
    private static readonly Dictionary<string, Over> Over = new(StringComparer.Ordinal)
    {
        ["food week"] = new(8, 40_000, 600_000,
            "v_food_day, v_fast and the day awards' ledger read are folded whole and filtered after; the drink days "
            + "look up every fast's meals again"),
        ["activity week"] = new(12, 37_000, 920_000,
            "v_activity_day, v_sleep_day and v_readiness_day are folded whole and filtered after, and v_sleep_day "
            + "joins every sleep period to every day"),
        ["inbox count"] = new(130, 7_100, 11_000,
            "v_inbox folds every capture's triage and asks bsk.is_archived of each; counting only what waits needs a "
            + "projection beside the log"),
        ["settle"] = new(28, 70_000, 5_250_000,
            "v_fast counts the meals during each fast with a subquery per fast, over every meal"),
        ["check-off"] = new(1_280, 35_500, 102_000,
            "v_reward_due asks bsk.ledger_opened_at() of every row it reads, and the awards' credit is read by "
            + "subject, every award the habit ever earned"),
        ["meal"] = new(13, 25_000, 4_300_000,
            "v_fast counts the meals during each fast with a subquery per fast, over every meal"),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Food_Log_week_reads_that_week()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var readers = QueryMeter.Readers(db);
        var today = await db.TodayAsync();
        var (_, cost) = await QueryMeter.MeasureAsync(() => readers.GetRequiredService<FoodReader>().GetDaysAsync(today.AddDays(-6), today, Ct));
        Within("food week", cost, Week);
    }

    [Fact]
    public async Task An_Activity_week_reads_that_week()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var readers = QueryMeter.Readers(db);
        var activity = readers.GetRequiredService<ActivityReader>();
        var today = await db.TodayAsync();
        var from = today.AddDays(-6);
        var cost = await QueryMeter.MeasureAsync(async () =>
        {
            await activity.GetDaysAsync(from, today, Ct);
            await activity.GetWorkoutsAsync(from, today, Ct);
            await activity.GetTagsAsync(from, today, Ct);
        });
        Within("activity week", cost, Week);
    }

    [Fact]
    public async Task The_Inbox_count_reads_what_waits()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var readers = QueryMeter.Readers(db);
        var (_, cost) = await QueryMeter.MeasureAsync(() => readers.GetRequiredService<SubjectReader>().CountInboxAsync(Ct));
        Within("inbox count", cost, new Budget(Statements: 4, EventRows: 1_000, RowsScanned: 5_000, ReadTime));
    }

    [Fact]
    public async Task The_domain_fold_reads_each_source_once()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        var sources = await FoldSourcesAsync(db);
        var cost = await FoldAsync(kernel);

        // It reads the ledger's earns and the reversals beside them, the ratings and the exception
        // days: under twice the events they are, where a second read of any would be more.
        Within("domain fold", cost, new Budget(Statements: 8, EventRows: 2 * sources, RowsScanned: 8 * sources, WriteTime));
    }

    [Fact]
    public async Task The_settle_reads_every_day_once()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        var days = await DaySourcesAsync(db);
        var fold = await FoldAsync(kernel);
        var (_, cost) = await QueryMeter.MeasureAsync(() => kernel.GetRequiredService<SettlementService>().SettleAsync(cancellationToken: Ct));

        // Every food and activity day, what they paid, and the domains once.
        Within("settle", cost, new Budget(
            Statements: 30, EventRows: fold.EventRowsRead + 2 * days, RowsScanned: fold.RowsScanned + 8 * days, WriteTime));
        Assert.True(cost.CountOf(NpgsqlDomainCreditReader.DaysSql) <= 1, $"the settle folds the domains' history more than once:\n{cost.Describe()}");
    }

    /// <summary>The writes that earn, each of which folds the life domains (R18's query budget).</summary>
    public static TheoryData<string> EarningWrites => ["check-off", "completed task", "meal", "room rating", "reading"];

    [Theory]
    [MemberData(nameof(EarningWrites))]
    public async Task An_earning_write_reads_the_domains_history_once(string write)
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        var today = await db.TodayAsync();
        Func<Task> run = write switch
        {
            "check-off" => await CheckOffAsync(db, kernel, today),
            "completed task" => await CompleteAsync(db, kernel),
            "meal" => () => kernel.GetRequiredService<FoodLogService>().LogAsync(
                new FoodEntryInput("Oatmeal", 4, 350, DateTimeOffset.UtcNow.AddMinutes(-30), "breakfast"), cancellationToken: Ct),
            "room rating" => () => kernel.GetRequiredService<SpaceRatingService>().RateAsync(today, [new RatingInput("Kitchen", 4, 3.5m, 4)], Ct),
            "reading" => () => kernel.GetRequiredService<MetricReadingService>().LogAsync("Weight", 180.4m, cancellationToken: Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(write)),
        };

        var history = await HabitHistoryAsync(db);
        var fold = await FoldAsync(kernel);
        var cost = await QueryMeter.MeasureAsync(run);

        // The domains' history once, the habit's own (its streaks run back to its start), and a
        // little more: what was written and what it's checked against.
        Within(write, cost, new Budget(
            Statements: 40, EventRows: fold.EventRowsRead + 4 * history + 2_500, RowsScanned: fold.RowsScanned + 8 * history + 25_000,
            WriteTime));
        Assert.True(cost.CountOf(NpgsqlDomainCreditReader.DaysSql) <= 1, $"{write} folds the domains' history more than once:\n{cost.Describe()}");
    }

    /// <summary>What a read of the life domains costs here.</summary>
    private static async Task<QueryMeter.Cost> FoldAsync(ServiceProvider kernel)
        => (await QueryMeter.MeasureAsync(() => kernel.GetRequiredService<RankUpService>().ReadAsync(Ct))).Cost;

    private static async Task<Func<Task>> CheckOffAsync(DomainTestDatabase db, ServiceProvider kernel, DateOnly today)
    {
        var habit = await ScalarAsync<string>(db, "SELECT urn FROM bsk.subject WHERE type = 'Habit' AND title = 'Morning walk';");
        return () => kernel.GetRequiredService<AdherenceService>().RecordAsync(habit, AdherenceResults.Followed, today, cancellationToken: Ct);
    }

    private static async Task<Func<Task>> CompleteAsync(DomainTestDatabase db, ServiceProvider kernel)
    {
        var task = await ScalarAsync<string>(db, """
            SELECT s.urn FROM bsk.subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            WHERE s.type = 'Task' AND s.title LIKE 'Task %' AND coalesce(c.status, '') NOT IN ('Completed', 'Cancelled')
            ORDER BY s.created_at DESC LIMIT 1;
            """);
        return () => kernel.GetRequiredService<StatusService>().ChangeStatusAsync(task, "Completed", Ct);
    }

    /// <summary>The events the domain fold reads: the ledger, the room ratings and the exception days.</summary>
    private static Task<long> FoldSourcesAsync(DomainTestDatabase db) => ScalarAsync<long>(db, """
        SELECT count(*) FROM bsk.event
        WHERE kind = 'ledger'
           OR (kind = 'measurement' AND jsonb_exists(payload, 'space_rating'))
           OR (kind = 'note' AND jsonb_exists(payload, 'exception_day'));
        """);

    /// <summary>The events the food and activity days are read from.</summary>
    private static Task<long> DaySourcesAsync(DomainTestDatabase db) => ScalarAsync<long>(db, """
        SELECT count(*) FROM bsk.event
        WHERE (kind = 'measurement' AND (jsonb_exists(payload, 'meal') OR jsonb_exists(payload, 'oura_activity')
                                         OR jsonb_exists(payload, 'oura_sleep_day') OR jsonb_exists(payload, 'oura_sleep')))
           OR (kind = 'activity' AND (jsonb_exists(payload, 'fast') OR jsonb_exists(payload, 'food_day') OR jsonb_exists(payload, 'oura_workout')));
        """);

    /// <summary>The checked-off habit's own events: its check-offs and what they earned.</summary>
    private static Task<long> HabitHistoryAsync(DomainTestDatabase db) => ScalarAsync<long>(db, """
        SELECT count(*) FROM bsk.event e JOIN bsk.subject s ON s.type = 'Habit' AND s.title = 'Morning walk'
        WHERE e.payload->>'subject_id' = s.id::text AND e.kind IN ('activity', 'ledger');
        """);

    private static async Task<T> ScalarAsync<T>(DomainTestDatabase db, string sql)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.QuerySingleAsync<T>(new CommandDefinition(sql, cancellationToken: Ct));
    }

    private static void Within(string path, QueryMeter.Cost cost, Budget budget)
    {
        TestContext.Current.SendDiagnosticMessage($"{path}: {cost.Describe()}");
        ScaleReport.Write($"{path}: {cost.Describe(top: 6)}");
        Assert.True(cost.Count > 0, $"{path}: the meter caught no statement, so it measured nothing.");
        var misses = budget.Misses(cost);
        if (Over.TryGetValue(path, out var over))
        {
            Assert.True(misses.Count > 0, $"{path} is within its budget now; take it off Over.\n{cost.Describe()}");
            var worse = over.Misses(cost);
            Assert.True(worse.Count == 0, $"{path} costs more than when it was listed over budget ({over.Why}): {string.Join("; ", worse)}\n{cost.Describe()}");
            return;
        }

        Assert.True(misses.Count == 0, $"{path} is over its budget: {string.Join("; ", misses)}\n{cost.Describe()}");
    }

    /// <summary>What a path may cost.</summary>
    private sealed record Budget(int Statements, long EventRows, long RowsScanned, TimeSpan Time)
    {
        public List<string> Misses(QueryMeter.Cost cost)
        {
            var misses = new List<string>();
            if (cost.Count > Statements)
            {
                misses.Add($"{cost.Count} statements, over {Statements}");
            }

            if (cost.EventRowsRead > EventRows)
            {
                misses.Add($"{cost.EventRowsRead} event rows read, over {EventRows}");
            }

            if (cost.RowsScanned > RowsScanned)
            {
                misses.Add($"{cost.RowsScanned} rows scanned, over {RowsScanned}");
            }

            if (cost.FullScans.Count > 0)
            {
                misses.Add($"{cost.FullScans.Count} reads of the whole log");
            }

            if (cost.Elapsed > Time)
            {
                misses.Add($"{cost.Elapsed.TotalMilliseconds:F0} ms, over {Time.TotalMilliseconds:F0}");
            }

            return misses;
        }
    }
}

/// <summary>A path over its budget: what it cost when listed (which it mustn't exceed), and why.</summary>
internal sealed record Over(int Statements, long EventRows, long RowsScanned, string Why)
{
    public List<string> Misses(QueryMeter.Cost cost)
    {
        var misses = new List<string>();
        if (cost.Count > Statements)
        {
            misses.Add($"{cost.Count} statements, over {Statements}");
        }

        if (cost.EventRowsRead > EventRows)
        {
            misses.Add($"{cost.EventRowsRead} event rows read, over {EventRows}");
        }

        if (cost.RowsScanned > RowsScanned)
        {
            misses.Add($"{cost.RowsScanned} rows scanned, over {RowsScanned}");
        }

        return misses;
    }
}
