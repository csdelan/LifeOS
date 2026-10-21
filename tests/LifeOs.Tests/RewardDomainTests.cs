using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 (xp-domains plan X1, migration 0030): the four life-domain XP currencies and
/// the day an earn counts on.
/// <list type="bullet">
/// <item>Health, Social, Environment and Finance XP are tracked, never spent. No default
/// pays them; an item pays them only once it's priced with them, on any trigger.</item>
/// <item>Each earn stamps <c>credited_on</c>, the local day it happened: a habit day's own
/// date (even when logged late), a streak's last day, and otherwise the instant's date in
/// the home zone (<c>Rewards:HomeTimeZone</c>, Pacific by default).</item>
/// </list>
/// The database is shared, so each test reasons about its own items. The zone tests read
/// triggers from 2003, which are before the wallet opened: they write no ledger entries.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardDomainTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, SubjectService Subjects, StatusService Status, AdherenceService Adherence,
        RewardService Rewards, BountyService Bounties, ILedgerRepository Ledger, IEventStore Events);

    private Kernel Build(string? homeTimeZone = null)
    {
        var provider = new ServiceCollection()
            .AddLifeOsKernel(postgres.ConnectionString, homeTimeZone: homeTimeZone)
            .BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<AdherenceService>(),
            provider.GetRequiredService<RewardService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>(),
            provider.GetRequiredService<IEventStore>());
    }

    private Task<SubjectRef> TaskAsync(Kernel kernel)
        => kernel.Subjects.CreateAsync(SubjectTypes.Task, $"domain task {Guid.NewGuid():N}", "{}", cancellationToken: Ct);

    /// <summary>A daily habit that started <paramref name="startOffset"/> days ago.</summary>
    private Task<SubjectRef> HabitAsync(Kernel kernel, int startOffset, bool allowsPartial = false)
    {
        var attributes = new Dictionary<string, object>
        {
            ["start"] = Today.AddDays(-startOffset).ToString("yyyy-MM-dd"),
            ["recurrence"] = new { freq = "daily" }
        };
        if (allowsPartial)
        {
            attributes["allows_partial"] = "true";
        }

        return kernel.Subjects.CreateAsync(
            SubjectTypes.Habit, $"domain habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attributes),
            cancellationToken: Ct);
    }

    private Task<AdherenceResult> RecordAsync(Kernel kernel, SubjectRef habit, string result, int daysAgo = 0)
        => kernel.Adherence.RecordAsync(habit.Urn, result, Today.AddDays(-daysAgo), cancellationToken: Ct);

    private static Dictionary<string, long> Health(long amount) => new() { [Currencies.HealthXp] = amount };

    /// <summary>A status change at <paramref name="occurredAt"/>, written behind reconcile's back.</summary>
    private Task<Guid> RecordStatusAtAsync(Kernel kernel, SubjectRef subject, string status, DateTimeOffset occurredAt)
        => kernel.Events.AppendAsync(
            new NewEvent(EventKinds.StateChange, Provenances.Declared, occurredAt, DateTimeOffset.UtcNow, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), status })),
            Ct);

    /// <summary>The <c>credited_on</c> a ledger entry carries, or null when it carries none.</summary>
    private async Task<string?> CreditedOnAsync(Guid ledgerEventId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT payload->>'credited_on' FROM bsk.event WHERE id = @id;",
            new { id = ledgerEventId }, cancellationToken: Ct));
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    [Fact]
    public void The_four_life_domains_are_tracked_currencies_and_no_default_pays_them()
    {
        Assert.Equal(
            new[] { Currencies.HealthXp, Currencies.SocialXp, Currencies.EnvironmentXp, Currencies.FinanceXp },
            Currencies.LifeDomains.Select(c => c.Key));
        Assert.All(Currencies.LifeDomains, c => Assert.False(c.Spendable));
        Assert.Equal("Life XP", Currencies.Find(Currencies.Xp)!.Label);

        foreach (var type in BountyTriggers.EarningTypes)
        {
            foreach (var trigger in BountyTriggers.For(type))
            {
                var defaults = BountyDefaults.For(type, trigger);
                Assert.DoesNotContain(defaults.Keys, key => Currencies.Find(key)!.LifeDomain);
            }
        }
    }

    [Fact]
    public async Task A_task_priced_with_health_xp_pays_it_on_completion_and_an_unpriced_one_pays_none()
    {
        var kernel = Build();
        var priced = await TaskAsync(kernel);
        var unpriced = await TaskAsync(kernel);
        await kernel.Bounties.SetAsync(priced.Urn, null, Health(25), cancellationToken: Ct);

        var pricedAward = Assert.Single((await kernel.Status.ChangeStatusAsync(priced.Urn, "Completed", Ct)).Awards);
        var unpricedAward = Assert.Single((await kernel.Status.ChangeStatusAsync(unpriced.Urn, "Completed", Ct)).Awards);

        // On top of the type's default, which pays no domain XP.
        Assert.Equal(25, pricedAward.Amounts[Currencies.HealthXp]);
        Assert.DoesNotContain(unpricedAward.Amounts.Keys, key => Currencies.Find(key)!.LifeDomain);
        Assert.Equal(25, Assert.Single(await kernel.Ledger.GetPaidAsync(priced.Id, Ct)).Net[Currencies.HealthXp]);
    }

    [Fact]
    public async Task A_habit_pays_its_health_xp_on_a_followed_day_and_half_on_a_partial_one()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 2, allowsPartial: true);
        await kernel.Bounties.SetAsync(habit.Urn, BountyTriggers.Followed, Health(11), cancellationToken: Ct);

        var followed = Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 1)).Awards);
        var partial = Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Partial)).Awards);

        Assert.Equal(11, followed.Amounts[Currencies.HealthXp]);
        Assert.Equal(5, partial.Amounts[Currencies.HealthXp]);
    }

    [Fact]
    public async Task Time_on_the_timer_pays_health_xp_per_block()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);
        await kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Timed, Health(3), everyMinutes: 10, Ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var (minutesAgo, action) in new[] { (30.0, "start"), (5.0, "stop") })
        {
            await kernel.Events.AppendAsync(new NewEvent(
                EventKinds.Activity, Provenances.Declared, now.AddMinutes(-minutesAgo), now, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = task.Id.ToString(), timer = action })), Ct);
        }

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        // 25 minutes is two whole blocks of 10.
        var timed = Assert.Single(completed.Awards, a => a.Trigger == BountyTriggers.Timed);
        Assert.Equal(Health(6), timed.Amounts);
    }

    [Fact]
    public async Task A_habit_day_logged_late_counts_on_the_day_it_happened()
    {
        var kernel = Build();
        var habit = await HabitAsync(kernel, startOffset: 4);

        var earn = Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 3)).Awards);

        Assert.Equal(Iso(Today.AddDays(-3)), await CreditedOnAsync(earn.EventId));

        // A reversal gives back an earn; it counts on no day of its own.
        var reversal = Assert.Single((await RecordAsync(kernel, habit, AdherenceResults.Missed, daysAgo: 3)).Awards);
        Assert.Equal(LedgerEntries.Reversal, reversal.Entry);
        Assert.Null(await CreditedOnAsync(reversal.EventId));
    }

    [Fact]
    public async Task A_streak_counts_on_its_last_day()
    {
        var kernel = Build();
        // Day -9 is scheduled and unrecorded (missed), so the run is days -8 to -2.
        var habit = await HabitAsync(kernel, startOffset: 9);
        AdherenceResult last = null!;
        for (var day = 8; day >= 2; day--)
        {
            last = await RecordAsync(kernel, habit, AdherenceResults.Followed, day);
        }

        var streak = Assert.Single(last.Awards, a => a.Trigger == BountyTriggers.Streak(7));
        Assert.Equal(Iso(Today.AddDays(-2)), await CreditedOnAsync(streak.EventId));
    }

    [Fact]
    public async Task A_completion_counts_on_its_date_in_the_home_zone()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);
        // 06:30 UTC on 15 Feb 2003 is 10:30 pm on the 14th in Pacific time.
        await RecordStatusAtAsync(kernel, task, "Completed", new DateTimeOffset(2003, 2, 15, 6, 30, 0, TimeSpan.Zero));

        var pacific = Assert.Single(await kernel.Ledger.GetTriggersAsync(task.Id, Ct));
        var utc = Assert.Single(await Build(homeTimeZone: "UTC").Ledger.GetTriggersAsync(task.Id, Ct));

        Assert.Equal(new DateOnly(2003, 2, 14), pacific.CreditedOn);
        Assert.Equal(new DateOnly(2003, 2, 15), utc.CreditedOn);
    }

    [Fact]
    public async Task A_completion_earned_now_is_stamped_with_todays_date_in_the_home_zone()
    {
        var kernel = Build(homeTimeZone: "UTC");
        var task = await TaskAsync(kernel);

        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var earn = Assert.Single((await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct)).Awards);
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Contains(await CreditedOnAsync(earn.EventId), new[] { Iso(before), Iso(after) });
    }

    [Fact]
    public async Task Domain_xp_cannot_be_redeemed()
    {
        var kernel = Build();

        var refused = await Assert.ThrowsAsync<ArgumentException>(
            () => kernel.Rewards.RedeemAsync(Health(5), "a treat", Ct));

        Assert.Equal("Health XP is tracked, not spent; it can't be redeemed.", refused.Message);
    }

    [Theory]
    [InlineData(null, HomeZone.Default)]
    [InlineData("   ", HomeZone.Default)]
    [InlineData("  UTC ", "UTC")]
    [InlineData("America/New_York", "America/New_York")]
    [InlineData("America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("Etc/GMT+8", "Etc/GMT+8")]
    public void A_configured_home_zone_is_an_iana_name_or_the_default(string? configured, string expected)
        => Assert.Equal(expected, ServiceCollectionExtensions.ParseHomeTimeZone(configured));

    [Theory]
    [InlineData("Pacific Standard Time")]
    [InlineData("America/")]
    [InlineData("-08:00")]
    public void A_home_zone_that_isnt_an_iana_name_is_refused(string configured)
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => ServiceCollectionExtensions.ParseHomeTimeZone(configured));
        Assert.Contains(HomeZone.ConfigurationKey, refused.Message);
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString, homeTimeZone: configured));
    }

    [Fact]
    public async Task A_home_zone_the_database_doesnt_know_fails_loudly_on_first_use()
    {
        var kernel = Build(homeTimeZone: "Mars/Olympus");
        var task = await TaskAsync(kernel);
        await RecordStatusAtAsync(kernel, task, "Completed", new DateTimeOffset(2003, 3, 1, 12, 0, 0, TimeSpan.Zero));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => kernel.Ledger.GetTriggersAsync(task.Id, Ct));

        Assert.Contains("Mars/Olympus", refused.Message);
        Assert.Contains(HomeZone.ConfigurationKey, refused.Message);
    }
}
