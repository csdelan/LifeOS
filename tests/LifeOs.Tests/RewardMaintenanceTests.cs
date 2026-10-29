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
/// REWARD-2 revision (xp-domains plan X2, migration 0031): an established habit keeps
/// earning through logged maintenance. A day logged after graduation pays the habit's day
/// price under the usual <c>day:</c> key; an unlogged one is never a miss; no streak
/// milestone pays after graduation; Abandoned habits and moving back to Building behave as
/// before.
/// <para>
/// A status change is always now, so these habits graduate through a status event written
/// days back. That event is from before the wallet opened, so its establishment award
/// isn't due: each test looks only at the day and streak awards. The database is shared, so
/// each test reasons about its own habits.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardMaintenanceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, SubjectService Subjects, AdherenceService Adherence, StatusService Status,
        RewardService Rewards, BountyService Bounties, ILedgerRepository Ledger, IEventStore Events);

    private Kernel Build()
    {
        var provider = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<AdherenceService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<RewardService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>(),
            provider.GetRequiredService<IEventStore>());
    }

    /// <summary>
    /// A daily habit that started <paramref name="startOffset"/> days ago and moved to
    /// <paramref name="status"/> <paramref name="graduatedDaysAgo"/> days ago.
    /// </summary>
    private async Task<SubjectRef> GraduatedHabitAsync(
        Kernel kernel, int startOffset, string status, int graduatedDaysAgo, bool allowsPartial = false)
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

        // With no status, so the status change written days back is its newest.
        var habit = await kernel.Provider.CreateHabitWithoutStatusAsync(
            $"maintained habit {Guid.NewGuid():N}", JsonSerializer.Serialize(attributes), Ct);
        // Midday UTC, so the graduation date is the same whichever zone reads it.
        var graduatedAt = new DateTimeOffset(Today.AddDays(-graduatedDaysAgo).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        await kernel.Events.AppendAsync(
            new NewEvent(EventKinds.StateChange, Provenances.Declared, graduatedAt, DateTimeOffset.UtcNow, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = habit.Id.ToString(), status })),
            Ct);
        return habit;
    }

    private Task<AdherenceResult> RecordAsync(Kernel kernel, SubjectRef habit, string result, int daysAgo = 0)
        => kernel.Adherence.RecordAsync(habit.Urn, result, Today.AddDays(-daysAgo), cancellationToken: Ct);

    private static IEnumerable<Award> DayAndStreakAwards(IEnumerable<Award> awards)
        => awards.Where(a => a.Trigger != BountyTriggers.Established);

    private async Task<HashSet<string>> PaidKeysAsync(Kernel kernel, SubjectRef habit)
        => (await kernel.Ledger.GetPaidAsync(habit.Id, Ct)).Select(p => p.AwardKey).ToHashSet(StringComparer.Ordinal);

    private async Task<string?> OccurrenceStateAsync(SubjectRef habit, DateOnly date)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT state FROM bsk.v_habit_occurrence WHERE habit_id = @id AND occurrence_date = @d::date;",
            new { id = habit.Id, d = date.ToString("yyyy-MM-dd") }, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_day_logged_on_an_established_habit_pays_its_day_price_and_a_partial_half()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 5, allowsPartial: true);
        await kernel.Bounties.SetAsync(
            habit.Urn, BountyTriggers.Followed, new Dictionary<string, long> { [Currencies.HealthXp] = 11 },
            cancellationToken: Ct);

        var followed = Assert.Single(DayAndStreakAwards((await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 2)).Awards));
        var partial = Assert.Single(DayAndStreakAwards((await RecordAsync(kernel, habit, AdherenceResults.Partial)).Awards));

        Assert.Equal(LedgerEntries.Earn, followed.Entry);
        Assert.Equal(AwardKeys.Day(habit.Id, Today.AddDays(-2), AdherenceResults.Followed), followed.AwardKey);
        Assert.Equal(11, followed.Amounts[Currencies.HealthXp]);
        Assert.Equal(AwardKeys.Day(habit.Id, Today, AdherenceResults.Partial), partial.AwardKey);
        Assert.Equal(5, partial.Amounts[Currencies.HealthXp]);
    }

    [Fact]
    public async Task Undoing_a_maintained_day_gives_its_reward_back()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 5);
        await RecordAsync(kernel, habit, AdherenceResults.Followed);

        var undone = Assert.Single(DayAndStreakAwards((await RecordAsync(kernel, habit, AdherenceResults.Missed)).Awards));

        Assert.Equal(LedgerEntries.Reversal, undone.Entry);
        Assert.Equal(AwardKeys.Day(habit.Id, Today, AdherenceResults.Followed), undone.AwardKey);
        Assert.Empty(await PaidKeysAsync(kernel, habit));
    }

    [Fact]
    public async Task An_unlogged_day_on_an_established_habit_is_never_missed()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 5);
        await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 3);

        // Before graduation the schedule ran, so an unlogged day there is missed; after it
        // only logged days exist.
        Assert.Equal("missed", await OccurrenceStateAsync(habit, Today.AddDays(-6)));
        Assert.Equal(AdherenceResults.Followed, await OccurrenceStateAsync(habit, Today.AddDays(-3)));
        foreach (var daysAgo in new[] { 4, 2, 1, 0 })
        {
            Assert.Null(await OccurrenceStateAsync(habit, Today.AddDays(-daysAgo)));
        }
    }

    [Fact]
    public async Task No_streak_milestone_pays_after_establishment()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 9);

        var awards = new List<Award>();
        for (var daysAgo = 8; daysAgo >= 0; daysAgo--)
        {
            awards.AddRange((await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo)).Awards);
        }

        // Nine maintained days in a row: nine day rewards, no 7-day milestone.
        var paid = DayAndStreakAwards(awards).ToList();
        Assert.Equal(9, paid.Count);
        Assert.All(paid, a => Assert.Equal(BountyTriggers.Followed, a.Trigger));
    }

    [Fact]
    public async Task An_abandoned_habit_still_earns_nothing_for_days_after_abandoning()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Abandoned", graduatedDaysAgo: 5);

        var recorded = await RecordAsync(kernel, habit, AdherenceResults.Followed);

        Assert.Empty(recorded.Awards);
        Assert.Equal(AdherenceResults.Followed, await OccurrenceStateAsync(habit, Today));
    }

    [Fact]
    public async Task Abandoning_an_established_habit_keeps_what_its_maintained_days_earned()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 5);
        await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 1);
        var dayKey = AwardKeys.Day(habit.Id, Today.AddDays(-1), AdherenceResults.Followed);

        var abandoned = await kernel.Status.ChangeStatusAsync(habit.Urn, "Abandoned", Ct);

        Assert.DoesNotContain(abandoned.Awards, a => a.AwardKey == dayKey);
        Assert.Contains(dayKey, await PaidKeysAsync(kernel, habit));
    }

    [Fact]
    public async Task Moving_a_maintained_habit_back_to_building_keeps_its_days_and_resumes_the_schedule()
    {
        var kernel = Build();
        var habit = await GraduatedHabitAsync(kernel, startOffset: 10, "Established", graduatedDaysAgo: 5);
        await RecordAsync(kernel, habit, AdherenceResults.Followed, daysAgo: 1);
        var dayKey = AwardKeys.Day(habit.Id, Today.AddDays(-1), AdherenceResults.Followed);

        var building = await kernel.Status.ChangeStatusAsync(habit.Urn, "Building", Ct);

        Assert.DoesNotContain(building.Awards, a => a.AwardKey == dayKey);
        Assert.Contains(dayKey, await PaidKeysAsync(kernel, habit));
        // Back in daily tracking: the unlogged days since graduation read as missed again,
        // and today is open.
        Assert.Equal("missed", await OccurrenceStateAsync(habit, Today.AddDays(-2)));
        Assert.Equal("unrecorded", await OccurrenceStateAsync(habit, Today));
    }
}
