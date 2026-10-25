using System.Globalization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// ACTIVITY-5 (activity plan A8, §3.8): activity XP on the ledger. Each Oura day pays a
/// <c>steps:&lt;day&gt;</c> and an <c>exercise:&lt;day&gt;</c> award once Oura's day is over, and a
/// <c>sleep:&lt;day&gt;</c> award once it has a sleep score, in base Health XP at the day's
/// multiplier, re-priced when Oura revises the day. Oura is played by
/// <see cref="FakeOuraClient"/>. The domain folds read the real clock, so each test runs on
/// today's date: the harness clock starts now, and XP starts 10 days ago.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActivityRewardsTests(PostgresFixture postgres)
{
    private static readonly OuraSyncOptions Recent = new(HistoryWindows: 0);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record World(OuraHarness H, ActivityRewards Rewards, DateOnly Today) : IAsyncDisposable
    {
        public DomainTestDatabase Db => H.Db;

        /// <summary>Two days ago: always over, whatever the time of day.</summary>
        public DateOnly Over => Today.AddDays(-2);

        public ActivityRewards With(ActivityRules rules)
            => new(H.Activity, Db.Get<ILedgerRepository>(), Db.Get<RewardService>(), Db.Get<IHomeCalendar>(), H.Clock,
                KernelSources.Cli, rules);

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }

    private async Task<World> CreateAsync(bool openWallet = true, int walletDaysAgo = 30)
    {
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var h = await OuraHarness.CreateAsync(postgres, Ct, start: now);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (openWallet)
        {
            await h.Db.Get<RewardService>().OpenAsync(now.AddDays(-walletDaysAgo), Ct);
        }

        var world = new World(h, null!, today);
        return world with { Rewards = world.With(ActivityRules.Default with { XpFrom = today.AddDays(-10) }) };
    }

    private static OuraWorkout Workout(
        string id, DateOnly day, int startMinute, int minutes, string activity = "running", string source = "manual")
    {
        var start = OuraHarness.At(day, 7).AddMinutes(startMinute);
        return new OuraWorkout(id, day, activity, null, start, start.AddMinutes(minutes), 300, null, "moderate", source);
    }

    private static OuraSleepScore Sleep(DateOnly day, int score, int deep = 90)
        => new(day, $"sleep-day-{day:yyyyMMdd}", OuraHarness.At(day, 0), score,
            new Dictionary<string, int?> { ["deep_sleep"] = deep, ["total_sleep"] = 80 });

    private static void Replace<T>(List<T> records, Func<T, bool> match, T with)
        => records[records.FindIndex(r => match(r))] = with;

    private static long Health(Award award) => award.Amounts.GetValueOrDefault(Currencies.HealthXp);

    /// <summary>The activity awards written, leaving out any rank-up they reached.</summary>
    private static List<Award> Activity(Earned earned)
        => [.. earned.Awards.Where(a => ActivityAwards.Triggers.Contains(a.Trigger))];

    /// <summary>Each activity award's key and Health XP, in the order written.</summary>
    private static List<(string Key, long Xp)> Paid(Earned earned) => [.. Activity(earned).Select(a => (a.AwardKey, Health(a)))];

    private async Task<Earned> SyncAndSettleAsync(World w, ActivityRewards? rewards = null)
    {
        await w.H.Sync.SyncAsync(Recent, Ct);
        return await (rewards ?? w.Rewards).SettleAsync(Ct);
    }

    /// <summary>The Health XP credited to <paramref name="day"/> (<c>bsk.v_award_credit</c>): base and paid.</summary>
    private async Task<(long Base, long Paid)> CreditAsync(World w, DateOnly day)
    {
        var credit = await w.Db.Get<ILedgerRepository>().GetDomainDaysAsync([Currencies.HealthXp], Ct);
        var rows = credit.Days.Where(d => d.Day == day).ToList();
        return (rows.Sum(d => d.Base), rows.Sum(d => d.Paid));
    }

    [Fact]
    public async Task Each_part_pays_once_its_day_is_due_credited_to_its_day_and_never_twice()
    {
        await using var w = await CreateAsync();
        var (day, today, before) = (w.Over, w.Today, w.Today.AddDays(-11));
        var oura = w.H.Oura;

        // Two days ago: 7,500 steps; a run and an overlapping ride (35 minutes together), plus a
        // walk and a detected run that don't count; a sleep score of 80; readiness of 95.
        oura.ActivityDays.Add(OuraHarness.Steps(day, 7_500));
        oura.Workouts.AddRange([
            Workout("w-run", day, 0, 20),
            Workout("w-ride", day, 10, 25, "cycling", "workout_heart_rate"),
            Workout("w-walk", day, 120, 60, "walking"),
            Workout("w-detected", day, 240, 60, "running", "autodetected"),
        ]);
        oura.SleepScores.Add(Sleep(day, 80));
        oura.Readiness.Add(OuraHarness.Ready(day, 95));

        // Today isn't over: its steps and workout wait, but last night's score pays.
        oura.ActivityDays.Add(OuraHarness.Steps(today, 4_000));
        oura.Workouts.Add(Workout("w-today", today, 0, 50));
        oura.SleepScores.Add(Sleep(today, 70));

        // Before XP started: never pays.
        oura.ActivityDays.Add(OuraHarness.Steps(before, 12_000));
        oura.Workouts.Add(Workout("w-before", before, 0, 60));
        oura.SleepScores.Add(Sleep(before, 90));

        // Missing isn't zero: a night with no score, and a day of no steps, pay nothing.
        oura.SleepPeriods.Add(OuraHarness.Night("s-unscored", day.AddDays(-1), 7 * 60));
        oura.ActivityDays.Add(OuraHarness.Steps(day.AddDays(-2), 0));

        var earned = await SyncAndSettleAsync(w);

        Assert.Equal(
            new Dictionary<string, long>
            {
                [ActivityAwards.ExerciseKey(day)] = 25,
                [ActivityAwards.SleepKey(day)] = 15,
                [ActivityAwards.SleepKey(today)] = 10,
                [ActivityAwards.StepsKey(day)] = 20,
            },
            Activity(earned).ToDictionary(a => a.AwardKey, Health));
        Assert.All(Activity(earned), a =>
        {
            Assert.Equal(LedgerEntries.Earn, a.Entry);
            Assert.Null(a.Subject);

            // No successful days before them: ×1.0, so they pay their base and record no multiplier.
            Assert.Null(a.Multipliers);
        });
        var steps = earned.Awards.Single(a => a.AwardKey == ActivityAwards.StepsKey(day));
        Assert.Equal(ActivityAwards.StepsTrigger, steps.Trigger);
        Assert.Equal("Steps " + day.ToString("ddd MMM d", CultureInfo.InvariantCulture), steps.Note);

        Assert.Equal((60L, 60L), await CreditAsync(w, day));
        Assert.Equal((10L, 10L), await CreditAsync(w, today));
        Assert.Equal((0L, 0L), await CreditAsync(w, before));

        // Settling again, or syncing again with nothing new, pays nothing.
        Assert.Empty((await w.Rewards.SettleAsync(Ct)).Awards);
        Assert.Empty((await SyncAndSettleAsync(w)).Awards);
        Assert.Equal((60L, 60L), await CreditAsync(w, day));
    }

    [Fact]
    public async Task Oura_revising_a_day_re_prices_it_and_an_unchanged_base_writes_nothing()
    {
        await using var w = await CreateAsync();
        var day = w.Over;
        var oura = w.H.Oura;
        oura.ActivityDays.Add(OuraHarness.Steps(day, 7_500));
        oura.Workouts.Add(Workout("w-run", day, 0, 30));
        oura.SleepScores.Add(Sleep(day, 80));
        Assert.Equal(60L, Activity(await SyncAndSettleAsync(w)).Sum(Health));

        // More steps synced after the day was over: the steps award is reversed and earned again.
        Replace(oura.ActivityDays, d => d.Day == day, OuraHarness.Steps(day, 10_000));
        Assert.Equal([(ActivityAwards.StepsKey(day), -20L), (ActivityAwards.StepsKey(day), 25L)], Paid(await SyncAndSettleAsync(w)));

        // A second session the same day: 30 + 20 minutes pays 35.
        oura.Workouts.Add(Workout("w-row", day, 60, 20, "rowing"));
        Assert.Equal([(ActivityAwards.ExerciseKey(day), -25L), (ActivityAwards.ExerciseKey(day), 35L)], Paid(await SyncAndSettleAsync(w)));

        // Deleted in the Oura app: the day pays nothing for exercise. Restored: it pays again.
        var workouts = oura.Workouts.ToList();
        oura.Workouts.Clear();
        Assert.Equal([(ActivityAwards.ExerciseKey(day), -35L)], Paid(await SyncAndSettleAsync(w)));
        oura.Workouts.AddRange(workouts);
        Assert.Equal([(ActivityAwards.ExerciseKey(day), 35L)], Paid(await SyncAndSettleAsync(w)));

        // A revised score re-prices sleep; a new version at the same score changes nothing.
        Replace(oura.SleepScores, s => s.Day == day, Sleep(day, 85));
        Assert.Equal([(ActivityAwards.SleepKey(day), -15L), (ActivityAwards.SleepKey(day), 20L)], Paid(await SyncAndSettleAsync(w)));
        Replace(oura.SleepScores, s => s.Day == day, Sleep(day, 85, deep: 60));
        Assert.Equal(1, (await w.H.Sync.SyncAsync(Recent, Ct)).Written);
        Assert.Empty((await w.Rewards.SettleAsync(Ct)).Awards);

        Assert.Equal((80L, 80L), await CreditAsync(w, day));
    }

    [Fact]
    public async Task A_day_s_steps_and_exercise_wait_for_4_am_the_next_morning()
    {
        await using var w = await CreateAsync();
        var today = w.Today;
        w.H.Oura.ActivityDays.Add(OuraHarness.Steps(today, 6_000));
        w.H.Oura.Workouts.Add(Workout("w-today", today, 0, 45));
        await w.H.Sync.SyncAsync(Recent, Ct);

        // 3:59 the next morning (the home zone is UTC): Oura's day is still today.
        var tomorrow = today.AddDays(1);
        w.H.Clock.Advance(new DateTimeOffset(tomorrow.ToDateTime(new TimeOnly(3, 59)), TimeSpan.Zero) - w.H.Clock.UtcNow);
        Assert.Empty((await w.Rewards.SettleAsync(Ct)).Awards);
        Assert.Equal(today, await w.Rewards.OuraDayAtAsync(w.H.Clock.UtcNow, Ct));

        w.H.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(
            [(ActivityAwards.ExerciseKey(today), 35L), (ActivityAwards.StepsKey(today), 15L)],
            Paid(await w.Rewards.SettleAsync(Ct)));
    }

    [Fact]
    public async Task A_retune_leaves_a_paid_day_alone_until_oura_revises_it()
    {
        await using var w = await CreateAsync();
        var day = w.Over;
        w.H.Oura.ActivityDays.Add(OuraHarness.Steps(day, 7_500));
        Assert.Equal([(ActivityAwards.StepsKey(day), 20L)], Paid(await SyncAndSettleAsync(w)));

        // Rules that would pay 7,500 steps only 8: the paid day stands.
        var retuned = w.With(w.Rewards.Rules with { StepsCurve = [new(0, 0), new(10_000, 10)] });
        Assert.Empty((await retuned.SettleAsync(Ct)).Awards);

        // Once Oura revises the day, it's priced by the rules of the day: 8,000 steps pay 8.
        Replace(w.H.Oura.ActivityDays, d => d.Day == day, OuraHarness.Steps(day, 8_000));
        Assert.Equal([(ActivityAwards.StepsKey(day), -20L), (ActivityAwards.StepsKey(day), 8L)], Paid(await SyncAndSettleAsync(w, retuned)));
    }

    [Fact]
    public async Task Activity_xp_pays_at_its_day_s_multiplier_and_its_base_counts_toward_the_day()
    {
        await using var w = await CreateAsync(walletDaysAgo: 40);
        var day = w.Over;

        // Every one of the 30 days before it succeeded: ×1.3.
        await w.Db.CreditDaysAsync(w.Today, fromAgo: 32, toAgo: 3, xp: 60);
        w.H.Oura.ActivityDays.Add(OuraHarness.Steps(day, 7_500));
        w.H.Oura.Workouts.Add(Workout("w-run", day, 0, 30));
        w.H.Oura.SleepScores.Add(Sleep(day, 80));

        var earned = await SyncAndSettleAsync(w);

        // 20, 25 and 15 at ×1.3, each rounded halves up: 26 + 33 + 20.
        Assert.Equal(
            [(ActivityAwards.ExerciseKey(day), 33L), (ActivityAwards.SleepKey(day), 20L), (ActivityAwards.StepsKey(day), 26L)],
            Paid(earned));
        Assert.All(Activity(earned), a => Assert.Equal(1.3m, a.Multipliers![Currencies.HealthXp]));
        Assert.Equal((60L, 79L), await CreditAsync(w, day));

        // What the page reads it paid (A9): each part's base, paid and multiplier.
        var paid = await new ActivityPaidReads(w.Db.Get<ILedgerRepository>()).GetDayAsync(day, Ct);
        Assert.Equal(
            (new ActivityPaid(20, 26, 1.3m), new ActivityPaid(25, 33, 1.3m), new ActivityPaid(15, 20, 1.3m), 79L, 60L),
            (paid.Steps, paid.Exercise, paid.Sleep, paid.Total, paid.BaseTotal));
        Assert.Equal(ActivityDayPaid.None, await new ActivityPaidReads(w.Db.Get<ILedgerRepository>()).GetDayAsync(w.Today, Ct));

        // 60 base XP, 45 of it action (sleep is passive): an active day on activity alone.
        var health = (await w.Db.Get<DomainService>().GetAsync(Ct)).Domains
            .Single(d => d.Currency.Key == Currencies.HealthXp).Progress!;
        Assert.True(health.ActiveOn(day));
    }

    [Fact]
    public async Task Nothing_pays_before_the_wallet_opens_and_activity_never_opens_it()
    {
        await using var w = await CreateAsync(openWallet: false);
        w.H.Oura.ActivityDays.Add(OuraHarness.Steps(w.Over, 9_000));
        w.H.Oura.SleepScores.Add(Sleep(w.Over, 82));

        Assert.Empty((await SyncAndSettleAsync(w)).Awards);
        Assert.Null(await w.Db.Get<ILedgerRepository>().GetOpenedAtAsync(Ct));
    }

    [Fact]
    public async Task Activity_pays_from_the_day_the_wallet_opened_dated_no_earlier_than_the_opening()
    {
        // Chris's Oct 22: awake at 6:20 and a run at 7:00, then the wallet opened at 11:00 with
        // his first Food Log entry. The night and the run came before the opening, the same day.
        await using var w = await CreateAsync(openWallet: false);
        var (day, before) = (w.Over, w.Over.AddDays(-1));
        var opened = OuraHarness.At(day, 11);
        await w.Db.Get<RewardService>().OpenAsync(opened, Ct);
        var oura = w.H.Oura;
        oura.SleepPeriods.Add(OuraHarness.Night("s-night", day, 7 * 60));
        oura.SleepScores.Add(Sleep(day, 80));
        oura.Workouts.Add(Workout("w-run", day, 0, 30));
        oura.ActivityDays.Add(OuraHarness.Steps(day, 7_500));

        // The day before the wallet opened never pays, though it's synced after the opening.
        oura.ActivityDays.Add(OuraHarness.Steps(before, 9_000));
        oura.SleepScores.Add(Sleep(before, 85));

        Assert.Equal(
            [(ActivityAwards.ExerciseKey(day), 25L), (ActivityAwards.SleepKey(day), 15L), (ActivityAwards.StepsKey(day), 20L)],
            Paid(await SyncAndSettleAsync(w)));

        // The night and the run are dated at the opening, never before it; the steps at the day's sync.
        var at = (await w.Db.Get<RewardService>().GetLedgerAsync(cancellationToken: Ct))
            .Where(e => e.AwardKey is not null)
            .ToDictionary(e => e.AwardKey!, e => e.OccurredAt);
        Assert.Equal(opened, at[ActivityAwards.SleepKey(day)]);
        Assert.Equal(opened, at[ActivityAwards.ExerciseKey(day)]);
        Assert.True(at[ActivityAwards.StepsKey(day)] > opened);
        Assert.Equal((60L, 60L), await CreditAsync(w, day));
        Assert.Equal((0L, 0L), await CreditAsync(w, before));
        Assert.Empty((await w.Rewards.SettleAsync(Ct)).Awards);
    }

    [Fact]
    public async Task The_kernel_s_activity_rewards_pay_from_2026_10_22()
    {
        await using var w = await CreateAsync();

        Assert.Equal(ActivityRules.Default, w.Db.Get<ActivityRewards>().Rules);
        Assert.Equal(new DateOnly(2026, 10, 22), w.Db.Get<ActivityRewards>().Rules.XpFrom);
    }
}
