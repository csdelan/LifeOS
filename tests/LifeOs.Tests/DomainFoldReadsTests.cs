using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// A write that earns life-domain XP reads all of the domains' history once (code review CR123,
/// fifth review R18). It read it twice, before the write and after it; now the after is the first
/// read with only the write's own awards' credit, or the exception days and the Home, read again,
/// and all of history is read again only to pay a rank-up. What the write leaves the domains is
/// what a read of all of history finds.
/// </summary>
public sealed class DomainFoldReadsTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A kernel on <paramref name="db"/> whose domain-credit reads are counted.</summary>
    private static ServiceProvider Counted(DomainTestDatabase db, RewardSweepTests.CallCounter counter)
    {
        var services = new ServiceCollection().AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone);
        var real = services.Single(d => d.ServiceType == typeof(IDomainCreditReader));
        services.Remove(real);
        services.AddSingleton(sp => RewardSweepTests.CountingProxy<IDomainCreditReader>.Wrap(
            (IDomainCreditReader)real.ImplementationFactory!(sp), counter));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_check_off_reads_all_of_history_once_and_again_only_to_pay_a_rank()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 40);
        var habit = await db.HabitAsync(today, startOffset: 30, healthXp: 200);
        var counter = new RewardSweepTests.CallCounter();
        await using var kernel = Counted(db, counter);
        var adherence = kernel.GetRequiredService<AdherenceService>();
        var reads = 0;
        int Reads()
        {
            var now = counter.Count(nameof(IDomainCreditReader.GetDomainDaysAsync));
            var these = now - reads;
            reads = now;
            return these;
        }

        // Ten followed days, as DomainRankAwardTests has them: no rank yet, one read of history each.
        for (var ago = 13; ago > 3; ago--)
        {
            var day = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-ago), cancellationToken: Ct);
            Assert.DoesNotContain(day.Awards, a => a.Trigger == LifeDomains.RankTrigger);
            Assert.True(Reads() == 1, $"{ago} days back read all of history more than once.");
        }

        // The eleventh reaches Apprentice: history is read again to pay it.
        var eleventh = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-3), cancellationToken: Ct);
        Assert.Single(eleventh.Awards, a => a.Trigger == LifeDomains.RankTrigger);
        Assert.Equal(2, Reads());
        var change = Assert.Single(eleventh.Progress);
        Assert.Equal((9, 10, 0, 1), (change.LevelFrom, change.LevelTo, change.RankFrom, change.RankTo));

        // The twelfth moves the level and pays nothing: one read, and the same answer a read of all of history gives.
        var twelfth = await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-2), cancellationToken: Ct);
        Assert.Equal(1, Reads());
        Assert.Equal([new DomainChange(Currencies.HealthXp, 10, 11, 1, 1, "Apprentice", 10, 11, 1, 1)], twelfth.Progress);

        // Two reads of the habit day's own credit for each, before and after its write.
        Assert.Equal(24, counter.Count(nameof(IDomainCreditReader.GetAwardCreditAsync)));
    }

    [Fact]
    public async Task What_an_earn_a_reversal_or_an_exception_day_leaves_the_domains_is_what_all_of_history_reads()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 20);
        var rankUps = db.Get<RankUpService>();
        var status = db.Get<StatusService>();
        var adherence = db.Get<AdherenceService>();
        var task = await db.Get<SubjectService>().CreateAsync(SubjectTypes.Task, "Clear the gutters", "{}", cancellationToken: Ct);
        await db.Get<BountyService>().SetAsync(task.Urn, null, new Dictionary<string, long> { [Currencies.EnvironmentXp] = 30 }, cancellationToken: Ct);
        var habit = await db.HabitAsync(today, startOffset: 10, healthXp: 20);
        await adherence.RecordAsync(habit.Urn, AdherenceResults.Followed, today.AddDays(-6), cancellationToken: Ct);
        var key = AwardKeys.Completed(task.Id);

        async Task SameAsAllOfHistoryAsync(string step, Func<CancellationToken, Task> write)
        {
            var before = await rankUps.ReadAsync(Ct);
            var (_, after) = await rankUps.AroundAwardsAsync(before, [key], async ct => { await write(ct); return 0; }, Ct);
            Assert.True(after.Credit.Days.Count > 0, step);
            Assert.Equivalent((await rankUps.ReadAsync(Ct)).Credit, after.Credit, strict: true);
        }

        // An earn on a day with other credit, its reversal, and the earn again.
        await SameAsAllOfHistoryAsync("earn", ct => status.ChangeStatusAsync(task.Urn, "Completed", ct));
        await SameAsAllOfHistoryAsync("reversal", ct => status.ChangeStatusAsync(task.Urn, "In progress", ct));
        await SameAsAllOfHistoryAsync("earned again", ct => status.ChangeStatusAsync(task.Urn, "Completed", ct));

        // An exception day writes no award: the exception days and the Home are read again.
        var credit = (await rankUps.ReadAsync(Ct)).Credit;
        await db.Get<ExceptionDayService>().MarkAsync(today.AddDays(-4), "Flu", Ct);
        var reread = await db.Get<IDomainCreditReader>().RereadBesideAsync(credit, Ct);
        Assert.Single(reread.ExceptionDays);
        Assert.Equivalent((await rankUps.ReadAsync(Ct)).Credit, reread, strict: true);
    }
}
