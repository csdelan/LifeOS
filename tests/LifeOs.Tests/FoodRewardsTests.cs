using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-3 (food-log plan F2, §3.3–3.4): diet XP on the ledger. Each food day pays a
/// <c>fast:&lt;day&gt;</c> award when a fast ends on it and a <c>diet:&lt;day&gt;</c> award once it's
/// closed, both in base Health XP at the day's multiplier, re-priced when the day's entries
/// change. Each test has a database of its own (UTC): food days and the multiplier read
/// everything in it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodRewardsTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
        => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private static FoodEntryInput Entry(string name, int score, int calories, DateTimeOffset at)
        => new(name, score, calories, at);

    private static long Health(Award award) => award.Amounts.GetValueOrDefault(Currencies.HealthXp);

    /// <summary>The Health XP credited to <paramref name="day"/> (<c>bsk.v_award_credit</c>): base and paid.</summary>
    private async Task<(long Base, long Paid)> CreditAsync(DomainTestDatabase db, DateOnly day)
    {
        var credit = await db.Get<ILedgerRepository>().GetDomainDaysAsync([Currencies.HealthXp], Ct);
        var row = credit.Days.Where(d => d.Day == day).ToList();
        return (row.Sum(d => d.Base), row.Sum(d => d.Paid));
    }

    /// <summary>Every ledger entry for <paramref name="key"/>, oldest first.</summary>
    private async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(DomainTestDatabase db, string key)
        => (await db.Get<RewardService>().GetLedgerAsync(cancellationToken: Ct))
            .Where(e => e.AwardKey == key)
            .OrderBy(e => e.OccurredAt)
            .ToList();

    /// <summary>A food event appended straight to the store, with no settle after it.</summary>
    private Task<IReadOnlyList<Guid>> AppendAsync(
        DomainTestDatabase db, string kind, object payload, DateTimeOffset occurredAt, DateTimeOffset recordedAt)
        => db.Get<IFoodLogRepository>().AppendAsync(
            [new NewEvent(kind, Provenances.Declared, occurredAt, recordedAt, KernelSources.Cli, PayloadJson: JsonSerializer.Serialize(payload))],
            FoodGuard.None, Ct);

    [Fact]
    public async Task The_example_days_pay_what_the_table_says_each_credited_to_its_day()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var today = await db.TodayAsync();
        var (iceCreamDay, salmonDay) = (today.AddDays(-3), today.AddDays(-2));

        // 3,000 calories of ice cream: the day is over, so it's closed, and it pays nothing.
        var iceCream = await food.LogAsync(Entry("Ice cream", 1, 3000, At(iceCreamDay, 13)), cancellationToken: Ct);
        Assert.Empty(iceCream.Earned.Awards);

        // A 23-hour fast pays 7 the moment it's recorded, credited to the day it ended.
        var fast = await food.RecordFastAsync(At(salmonDay.AddDays(-1), 13), At(salmonDay, 12), Ct);
        var fastAward = Assert.Single(fast.Earned.Awards);
        Assert.Equal((LedgerEntries.Earn, FoodAwards.FastTrigger, FoodAwards.FastKey(salmonDay), 7L),
            (fastAward.Entry, fastAward.Trigger, fastAward.AwardKey, Health(fastAward)));
        Assert.Null(fastAward.Subject);
        Assert.Single(fastAward.Amounts);

        // 1,200 calories of salmon salad on a day that's over pays its food: 32.
        var salmon = await food.LogAsync(Entry("Salmon salad", 5, 1200, At(salmonDay, 12, 30)), cancellationToken: Ct);
        var dietAward = Assert.Single(salmon.Earned.Awards);
        Assert.Equal((FoodAwards.DietKey(salmonDay), 32L), (dietAward.AwardKey, Health(dietAward)));
        Assert.Equal("Food day " + salmonDay.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture), dietAward.Note);

        // 39 of 40, on the salmon day; nothing on the ice-cream day.
        Assert.Equal((39L, 39L), await CreditAsync(db, salmonDay));
        Assert.Equal((0L, 0L), await CreditAsync(db, iceCreamDay));

        // The earns are derived, credited to the day, dated at their trigger, citing what they read.
        var fastPayload = await db.PayloadAsync(fastAward.EventId);
        Assert.Equal(salmonDay.ToString("yyyy-MM-dd"), fastPayload.GetProperty("credited_on").GetString());
        var fastEntry = Assert.Single(await EntriesAsync(db, FoodAwards.FastKey(salmonDay)));
        Assert.Equal(At(salmonDay, 12), fastEntry.OccurredAt);
        Assert.Equal(Provenances.Derived, fastEntry.Provenance);
        var dietPayload = await db.PayloadAsync(dietAward.EventId);
        Assert.Equal(salmonDay.ToString("yyyy-MM-dd"), dietPayload.GetProperty("credited_on").GetString());
        Assert.False(dietPayload.TryGetProperty("base", out _), "no multiplier, so no base stamped");

        // The day reads what it paid; settling again writes nothing.
        var view = await food.GetDayAsync(salmonDay, Ct);
        Assert.Equal((39L, 7L, 32L), (view.Paid.Total, view.Paid.Fast!.Paid, view.Paid.Diet!.Paid));
        Assert.Null(view.Paid.Diet.Multiplier);
        Assert.Equal(39L, (await food.GetDaysAsync(iceCreamDay, salmonDay, Ct)).Days.Sum(d => d.Paid.Total));
        Assert.Empty((await food.SettleAsync(Ct)).Awards);
    }

    [Fact]
    public async Task Today_pays_its_food_when_closed_and_food_logged_after_the_close_re_scores_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var today = await db.TodayAsync();
        var now = Now();
        if (now - At(today, 0) < TimeSpan.FromMinutes(10))
        {
            Assert.Skip("Too close to midnight UTC for a test about today.");
        }

        // Ending a 20-hour fast pays its fasting award right away: 8 × 8 / 12 = 5.3 → 5.
        await food.BeginFastAsync(now.AddHours(-20), Ct);
        var ended = await food.EndFastAsync(now.AddMinutes(-5), Ct);
        Assert.Equal(5L, Health(Assert.Single(ended.Earned.Awards)));

        // Today's food doesn't pay while the day is open...
        var oatmeal = await food.LogAsync(Entry("Oatmeal", 4, 400, now.AddMinutes(-4)), cancellationToken: Ct);
        Assert.Empty(oatmeal.Earned.Awards);

        // ... and pays when it's closed: 21 quality, nothing for calories under 500.
        var closed = await food.CloseDayAsync(cancellationToken: Ct);
        var diet = Assert.Single(closed.Earned.Awards);
        Assert.Equal((FoodAwards.DietKey(today), 21L), (diet.AwardKey, Health(diet)));
        var closedAt = (await food.GetDayAsync(today, Ct)).Facts.ClosedAt!.Value;
        Assert.Equal(closedAt, Assert.Single(await EntriesAsync(db, diet.AwardKey)).OccurredAt);

        // A snack after the close re-scores the day: 21 is given back and 28 paid.
        var snack = await food.LogAsync(Entry("Berries", 5, 300, now.AddMinutes(-1)), cancellationToken: Ct);
        Assert.Equal(
            [(LedgerEntries.Reversal, -21L), (LedgerEntries.Earn, 28L)],
            snack.Earned.Awards.Select(a => (a.Entry, Health(a))));
        Assert.All(snack.Earned.Awards, a => Assert.Equal(diet.AwardKey, a.AwardKey));
        Assert.Equal((33L, 33L), await CreditAsync(db, today));

        // The re-price's earn is dated after the earn it replaces, so it's the one the day reads.
        var entries = await EntriesAsync(db, diet.AwardKey);
        Assert.Equal([21L, -21L, 28L], entries.Select(e => e.Amounts[Currencies.HealthXp]));
        Assert.True(entries[2].OccurredAt > entries[0].OccurredAt);
    }

    [Fact]
    public async Task A_day_that_closed_itself_pays_at_the_next_settle_credited_to_its_own_day()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-1);

        // Logged yesterday evening, before the day was over: nothing settled it then.
        var loggedAt = Now().AddMinutes(-30);
        await AppendAsync(db, EventKinds.Measurement,
            new { meal = new { id = Guid.NewGuid(), name = "Grilled chicken", score = 4, calories = 900, meal_type = "dinner" } },
            At(day, 19), loggedAt);
        Assert.Equal((0L, 0L), await CreditAsync(db, day));

        // The next settle — the page opening, the sweep, or any food write — pays it.
        var settled = await food.SettleAsync(Ct);
        var award = Assert.Single(settled.Awards);
        Assert.Equal((FoodAwards.DietKey(day), 25L), (award.AwardKey, Health(award)));
        Assert.Equal((25L, 25L), await CreditAsync(db, day));
        Assert.Equal(loggedAt, Assert.Single(await EntriesAsync(db, award.AwardKey)).OccurredAt);
        Assert.Empty((await food.SettleAsync(Ct)).Awards);

        // Closing it by hand now writes nothing, and pays nothing more.
        var close = await food.CloseDayAsync(day, Ct);
        Assert.Equal(FoodActions.AlreadyClosed, close.Action);
        Assert.Empty(close.Earned.Awards);
    }

    [Fact]
    public async Task Edits_re_price_a_paid_day_and_deleting_every_entry_reverses_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-3);
        var key = FoodAwards.DietKey(day);

        var meal = (await food.LogAsync(Entry("Salmon salad", 5, 1200, At(day, 12)), cancellationToken: Ct)).Meal!;
        Assert.Equal((32L, 32L), await CreditAsync(db, day));

        // A score of 3 instead: 14 + 4 = 18.
        var edited = await food.EditAsync(meal.Id.ToString(), Entry("Salmon salad", 3, 1200, At(day, 12)), Ct);
        Assert.Equal([(LedgerEntries.Reversal, -32L), (LedgerEntries.Earn, 18L)], edited.Earned.Awards.Select(a => (a.Entry, Health(a))));
        Assert.Equal((18L, 18L), await CreditAsync(db, day));

        // An edit that doesn't change the score writes nothing.
        Assert.Empty((await food.EditAsync(meal.Id.ToString(), Entry("Salmon and rice", 3, 1200, At(day, 12, 5)), Ct)).Earned.Awards);

        // Deleting the only entry gives it all back.
        var deleted = await food.DeleteAsync(meal.Id.ToString(), Ct);
        var reversal = Assert.Single(deleted.Earned.Awards);
        Assert.Equal((LedgerEntries.Reversal, -18L), (reversal.Entry, Health(reversal)));
        Assert.Equal((0L, 0L), await CreditAsync(db, day));
        var payload = await db.PayloadAsync(reversal.EventId);
        Assert.Equal(deleted.Meal!.Id, meal.Id);
        Assert.Single(payload.GetProperty("amounts").EnumerateObject());

        // Logged again, it pays again, dated after every earn before it.
        var again = await food.LogAsync(Entry("Salmon salad", 5, 1200, At(day, 12)), cancellationToken: Ct);
        Assert.Equal(32L, Health(Assert.Single(again.Earned.Awards)));
        var entries = await EntriesAsync(db, key);
        Assert.Equal([32L, -32L, 18L, -18L, 32L], entries.Select(e => e.Amounts[Currencies.HealthXp]));
        Assert.Equal(entries[^1].EventId, (await db.Get<ILedgerRepository>().GetKeyedAwardsAsync(FoodAwards.Triggers, Ct))
            .Single(k => k.AwardKey == key).LatestEarnId);
    }

    [Fact]
    public async Task Moving_an_entry_or_a_fast_across_midnight_re_scores_both_days()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var today = await db.TodayAsync();
        var (first, second) = (today.AddDays(-5), today.AddDays(-4));

        var meal = (await food.LogAsync(Entry("Late dinner", 5, 1000, At(first, 23, 30)), cancellationToken: Ct)).Meal!;
        var moved = await food.EditAsync(meal.Id.ToString(), Entry("Late dinner", 5, 1000, At(second, 0, 30)), Ct);
        Assert.Equal(
            [(FoodAwards.DietKey(first), LedgerEntries.Reversal, -32L), (FoodAwards.DietKey(second), LedgerEntries.Earn, 32L)],
            moved.Earned.Awards.Select(a => (a.AwardKey, a.Entry, Health(a))));

        // An 18-hour fast pays 4 on the day it ends; lengthened to 24 hours, 8.
        var fast = (await food.RecordFastAsync(At(first, 20), At(second, 14), Ct)).Fast!;
        Assert.Equal((36L, 36L), await CreditAsync(db, second));
        var longer = await food.EditFastAsync(fast.Id.ToString(), At(first, 20), At(second, 20), Ct);
        Assert.Equal([-4L, 8L], longer.Earned.Awards.Select(Health));

        // Ending it the next day moves the award there.
        var third = second.AddDays(1);
        var later = await food.EditFastAsync(fast.Id.ToString(), At(second, 2), At(third, 2), Ct);
        Assert.Equal(
            [(FoodAwards.FastKey(second), -8L), (FoodAwards.FastKey(third), 8L)],
            later.Earned.Awards.Select(a => (a.AwardKey, Health(a))));

        // A deleted fast pays nothing.
        var deleted = await food.DeleteFastAsync(fast.Id.ToString(), Ct);
        Assert.Equal((FoodAwards.FastKey(third), -8L), (Assert.Single(deleted.Earned.Awards).AwardKey, Health(deleted.Earned.Awards[0])));
        Assert.Equal((32L, 32L), await CreditAsync(db, second));
    }

    [Fact]
    public async Task Diet_xp_pays_at_its_days_multiplier_and_its_base_counts_toward_the_70()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var today = await db.TodayAsync();
        var day = today.AddDays(-3);
        await db.OpenWalletAsync(daysAgo: 40);

        // 26 of the 30 days before the food day succeeded: x1.3. The day itself has 31 already.
        await db.CreditDaysAsync(today, fromAgo: 30, toAgo: 5, xp: 70);
        await db.CreditAsync(day, 31);

        var fast = await food.RecordFastAsync(At(day.AddDays(-1), 12), At(day, 11), Ct);
        var salmon = await food.LogAsync(Entry("Salmon salad", 5, 1200, At(day, 12)), cancellationToken: Ct);

        // 7 x 1.3 = 9.1 -> 9 and 32 x 1.3 = 41.6 -> 42, each stamping its base.
        var fastAward = Assert.Single(fast.Earned.Awards);
        Assert.Equal((9L, 7L, 1.3m), (Health(fastAward), fastAward.Base![Currencies.HealthXp], fastAward.Multipliers![Currencies.HealthXp]));
        var dietAward = Assert.Single(salmon.Earned.Awards);
        Assert.Equal((42L, 32L, 1.3m), (Health(dietAward), dietAward.Base![Currencies.HealthXp], dietAward.Multipliers![Currencies.HealthXp]));
        Assert.Equal(32, (await db.PayloadAsync(dietAward.EventId)).GetProperty("base").GetProperty(Currencies.HealthXp).GetInt64());

        // The day's base is 31 + 7 + 32 = 70: a success, read on the base, not the 82 paid.
        Assert.Equal((70L, 82L), await CreditAsync(db, day));
        var series = await db.Get<DomainService>().GetDaysAsync("health", 4, Ct);
        var row = series.Days.Single(d => d.Day == day);
        Assert.Equal((70L, 82L, true), (row.Base, row.Paid, row.Succeeded));

        var paid = (await food.GetDayAsync(day, Ct)).Paid;
        Assert.Equal(new FoodPaid(32, 42, 1.3m), paid.Diet);
        Assert.Equal(new FoodPaid(7, 9, 1.3m), paid.Fast);
        Assert.Equal((51L, 39L), (paid.Total, paid.BaseTotal));
    }

    [Fact]
    public async Task Nothing_recorded_before_the_wallet_opened_pays_and_a_food_write_can_open_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var today = await db.TodayAsync();
        var past = today.AddDays(-5);

        // Before the wallet ever opens, a settle with no write behind it pays nothing.
        await AppendAsync(db, EventKinds.Measurement,
            new { meal = new { id = Guid.NewGuid(), name = "Toast", score = 3, calories = 600, meal_type = "breakfast" } },
            At(past, 8), At(past, 8, 5));
        Assert.Empty((await food.SettleAsync(Ct)).Awards);
        Assert.Null((await db.Get<RewardService>().GetWalletAsync(Ct)).OpenedAt);

        // A fast recorded now opens it, dated at that fast's end, and pays it. The toast day was
        // recorded before then, so it doesn't pay.
        var fastDay = today.AddDays(-2);
        var fast = await food.RecordFastAsync(At(fastDay.AddDays(-1), 20), At(fastDay, 14), Ct);
        Assert.Equal(At(fastDay, 14), (await db.Get<RewardService>().GetWalletAsync(Ct)).OpenedAt);
        Assert.Equal(FoodAwards.FastKey(fastDay), Assert.Single(fast.Earned.Awards).AwardKey);
        Assert.Equal((0L, 0L), await CreditAsync(db, past));

        // Food logged for that day now is recorded after the opening, so the day pays — all of it.
        var lunch = await food.LogAsync(Entry("Soup", 3, 400, At(past, 12)), cancellationToken: Ct);
        var award = Assert.Single(lunch.Earned.Awards);
        Assert.Equal((FoodAwards.DietKey(past), 18L), (award.AwardKey, Health(award)));
    }

    [Fact]
    public async Task Two_settles_racing_to_re_price_a_day_pay_it_once()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-3);
        var key = FoodAwards.DietKey(day);
        var meal = (await food.LogAsync(Entry("Salmon salad", 5, 1200, At(day, 12)), cancellationToken: Ct)).Meal!;

        // A new version of the entry lands with no settle behind it; then two windows settle at once.
        await AppendAsync(db, EventKinds.Measurement,
            new { meal = new { id = meal.Id, name = "Salmon salad", score = 3, calories = 1200, meal_type = "lunch" } },
            At(day, 12), DateTimeOffset.UtcNow);
        var rewards = db.Get<FoodRewards>();
        var results = await Task.WhenAll(
            Task.Run(() => rewards.SettleAsync(cancellationToken: Ct), Ct),
            Task.Run(() => rewards.SettleAsync(cancellationToken: Ct), Ct));

        Assert.Equal(2, results.Sum(r => r.Awards.Count));
        Assert.Equal([32L, -32L, 18L], (await EntriesAsync(db, key)).Select(e => e.Amounts[Currencies.HealthXp]));
        Assert.Equal((18L, 18L), await CreditAsync(db, day));
    }

    [Fact]
    public async Task A_paid_day_is_left_alone_while_its_entries_are_unchanged_whatever_the_rules_score_now()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var ledger = db.Get<ILedgerRepository>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-3);
        var key = FoodAwards.DietKey(day);
        var meal = (await food.LogAsync(Entry("Salmon salad", 5, 1200, At(day, 12)), cancellationToken: Ct)).Meal!;

        // As if older rules had paid this day 30 from the very same entry.
        var held = (await ledger.GetKeyedAwardsAsync(FoodAwards.Triggers, Ct)).Single(k => k.AwardKey == key);
        var now = Now();
        await ledger.AppendAsync(
        [
            Write(LedgerEntries.Reversal, -32, now, held.LastEventId, expectPaid: true, expectLatestEarn: held.LatestEarnId),
            WriteFrom(LedgerEntries.Earn, 30, now.AddSeconds(1), [.. held.LatestSources], expectPaid: false)
        ], Ct);
        Assert.Equal((30L, 30L), await CreditAsync(db, day));

        // Today's rules would score it 32, but its entries haven't changed: nothing is re-priced.
        Assert.Empty((await food.SettleAsync(Ct)).Awards);

        // Once an entry changes, it's scored by today's rules.
        var edited = await food.EditAsync(meal.Id.ToString(), Entry("Salmon salad", 5, 1300, At(day, 12)), Ct);
        Assert.Equal([-30L, 32L], edited.Earned.Awards.Select(Health));

        LedgerWrite Write(string entry, long xp, DateTimeOffset at, Guid cause, bool expectPaid, Guid? expectLatestEarn = null)
            => WriteFrom(entry, xp, at, [cause], expectPaid, expectLatestEarn);

        LedgerWrite WriteFrom(string entry, long xp, DateTimeOffset at, Guid[] sources, bool expectPaid, Guid? expectLatestEarn = null)
            => new(
                new NewEvent(EventKinds.Ledger, Provenances.Derived, at, at, KernelSources.Cli,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        entry,
                        amounts = new Dictionary<string, long> { [Currencies.HealthXp] = xp },
                        award_key = key,
                        trigger = FoodAwards.DietTrigger,
                        credited_on = day.ToString("yyyy-MM-dd")
                    }),
                    DerivedFrom: sources),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: expectPaid, ExpectLatestEarn: expectLatestEarn);
    }

    [Fact]
    public async Task The_ledger_checks_a_batch_in_order_and_refuses_a_stale_latest_earn()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var ledger = db.Get<ILedgerRepository>();
        await db.OpenWalletAsync(daysAgo: 10);
        var day = (await db.TodayAsync()).AddDays(-1);
        var key = FoodAwards.FastKey(day);
        var cause = (await AppendAsync(db, EventKinds.Activity,
            new { fast = new { id = Guid.NewGuid(), started_at = At(day, 0), ended_at = At(day, 1) } }, At(day, 1), Now()))[0];
        var now = Now();

        LedgerWrite Write(string entry, long xp, int second, bool expectPaid, Guid? latest = null) => new(
            new NewEvent(EventKinds.Ledger, Provenances.Derived, now.AddSeconds(second), now, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    entry,
                    amounts = new Dictionary<string, long> { [Currencies.HealthXp] = xp },
                    award_key = key,
                    trigger = FoodAwards.FastTrigger,
                    credited_on = day.ToString("yyyy-MM-dd")
                }),
                DerivedFrom: [cause]),
            [], GuardBalance: false, AwardKey: key, ExpectPaid: expectPaid, ExpectLatestEarn: latest);

        var first = (await ledger.AppendAsync([Write(LedgerEntries.Earn, 5, 0, expectPaid: false)], Ct))[0];

        // A reversal then an earn of the same key, in one batch: the earn's guard sees the reversal.
        var repriced = await ledger.AppendAsync(
            [Write(LedgerEntries.Reversal, -5, 1, expectPaid: true, latest: first), Write(LedgerEntries.Earn, 8, 2, expectPaid: false)], Ct);

        // The same re-price decided against the first earn is refused, and writes nothing.
        await Assert.ThrowsAsync<LedgerConflictException>(() => ledger.AppendAsync(
            [Write(LedgerEntries.Reversal, -8, 3, expectPaid: true, latest: first), Write(LedgerEntries.Earn, 6, 4, expectPaid: false)], Ct));
        var held = (await ledger.GetKeyedAwardsAsync([FoodAwards.FastTrigger], Ct)).Single();
        Assert.Equal((8L, repriced[1], true), (held.Net[Currencies.HealthXp], held.LatestEarnId!.Value, held.Paid));
        Assert.Equal(new Dictionary<string, long> { [Currencies.HealthXp] = 8 }, held.LatestBase);
        Assert.Equal([cause], held.LatestSources);

        // An earn of a key the batch has just paid is refused too.
        await Assert.ThrowsAsync<LedgerConflictException>(() => ledger.AppendAsync(
            [Write(LedgerEntries.Reversal, -8, 5, expectPaid: true, latest: repriced[1]), Write(LedgerEntries.Earn, 6, 6, expectPaid: false), Write(LedgerEntries.Earn, 6, 7, expectPaid: false)], Ct));
        Assert.Equal(8L, (await ledger.GetKeyedAwardsAsync([FoodAwards.FastTrigger], Ct)).Single().Net[Currencies.HealthXp]);
    }
}
