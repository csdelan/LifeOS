using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-1 / TASKS-2 (migration 0029): a Task can pay for the time its timer recorded. The
/// <c>timed</c> trigger pays its price once for every <c>every_minutes</c> (default 1) the
/// timer recorded before the completion, on top of <c>completed</c>. It is opt-in: no
/// default price, and nothing without the timer.
/// <para>
/// Completions happen now (the shared wallet may have opened moments ago, and nothing before
/// the opening pays); the timer's actions are back-dated, which is what makes the time.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RewardTimedTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kernel(
        ServiceProvider Provider, SubjectService Subjects, StatusService Status, BountyService Bounties,
        ILedgerRepository Ledger, IEventStore Events);

    private Kernel Build()
    {
        var provider = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        return new Kernel(
            provider,
            provider.GetRequiredService<SubjectService>(),
            provider.GetRequiredService<StatusService>(),
            provider.GetRequiredService<BountyService>(),
            provider.GetRequiredService<ILedgerRepository>(),
            provider.GetRequiredService<IEventStore>());
    }

    private Task<SubjectRef> TaskAsync(Kernel kernel)
        => kernel.Subjects.CreateAsync(SubjectTypes.Task, $"timed task {Guid.NewGuid():N}", "{}", cancellationToken: Ct);

    /// <summary>Prices the task's time: <paramref name="amounts"/> per <paramref name="everyMinutes"/>.</summary>
    private Task PriceTimeAsync(Kernel kernel, SubjectRef task, Dictionary<string, long> amounts, int? everyMinutes = null)
        => kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Timed, amounts, everyMinutes, Ct);

    /// <summary>Timer actions, each <c>(minutes ago, action)</c>, written as the timer writes them.</summary>
    private async Task TimeAsync(Kernel kernel, SubjectRef task, params (double MinutesAgo, string Action)[] actions)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (minutesAgo, action) in actions)
        {
            await kernel.Events.AppendAsync(new NewEvent(
                EventKinds.Activity, Provenances.Declared, now.AddMinutes(-minutesAgo), now, KernelSources.Cli,
                PayloadJson: JsonSerializer.Serialize(new { subject_id = task.Id.ToString(), timer = action })), Ct);
        }
    }

    private static Award TimedAward(IEnumerable<Award> awards)
        => Assert.Single(awards, a => a.Trigger == BountyTriggers.Timed);

    [Fact]
    public async Task Time_on_the_timer_pays_once_per_block_of_minutes_on_top_of_completing()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);
        await PriceTimeAsync(kernel, task, new() { [Currencies.Xp] = 2, [Currencies.Coins] = 1 }, everyMinutes: 15);
        // 20 minutes, a pause, then 19 more: 39 minutes is two whole blocks of 15.
        await TimeAsync(kernel, task, (60, "start"), (40, "pause"), (30, "resume"), (11, "stop"));

        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);

        Assert.Equal(
            [BountyTriggers.Completed, BountyTriggers.Timed],
            completed.Awards.Select(a => a.Trigger).Order(StringComparer.Ordinal));
        var timed = TimedAward(completed.Awards);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Coins] = 2, [Currencies.Xp] = 4 }, timed.Amounts);
        Assert.Equal(AwardKeys.Timed(task.Id), timed.AwardKey);
        // Completing still pays its own price, untouched by the time.
        Assert.Equal(
            BountyDefaults.For(SubjectTypes.Task, BountyTriggers.Completed),
            Assert.Single(completed.Awards, a => a.Trigger == BountyTriggers.Completed).Amounts);

        var trigger = Assert.Single(await kernel.Ledger.GetTriggersAsync(task.Id, Ct), t => t.Trigger == BountyTriggers.Timed);
        Assert.InRange(trigger.TimedSeconds!.Value, 39 * 60 - 1, 39 * 60 + 1);
    }

    [Fact]
    public async Task Time_pays_nothing_by_default_and_nothing_without_the_timer()
    {
        var kernel = Build();

        // Timed, but its time was never priced: completing pays only for completing.
        var unpriced = await TaskAsync(kernel);
        await TimeAsync(kernel, unpriced, (30, "start"), (5, "stop"));
        var view = await kernel.Bounties.GetAsync(unpriced.Urn, Ct);
        var line = Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Timed);
        Assert.Empty(line.Amounts);
        Assert.Equal(1, line.EveryMinutes);
        var completed = await kernel.Status.ChangeStatusAsync(unpriced.Urn, "Completed", Ct);
        Assert.Equal(BountyTriggers.Completed, Assert.Single(completed.Awards).Trigger);

        // Priced, but done without the timer: nothing for time.
        var untimed = await TaskAsync(kernel);
        await PriceTimeAsync(kernel, untimed, new() { [Currencies.Xp] = 1 });
        completed = await kernel.Status.ChangeStatusAsync(untimed.Urn, "Completed", Ct);
        Assert.Equal(BountyTriggers.Completed, Assert.Single(completed.Awards).Trigger);
        Assert.DoesNotContain(await kernel.Ledger.GetTriggersAsync(untimed.Id, Ct), t => t.Trigger == BountyTriggers.Timed);

        // Less than one block pays nothing either.
        var brief = await TaskAsync(kernel);
        await PriceTimeAsync(kernel, brief, new() { [Currencies.Xp] = 1 }, everyMinutes: 30);
        await TimeAsync(kernel, brief, (20, "start"), (1, "stop"));
        completed = await kernel.Status.ChangeStatusAsync(brief.Urn, "Completed", Ct);
        Assert.Equal(BountyTriggers.Completed, Assert.Single(completed.Awards).Trigger);
    }

    [Fact]
    public async Task A_timer_running_at_completion_counts_up_to_it_and_later_time_never_counts()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);
        await PriceTimeAsync(kernel, task, new() { [Currencies.Xp] = 1 });
        await TimeAsync(kernel, task, (5, "start"));

        // Completed by status, not by the timer: the running segment ends at the completion.
        var completed = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        Assert.Equal(new Dictionary<string, long> { [Currencies.Xp] = 5 }, TimedAward(completed.Awards).Amounts);

        // The timer keeps going after the completion; that time is never paid.
        await TimeAsync(kernel, task, (-30, "stop"));
        var swept = await kernel.Provider.GetRequiredService<RewardService>().ReconcileAsync(task, causeEventId: null, Ct);
        Assert.Empty(swept);
    }

    [Fact]
    public async Task Reopening_gives_the_time_back_and_completing_again_pays_on_all_of_it()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);
        await PriceTimeAsync(kernel, task, new() { [Currencies.Freedom] = 1 }, everyMinutes: 10);
        await TimeAsync(kernel, task, (60, "start"), (40, "stop"));

        var first = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        Assert.Equal(2, TimedAward(first.Awards).Amounts[Currencies.Freedom]);

        var reopened = await kernel.Status.ChangeStatusAsync(task.Urn, "In progress", Ct);
        var givenBack = TimedAward(reopened.Awards);
        Assert.Equal(LedgerEntries.Reversal, givenBack.Entry);
        Assert.Equal(-2, givenBack.Amounts[Currencies.Freedom]);

        // Ten more minutes on the timer, then done again: paid on all thirty.
        await TimeAsync(kernel, task, (30, "start"), (20, "stop"));
        var again = await kernel.Status.ChangeStatusAsync(task.Urn, "Completed", Ct);
        Assert.Equal(3, TimedAward(again.Awards).Amounts[Currencies.Freedom]);
    }

    [Fact]
    public async Task Minutes_belong_to_timed_from_one_up_and_clearing_puts_them_back()
    {
        var kernel = Build();
        var task = await TaskAsync(kernel);

        // A Task left without a trigger prices completing, as it did before time could pay.
        var view = await kernel.Bounties.SetAsync(task.Urn, null, new Dictionary<string, long> { [Currencies.Coins] = 3 }, cancellationToken: Ct);
        Assert.Equal(3, Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Completed).Amounts[Currencies.Coins]);

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Completed, new Dictionary<string, long> { ["xp"] = 1 }, 15, Ct));
        Assert.Contains("Only timed pays by the minutes on the timer", refused.Message);
        refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Timed, null, 0, Ct));
        Assert.Contains("from 1 up; got 0", refused.Message);

        await PriceTimeAsync(kernel, task, new() { [Currencies.Xp] = 2 }, everyMinutes: 25);
        // Minutes alone keep the price; amounts alone keep the minutes.
        view = await kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Timed, null, 45, Ct);
        var timed = Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Timed);
        Assert.Equal((2L, 45), (timed.Amounts[Currencies.Xp], timed.EveryMinutes));
        view = await kernel.Bounties.SetAsync(task.Urn, BountyTriggers.Timed, new Dictionary<string, long> { ["karma"] = 1 }, cancellationToken: Ct);
        timed = Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Timed);
        Assert.Equal((1L, 45), (timed.Amounts[Currencies.Karma], timed.EveryMinutes));
        Assert.Equal(3, Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Completed).Amounts[Currencies.Coins]);

        view = await kernel.Bounties.ClearAsync(task.Urn, BountyTriggers.Timed, Ct);
        timed = Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Timed);
        Assert.Equal((0, 1, false), (timed.Amounts.Count, timed.EveryMinutes, timed.IsCustom));
        Assert.True(Assert.Single(view.Lines, l => l.Trigger == BountyTriggers.Completed).IsCustom);
    }
}
