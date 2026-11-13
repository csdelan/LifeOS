using System.Reflection;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR14: a fold of the life domains reads all of their history, so the sweep
/// (<see cref="RewardService.ReconcileAllAsync"/>) folds once and keeps that fold from item to
/// item until a write changes the domains' credit, and pays rank-ups once at the end. Before,
/// every item that paid domain XP folded twice.
/// </summary>
public sealed class RewardSweepTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sweep_folds_once_for_each_item_that_pays_domain_XP_and_once_for_rank_ups()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 10);
        var habits = new List<SubjectRef>();
        for (var i = 0; i < 3; i++)
        {
            var habit = await db.HabitAsync(today, startOffset: 3, healthXp: 5);
            await CheckOffWithoutReconcileAsync(db, habit, today);
            habits.Add(habit);
        }

        var services = new ServiceCollection().AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone);
        var counter = new CallCounter();
        var real = services.Single(d => d.ServiceType == typeof(IDomainCreditReader));
        services.Remove(real);
        services.AddSingleton(sp => CountingProxy<IDomainCreditReader>.Wrap((IDomainCreditReader)real.ImplementationFactory!(sp), counter));
        await using var kernel = services.BuildServiceProvider();

        var awards = await kernel.GetRequiredService<RewardService>().ReconcileAllAsync(Ct);

        Assert.Equal(3, awards.Count(a => habits.Any(h => h.Id == a.Subject?.Id) && a.Amounts.ContainsKey(Currencies.HealthXp)));
        // One fold to price each of the three, then one to pay rank-ups: four, not seven.
        Assert.Equal(4, counter.Count(nameof(IDomainCreditReader.GetDomainDaysAsync)));
    }

    // Code review CR46: the Character page and bsk rewards reconcile settle through one method. The
    // page's settle leaves an item to the reconcile its own write runs; the CLI's heals it too.
    [Fact]
    public async Task Settling_heals_an_item_only_when_asked_and_never_pays_twice()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 10);
        var habit = await db.HabitAsync(today, startOffset: 3, healthXp: 5);
        await CheckOffWithoutReconcileAsync(db, habit, today);
        var settlement = db.Get<SettlementService>();

        var page = await settlement.SettleAsync(cancellationToken: Ct);
        var cli = await settlement.SettleAsync(items: true, Ct);
        var again = await settlement.SettleAsync(items: true, Ct);

        Assert.DoesNotContain(page.Awards, a => a.Subject?.Id == habit.Id);
        Assert.Contains(cli.Awards, a => a.Subject?.Id == habit.Id && a.Amounts.ContainsKey(Currencies.HealthXp));
        Assert.Empty(again.Awards);
    }

    /// <summary>The habit followed on <paramref name="day"/>, written straight to the log with no reconcile after it.</summary>
    private async Task CheckOffWithoutReconcileAsync(DomainTestDatabase db, SubjectRef habit, DateOnly day)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Get<IEventStore>().AppendAsync(new NewEvent(
            EventKinds.Activity, Provenances.Declared, now, now, KernelSources.Cli,
            PayloadJson: JsonSerializer.Serialize(new
            {
                subject_id = habit.Id.ToString(),
                occurrence = day.ToString("yyyy-MM-dd"),
                result = AdherenceResults.Followed
            })), Ct);
    }

    public sealed class CallCounter
    {
        private readonly Dictionary<string, int> calls = [];

        internal void Add(string method)
        {
            lock (calls)
            {
                calls[method] = calls.GetValueOrDefault(method) + 1;
            }
        }

        internal int Count(string method)
        {
            lock (calls)
            {
                return calls.GetValueOrDefault(method);
            }
        }
    }

    /// <summary>Passes every call through to the real object, counting each method's calls.</summary>
    public class CountingProxy<T> : DispatchProxy
        where T : class
    {
        private T inner = null!;
        private CallCounter counter = null!;

        internal static T Wrap(T inner, CallCounter counter)
        {
            var proxy = Create<T, CountingProxy<T>>();
            var self = (CountingProxy<T>)(object)proxy;
            self.inner = inner;
            self.counter = counter;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            counter.Add(targetMethod!.Name);
            try
            {
                return targetMethod.Invoke(inner, args);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }
    }
}
