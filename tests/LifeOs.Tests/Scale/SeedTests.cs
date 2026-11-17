using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// The seeded template (<see cref="SeededTemplate"/>) is what the scale tests say it is: three
/// years of every stream the hot paths read, and a ledger that pays what the items are due, so a
/// write measured on it does no catching up the seed left it.
/// </summary>
public sealed class SeedTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Each stream's view, and the fewest rows three years of use leave in it.</summary>
    public static TheoryData<string, long> Streams => new()
    {
        { "bsk.v_meal", 3_000 },
        { "bsk.v_fast", 700 },
        { "bsk.v_food_day", 1_000 },
        { "bsk.v_activity_day", 1_000 },
        { "bsk.v_sleep_period", 1_000 },
        { "bsk.v_workout", 500 },
        { "bsk.v_oura_tag", 50 },
        { "bsk.v_inbox", 20 },
        { "bsk.v_reward_due", 5_000 },
        { "bsk.v_space_rating", 1_000 },
        { "bsk.v_metric_reading", 1_000 },
        // v_focus keeps one row, the newest choice; the stream is every choice ever made.
        { "bsk.event WHERE kind = 'note' AND jsonb_exists(payload, 'focus')", 40 },
    };

    [Theory]
    [MemberData(nameof(Streams))]
    public async Task The_seed_holds_three_years_of_each_stream(string view, long least)
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var rows = await connection.QuerySingleAsync<long>(new CommandDefinition($"SELECT count(*) FROM {view};", cancellationToken: Ct));
        ScaleReport.Write($"rows: {view} {rows}");
        Assert.True(rows >= least, $"{view} has {rows} rows, fewer than {least}.");
    }

    [Fact]
    public async Task The_log_runs_back_three_years_and_nothing_is_recorded_after_now()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        var (days, future) = await connection.QuerySingleAsync<(int Days, long Future)>(new CommandDefinition("""
            SELECT (now()::date - min(occurred_at)::date), count(*) FILTER (WHERE recorded_at > now())
            FROM bsk.event;
            """, cancellationToken: Ct));
        ScaleReport.Write($"log: {await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT format('%s events, %s', count(*), pg_size_pretty(pg_total_relation_size('bsk.event'))) FROM bsk.event;", cancellationToken: Ct))}");
        ScaleReport.Write($"by kind: {await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT string_agg(format('%s %s', kind, n), ', ' ORDER BY n DESC) FROM (SELECT kind, count(*) n FROM bsk.event GROUP BY kind) k;", cancellationToken: Ct))}");
        Assert.InRange(days, 1_095, 1_100);
        Assert.Equal(0, future);
    }

    [Fact]
    public async Task The_settle_on_build_paid_only_the_days_and_the_rank_ups()
    {
        var template = await postgres.SeededTemplateAsync();
        ScaleReport.Write($"template: built in {template.Took.TotalSeconds:F1} s; the settle paid "
                          + string.Join(", ", template.PaidOnBuild.OrderBy(p => p.Key).Select(p => $"{p.Value} {p.Key}")));

        // An item's award paid here would be one the seed missed; the food and activity days are
        // the kernel's to price.
        string[] days =
        [
            FoodAwards.DietTrigger, FoodAwards.FastTrigger,
            ActivityAwards.StepsTrigger, ActivityAwards.ExerciseTrigger, ActivityAwards.SleepTrigger,
        ];
        Assert.Subset(new HashSet<string>([.. days, LifeDomains.RankTrigger]), template.PaidOnBuild.Keys.ToHashSet());
        Assert.All(days, d => Assert.True(template.PaidOnBuild.GetValueOrDefault(d) > 0, $"the settle paid no {d} day."));
    }

    [Fact]
    public async Task Every_award_an_item_is_due_is_paid()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);

        // A Task's timer pays only when its bounty says how (BountyTriggers.OptIn), and none here does.
        var unpaid = (await connection.QueryAsync<(string Trigger, long Count)>(new CommandDefinition("""
            SELECT d.trigger, count(*)
            FROM bsk.v_reward_due d
            WHERE d.trigger <> 'timed'
              AND NOT EXISTS (SELECT 1 FROM bsk.event l WHERE l.kind = 'ledger' AND l.payload->>'award_key' = d.award_key)
            GROUP BY d.trigger ORDER BY d.trigger;
            """, cancellationToken: Ct))).ToList();
        Assert.True(unpaid.Count == 0, $"the seed left awards unpaid: {string.Join(", ", unpaid.Select(u => $"{u.Trigger} {u.Count}"))}");
    }
}
