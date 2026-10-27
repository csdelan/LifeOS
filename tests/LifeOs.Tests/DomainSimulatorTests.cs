using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 / REWARD-8 (xp-domains plan X10, §7.3, §7.4.11): the simulator's regression
/// scenarios — ten seeded years of realistic earning under Health's v2 rules, multiplier
/// included, each with the band it must land in, so a retune that breaks progression fails the
/// build. The bands were set from C# runs and checked against the Python model's §7.3 targets.
/// </summary>
public sealed class DomainSimulatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 21);
    private static readonly DomainRules Health = LifeDomains.Health;
    private static readonly SimulationOptions TenYears = new(Years: 10, Runs: 20, Seed: 1);

    /// <summary>The calibration routine (activity plan §3.8): about 79 base XP a day, up to 15 of it sleep.</summary>
    private static readonly SimulationPattern Steady = new(AverageXp: 82, SpreadXp: 15, OffShare: 0.02m, HalfShare: 0.02m);

    /// <summary>The same routine with 10% of days off: about 72 a day.</summary>
    private static readonly SimulationPattern Human = Steady with { OffShare = 0.10m };

    /// <summary>High activity: about 100 a day.</summary>
    private static readonly SimulationPattern High = Steady with { AverageXp = 104 };

    /// <summary>Six months off, starting three years in.</summary>
    private static readonly SimulationLapse SixMonths = new(AfterDays: 3 * 365, LengthDays: 182);

    private static SimulationResult Simulate(SimulationPattern pattern, SimulationOptions? options = null)
        => DomainSimulator.Run(Health, pattern, options ?? TenYears, Start);

    private static RankOutcome Rank(SimulationResult result, string title) => result.Ranks.Single(r => r.Rule.Title == title);

    [Fact]
    public void Steady_reaches_grandmaster_in_every_run_in_6_to_8_years_and_stays_above_85()
    {
        var result = Simulate(Steady);

        var grandmaster = Rank(result, "Grandmaster");
        Assert.Equal(20, grandmaster.RunsReached);
        Assert.InRange(grandmaster.Years!.P10, 6.0, 8.0);
        Assert.InRange(grandmaster.Years.P90, 6.0, 8.0);
        Assert.InRange(result.LowestLevel.P10, 85, 100);
        Assert.InRange(result.FinalLevel.P10, 90, 100);
        Assert.Equal(100, result.BestLevel.P10);

        // Short dips from Grandmaster to Legend, a few a year (§7.3).
        Assert.InRange(result.RankDropsPerYear.P50, 1.0, 7.0);
        Assert.InRange(result.AverageActiveShare, 0.94m, 0.99m);
        Assert.Equal(Start.AddYears(5), result.DropsFrom);
    }

    [Fact]
    public void The_multiplier_brings_steady_grandmaster_forward_by_more_than_a_year()
    {
        var with = Rank(Simulate(Steady), "Grandmaster").Years!.P50;
        var without = Rank(Simulate(Steady, TenYears with { Multiplier = false }), "Grandmaster").Years?.P50 ?? 10.0;

        Assert.True(without - with > 1.0, $"with ×: {with:0.0} y, without: {without:0.0} y");
    }

    [Fact]
    public void Human_reaches_champion_by_year_5_in_every_run_and_rarely_level_100()
    {
        var result = Simulate(Human);

        var champion = Rank(result, "Champion");
        Assert.Equal(20, champion.RunsReached);
        Assert.True(champion.Years!.P90 <= 5.0, $"Champion at {champion.Years.P90:0.0} years");
        Assert.InRange(Rank(result, "Grandmaster").RunsReached, 0, 10);
        Assert.InRange(result.LowestLevel.P50, 65, 85);
        Assert.InRange(result.AverageActiveShare, 0.85m, 0.92m);
    }

    [Fact]
    public void High_activity_reaches_level_100_in_about_5_years_and_never_drops_after()
    {
        var result = Simulate(High);

        var grandmaster = Rank(result, "Grandmaster");
        Assert.Equal(20, grandmaster.RunsReached);
        Assert.InRange(grandmaster.Years!.P50, 5.0, 5.7);
        Assert.Equal(new Spread<double>(0, 0, 0), result.RankDropsPerYear);
        Assert.Equal(new Spread<int>(100, 100, 100), result.LowestLevel);
    }

    [Fact]
    public void Six_months_off_lowers_the_level_and_it_is_back_within_a_year_of_returning()
    {
        var result = Simulate(Steady with { Lapse = SixMonths });

        var lapseStart = Start.AddDays(SixMonths.AfterDays);
        var lapseEnd = lapseStart.AddDays(SixMonths.LengthDays - 1);
        Assert.All(result.Runs, run =>
        {
            var before = run.StandingOn(lapseStart.AddDays(-1));
            Assert.True(before.Level >= 70, $"level {before.Level} before the lapse");

            // It falls well below where it was, but the best stays.
            var lowest = run.Standings.Where(s => s.Day >= lapseStart && s.Day <= lapseEnd.AddDays(365)).Min(s => s.Level);
            Assert.True(lowest < before.Level - 30, $"lowest {lowest} from {before.Level}");
            Assert.Equal(before.BestLevel, run.StandingOn(lapseEnd).BestLevel);

            // Back within a year of returning.
            var back = run.Standings.First(s => s.Day > lapseEnd && s.Level >= before.Level).Day;
            Assert.True(back.DayNumber - lapseEnd.DayNumber <= 365, $"back after {back.DayNumber - lapseEnd.DayNumber} days");

            // The multiplier's 30 days are all off at the return, and recover in a month.
            Assert.Equal(1.0m, run.MultiplierFor(lapseEnd.AddDays(1)));
            Assert.True(run.MultiplierFor(lapseEnd.AddDays(31)) >= 1.2m);
        });
    }

    [Fact]
    public void The_same_six_months_marked_as_exception_days_moves_nothing()
    {
        var result = Simulate(Steady with { Exceptions = [SixMonths] });

        var lapseStart = Start.AddDays(SixMonths.AfterDays);
        var lapseEnd = lapseStart.AddDays(SixMonths.LengthDays - 1);
        Assert.All(result.Runs, run =>
        {
            var before = run.StandingOn(lapseStart.AddDays(-1));
            Assert.All(
                run.Standings.Where(s => s.Day >= lapseStart && s.Day <= lapseEnd),
                s => Assert.Equal((before.Level, before.Rank, before.ConsistencyLevel, true), (s.Level, s.Rank, s.ConsistencyLevel, s.Exception)));
            Assert.Equal(run.MultiplierFor(lapseStart), run.MultiplierFor(lapseEnd.AddDays(1)));
            Assert.Equal(SixMonths.LengthDays, run.ExceptionDays.Count);
        });
    }

    [Fact]
    public void Below_the_bar_settles_at_the_level_its_average_supports_and_stays()
    {
        // A flat 38 base XP a day, 23 of it action: level 18 asks a 38 average, level 19 asks 39.
        var result = Simulate(new SimulationPattern(AverageXp: 38, SpreadXp: 0, OffShare: 0, HalfShare: 0));

        Assert.Equal((38, 39), (LifeDomains.HealthConsistency.Average(18), LifeDomains.HealthConsistency.Average(19)));
        Assert.Equal(new Spread<int>(18, 18, 18), result.FinalLevel);
        Assert.Equal(new Spread<int>(18, 18, 18), result.LowestLevel);
        Assert.Equal(20, Rank(result, "Apprentice").RunsReached);
        Assert.Equal(0, Rank(result, "Journeyman").RunsReached);
        Assert.All(result.Runs, run => Assert.Equal((18, 18), (run.ConsistencyLevel, run.BestLevel)));
    }

    [Fact]
    public void Level_100_then_nothing_falls_to_legend_on_day_10_champion_on_day_21_and_novice_on_day_249()
    {
        var history = Enumerable.Range(1, 100).Select(i => new DomainDay(Start.AddDays(-i), 3_000, 3_000)).ToList();
        var nothing = new SimulationPattern(AverageXp: 0, SpreadXp: 0, OffShare: 0, HalfShare: 0, SleepXp: 0);

        var run = Assert.Single(DomainSimulator.Run(Health, nothing, new SimulationOptions(Years: 1, Runs: 1), Start, history).Runs);

        DateOnly First(Func<DomainStanding, bool> test) => run.Standings.First(s => s.Day >= Start && test(s)).Day;
        Assert.Equal(100, run.StandingOn(Start.AddDays(-1)).Level);
        Assert.Equal(Start.AddDays(9), First(s => s.Rank <= 9));
        Assert.Equal(Start.AddDays(20), First(s => s.Rank <= 8));
        Assert.Equal(Start.AddDays(248), First(s => s.Rank == 0));
        Assert.Equal((100, 10), (run.BestLevel, run.BestRank));
    }

    [Fact]
    public void A_seed_gives_the_same_runs_and_another_seed_different_ones()
    {
        var options = new SimulationOptions(Years: 2, Runs: 3, Seed: 7);
        var pattern = new SimulationPattern();

        var once = Simulate(pattern, options);
        var again = Simulate(pattern, options);
        var other = Simulate(pattern, options with { Seed = 8 });

        Assert.Equal(once.Runs.Select(r => r.TotalXp), again.Runs.Select(r => r.TotalXp));
        Assert.NotEqual(once.Runs.Select(r => r.TotalXp), other.Runs.Select(r => r.TotalXp));
        Assert.Equal(Start.AddYears(2).AddDays(-1), once.End);
        Assert.Equal(Start.AddYears(1), once.DropsFrom);
    }

    [Fact]
    public void A_replay_keeps_the_real_days_as_paid_with_their_exception_days_and_projects_from_the_day_after()
    {
        // Two weeks of real days, one paid at 1.2× and one marked as an exception.
        var today = Start.AddDays(-1);
        var history = Enumerable.Range(0, 14)
            .Select(i => new DomainDay(today.AddDays(-i), i == 0 ? 400 : 300, i == 0 ? 480 : 300, 20))
            .ToList();

        var result = DomainSimulator.Run(Health, Steady, TenYears with { Runs = 3 }, Start, history, [today.AddDays(-5)]);

        Assert.Equal(14, result.ReplayedDays);
        Assert.All(result.Runs, run =>
        {
            Assert.Equal(today.AddDays(-13), run.Start);
            Assert.Equal(480, run.Periods.Single(p => p.Start == today).Paid);
            Assert.Equal(20, run.Periods.Single(p => p.Start == today).Passive);
            Assert.Contains(today.AddDays(-5), run.ExceptionDays);
            Assert.True(run.StandingOn(today.AddDays(-5)).Exception);
            Assert.True(run.RanksReached[0].QualifiedOn <= today);
        });
        Assert.Throws<ArgumentException>(() => DomainSimulator.Run(Health, Steady, TenYears, today, history));
    }

    [Fact]
    public void A_sampled_pattern_draws_full_days_from_the_real_ones_with_sleep_as_passive()
    {
        var pattern = new SimulationPattern(OffShare: 0, HalfShare: 0, Sample: [10, 80, 120], SleepXp: 15);

        var run = Assert.Single(Simulate(pattern, new SimulationOptions(Years: 1, Runs: 1, Multiplier: false)).Runs);

        Assert.All(run.Periods, p => Assert.Contains(p.Base, new long[] { 10, 80, 120 }));
        Assert.All(run.Periods, p => Assert.Equal(Math.Min(p.Base, 15), p.Passive));
        Assert.Contains(run.Periods, p => p.Base == 10 && p.Action == 0);
        Assert.Contains(run.Periods, p => p.Base == 120 && p.Action == 105);
    }

    [Fact]
    public void A_pattern_no_one_could_follow_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(OffShare: 0.7m, HalfShare: 0.4m)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(AverageXp: -1)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(SleepXp: -1)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(Lapse: new SimulationLapse(10, 0))));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(Exceptions: [new SimulationLapse(-1, 5)])));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(Sample: [])));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(), new SimulationOptions(Years: 0)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(), new SimulationOptions(Runs: 0)));
    }

    [Fact]
    public void A_pattern_reads_in_words()
    {
        var human = new SimulationPattern(AverageXp: 90, SpreadXp: 15, Essentials: 70, OffShare: 0.10m, HalfShare: 0.04m);
        var steady = new SimulationPattern(AverageXp: 95, SpreadXp: 15, Essentials: 70, OffShare: 0.05m, HalfShare: 0, SleepXp: 20);

        Assert.Equal(
            "full days about 90 base XP (spread 15, essentials 70), 10% of days off, 4% half days, up to 15 XP of a day sleep",
            human.Describe());
        Assert.Equal(
            "full days about 95 base XP (spread 15, essentials 70), 5% of days off, up to 20 XP of a day sleep, +5 a day per year, "
            + "182 days off after 1095 days, 30 exception days after 400 days",
            (steady with { GrowthPerYear = 5, Lapse = new SimulationLapse(1095, 182), Exceptions = [new SimulationLapse(400, 30)] }).Describe());
    }
}
