using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR78: what the Food Log and the Activity page show paid for their days is read by
/// those days' award keys (<see cref="ILedgerRepository.GetDayAwardsAsync"/>), in one shared read
/// (<see cref="DayAwardReads"/>). Before, each read every keyed award its triggers ever made and
/// kept the days it showed in C#, from a copy of the same loop in each.
/// </summary>
public sealed class DayAwardReadsTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An award paid straight into the ledger: its base, at a multiplier, keyed by its day.</summary>
    private async Task EarnAsync(DomainTestDatabase db, string trigger, DateOnly day, long @base, decimal multiplier)
    {
        var key = DayAwardKeys.Key(trigger, day);
        var now = DateTimeOffset.UtcNow;
        var paid = (long)Math.Round(@base * multiplier, MidpointRounding.AwayFromZero);
        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Earn,
            amounts = new Dictionary<string, long> { [Currencies.HealthXp] = paid },
            @base = new Dictionary<string, long> { [Currencies.HealthXp] = @base },
            multipliers = new Dictionary<string, decimal> { [Currencies.HealthXp] = multiplier },
            award_key = key,
            trigger,
            credited_on = day.ToString("yyyy-MM-dd")
        });
        await db.Get<ILedgerRepository>().AppendAsync(
            [new LedgerWrite(
                new NewEvent(EventKinds.Ledger, Provenances.Declared, now, now, KernelSources.Cli, PayloadJson: payload),
                [], GuardBalance: false, AwardKey: key, ExpectPaid: false)],
            Ct);
    }

    [Fact]
    public async Task The_ledger_answers_the_keys_of_the_days_asked_and_no_others()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var day = new DateOnly(2026, 11, 8);
        foreach (var ago in new[] { 2, 1, 0, -1 })
        {
            await EarnAsync(db, FoodAwards.DietTrigger, day.AddDays(-ago), 30, 1.3m);
        }
        await EarnAsync(db, FoodAwards.FastTrigger, day, 7, 1.3m);
        await EarnAsync(db, ActivityAwards.StepsTrigger, day, 20, 1.3m);

        var awards = await db.Get<ILedgerRepository>().GetDayAwardsAsync(FoodAwards.Triggers, day.AddDays(-1), day, Ct);

        Assert.Equal(
            [FoodAwards.DietKey(day.AddDays(-1)), FoodAwards.DietKey(day), FoodAwards.FastKey(day)],
            awards.Select(a => a.AwardKey).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_food_log_and_the_activity_page_read_only_their_days_awards()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var day = new DateOnly(2026, 11, 8);
        await EarnAsync(db, FoodAwards.DietTrigger, day.AddDays(-3), 30, 1.3m);
        await EarnAsync(db, FoodAwards.DietTrigger, day, 32, 1.3m);
        await EarnAsync(db, FoodAwards.FastTrigger, day, 7, 1.3m);
        await EarnAsync(db, ActivityAwards.StepsTrigger, day.AddDays(-3), 15, 1m);
        await EarnAsync(db, ActivityAwards.StepsTrigger, day, 20, 1.3m);
        await EarnAsync(db, ActivityAwards.SleepTrigger, day, 15, 1.3m);

        var counter = new RewardSweepTests.CallCounter();
        var ledger = RewardSweepTests.CountingProxy<ILedgerRepository>.Wrap(db.Get<ILedgerRepository>(), counter);
        var food = await new FoodReads(db.Get<IFoodLogRepository>(), ledger, db.Get<IHomeCalendar>()).GetPaidAsync(day, day, Ct);
        var activity = await new ActivityPaidReads(ledger).GetPaidAsync(day.AddDays(-1), day, Ct);

        var foodDay = Assert.Single(food);
        Assert.Equal((day, new DayAwardPaid(7, 9, 1.3m), new DayAwardPaid(32, 42, 1.3m)), (foodDay.Key, foodDay.Value.Fast, foodDay.Value.Diet));
        var activityDay = Assert.Single(activity);
        Assert.Equal(
            (day, new DayAwardPaid(20, 26, 1.3m), (DayAwardPaid?)null, new DayAwardPaid(15, 20, 1.3m)),
            (activityDay.Key, activityDay.Value.Steps, activityDay.Value.Exercise, activityDay.Value.Sleep));

        // Neither read every keyed award of its triggers.
        Assert.Equal(0, counter.Count(nameof(ILedgerRepository.GetKeyedAwardsAsync)));
        Assert.Equal(2, counter.Count(nameof(ILedgerRepository.GetDayAwardsAsync)));
    }

    [Fact]
    public void A_day_award_key_is_its_trigger_and_its_day()
    {
        var day = new DateOnly(2026, 11, 8);
        Assert.Equal("diet:2026-11-08", FoodAwards.DietKey(day));
        Assert.Equal("steps:2026-11-08", ActivityAwards.Key(ActivityAwards.StepsTrigger, day));
        Assert.Equal(("fast", day), DayAwardKeys.Parse("fast:2026-11-08", FoodAwards.Triggers));
        Assert.Null(DayAwardKeys.Parse("steps:2026-11-08", FoodAwards.Triggers));
        Assert.Null(DayAwardKeys.Parse("diet:not-a-day", FoodAwards.Triggers));
        Assert.Null(DayAwardKeys.Parse("diet", FoodAwards.Triggers));
        Assert.Equal(
            ["diet:2026-11-07", "fast:2026-11-07", "diet:2026-11-08", "fast:2026-11-08"],
            DayAwardKeys.Keys([FoodAwards.DietTrigger, FoodAwards.FastTrigger], day.AddDays(-1), day));
        Assert.Empty(DayAwardKeys.Keys(FoodAwards.Triggers, day, day.AddDays(-1)));
    }
}
