using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 (xp-domains plan X4, §3.10): the simulator's regression scenarios —
/// ten seeded years of realistic earning under Health's rules, multiplier included, each with
/// the band it must land in, so a retune that breaks progression fails the build. They test
/// the spread of realistic outcomes, not just whether perfect adherence wins.
/// </summary>
public sealed class DomainSimulatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 21);
    private static readonly DomainRules Health = LifeDomains.Health;
    private static readonly SimulationOptions TenYears = new(Years: 10, Runs: 20, Seed: 1);

    // Full days do the 70 XP of essentials plus extras that vary.
    private static readonly SimulationPattern Steady = new(AverageXp: 95, SpreadXp: 15, Essentials: 70, OffShare: 0.05m, HalfShare: 0);
    private static readonly SimulationPattern Human = new(AverageXp: 90, SpreadXp: 15, Essentials: 70, OffShare: 0.10m, HalfShare: 0.04m);

    private static SimulationResult Simulate(SimulationPattern pattern, SimulationOptions? options = null)
        => DomainSimulator.Run(Health, pattern, options ?? TenYears, Start);

    private static RankOutcome Rank(SimulationResult result, string title) => result.Ranks.Single(r => r.Rule.Title == title);

    /// <summary>When each run reached <paramref name="title"/>, in years since its start.</summary>
    private static List<double> YearsTo(SimulationResult result, string title)
        => result.Runs
            .Select(r => r.RanksReached.FirstOrDefault(x => x.Rule.Title == title) is { } reached
                ? (reached.QualifiedOn.DayNumber - r.Start!.Value.DayNumber) / 365.25
                : (double?)null)
            .OfType<double>()
            .ToList();

    [Fact]
    public void Steady_reaches_grandmaster_in_every_run_in_5_to_8_years()
    {
        var result = Simulate(Steady);

        var grandmaster = Rank(result, "Grandmaster");
        Assert.Equal(20, grandmaster.RunsReached);
        Assert.InRange(grandmaster.Years!.P50, 5.0, 8.0);
        Assert.All(result.Runs, r => Assert.Equal((100, 10), (r.Level, r.Rank)));
    }

    [Fact]
    public void The_multiplier_brings_steady_grandmaster_forward_by_more_than_a_year()
    {
        var with = Rank(Simulate(Steady), "Grandmaster").Years!.P50;
        var without = Rank(Simulate(Steady, TenYears with { Multiplier = false }), "Grandmaster").Years!.P50;

        Assert.True(without - with > 1.0, $"with ×: {with:0.0} y, without: {without:0.0} y");
    }

    [Fact]
    public void Human_nearly_always_reaches_legend_and_grandmaster_in_under_half_the_runs_never_early()
    {
        // 10% of days off and some half days: about 87% of days succeed, against Legend's 88%
        // and Grandmaster's 90% bars over a year.
        var result = Simulate(Human);

        Assert.InRange(Rank(result, "Champion").RunsReached, 20, 20);
        Assert.InRange(Rank(result, "Legend").RunsReached, 18, 20);
        Assert.InRange(Rank(result, "Grandmaster").RunsReached, 1, 9);
        Assert.All(YearsTo(result, "Grandmaster"), years => Assert.True(years >= 6.5, $"Grandmaster after {years:0.0} years"));
        Assert.InRange(result.AverageConsistency, 0.85m, 0.89m);
    }

    [Fact]
    public void Improving_reaches_grandmaster_within_10_years()
    {
        // Human, with the extras growing by 5 XP a day each year: half days start to succeed.
        var result = Simulate(Human with { GrowthPerYear = 5 });

        Assert.Equal(20, Rank(result, "Grandmaster").RunsReached);
        Assert.All(YearsTo(result, "Grandmaster"), years => Assert.True(years < 10));
    }

    [Fact]
    public void Six_months_off_keeps_level_and_rank_and_consistency_recovers_on_return()
    {
        var lapse = new SimulationLapse(AfterDays: 3 * 365, LengthDays: 182);
        var result = Simulate(Steady with { Lapse = lapse });

        var lapseStart = Start.AddDays(lapse.AfterDays);
        var lapseEnd = lapseStart.AddDays(lapse.LengthDays - 1);
        Assert.All(result.Runs, run =>
        {
            var before = run.StandingOn(lapseStart.AddDays(-1));
            var after = run.StandingOn(lapseEnd);
            Assert.Equal((before.TotalXp, before.Level, before.Rank), (after.TotalXp, after.Level, after.Rank));
            Assert.True(before.Rank >= 7, $"rank {before.Rank} before the lapse");

            // The last 30 complete days are all off: 0% and ×1.0.
            Assert.Equal(0, run.WindowBefore(lapseEnd.AddDays(1), 30).SuccessfulDays);
            Assert.Equal(1.0m, run.MultiplierFor(lapseEnd.AddDays(1)));

            // Back for a month, both recover.
            var back = lapseEnd.AddDays(31);
            Assert.True(run.WindowBefore(back, 30).Share >= 0.7m);
            Assert.True(run.MultiplierFor(back) >= 1.2m);
            Assert.Equal(10, run.Rank);
        });
    }

    [Fact]
    public void Always_below_the_minimum_stays_a_level_9_novice_with_the_rest_banked()
    {
        var result = Simulate(new SimulationPattern(AverageXp: 60, SpreadXp: 0, OffShare: 0, HalfShare: 0));

        Assert.All(result.Runs, run =>
        {
            Assert.Equal((9, 0), (run.Level, run.Rank));
            Assert.True(run.RawLevel > 50);
            Assert.Equal(run.TotalXp - Health.Curve.TotalFor(10), run.BankedXp);
            Assert.Equal(60 * run.Periods.Count, run.TotalXp); // never multiplied
        });
        Assert.All(result.Ranks, r => Assert.Equal(0, r.RunsReached));
        Assert.Equal(0m, result.AverageConsistency);
    }

    [Fact]
    public void Exactly_70_a_day_reaches_each_rank_the_day_its_level_arrives()
    {
        var result = Simulate(new SimulationPattern(AverageXp: 70, SpreadXp: 0, OffShare: 0, HalfShare: 0), TenYears with { Runs = 1 });

        var run = Assert.Single(result.Runs);
        Assert.Equal(10, run.RanksReached.Count);
        foreach (var reached in run.RanksReached)
        {
            var levelArrives = run.Standings.First(s => s.RawLevel >= reached.Rule.StartLevel).Day;
            Assert.Equal(levelArrives, reached.QualifiedOn);
        }

        Assert.InRange(Rank(result, "Grandmaster").Years!.P50, 7.0, 8.5);
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
    }

    [Fact]
    public void A_replay_keeps_the_real_days_as_paid_and_projects_from_the_day_after()
    {
        // Two weeks of real days, already Apprentice-ready, with one paid at 1.2×.
        var today = Start.AddDays(-1);
        var history = Enumerable.Range(0, 14)
            .Select(i => new DomainDay(today.AddDays(-i), i == 0 ? 400 : 300, i == 0 ? 480 : 300))
            .ToList();

        var result = DomainSimulator.Run(Health, Steady, TenYears with { Runs = 3 }, Start, history);

        Assert.Equal(14, result.ReplayedDays);
        Assert.All(result.Runs, run =>
        {
            Assert.Equal(today.AddDays(-13), run.Start);
            Assert.Equal(480, run.Periods.Single(p => p.Start == today).Paid);
            Assert.Equal(today, run.RanksReached[0].QualifiedOn);
        });
        Assert.Throws<ArgumentException>(() => DomainSimulator.Run(Health, Steady, TenYears, today, history));
    }

    [Fact]
    public void A_sampled_pattern_draws_full_days_from_the_real_ones()
    {
        var pattern = new SimulationPattern(OffShare: 0, HalfShare: 0, Sample: [80, 120]);

        var run = Assert.Single(Simulate(pattern, new SimulationOptions(Years: 1, Runs: 1, Multiplier: false)).Runs);

        Assert.All(run.Periods, p => Assert.Contains(p.Base, new long[] { 80, 120 }));
        Assert.Contains(run.Periods, p => p.Base == 80);
        Assert.Contains(run.Periods, p => p.Base == 120);
    }

    [Theory]
    [InlineData(25, "1.1", 28)]
    [InlineData(25, "1.3", 33)]
    [InlineData(10, "1.25", 13)]
    [InlineData(70, "1.0", 70)]
    public void A_day_is_paid_at_its_multiplier_rounding_halves_up(long baseXp, string multiplier, long paid)
        => Assert.Equal(paid, DomainSimulator.Price(baseXp, decimal.Parse(multiplier, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_pattern_no_one_could_follow_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(OffShare: 0.7m, HalfShare: 0.4m)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(AverageXp: -1)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(Lapse: new SimulationLapse(10, 0))));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(Sample: [])));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(), new SimulationOptions(Years: 0)));
        Assert.Throws<ArgumentException>(() => Simulate(new SimulationPattern(), new SimulationOptions(Runs: 0)));
    }

    [Fact]
    public void A_pattern_reads_in_words()
    {
        Assert.Equal(
            "full days about 90 base XP (spread 15, essentials 70), 10% of days off, 4% half days",
            Human.Describe());
        Assert.Equal(
            "full days about 95 base XP (spread 15, essentials 70), 5% of days off, +5 a day per year, 182 days off after 1095 days",
            (Steady with { GrowthPerYear = 5, Lapse = new SimulationLapse(1095, 182) }).Describe());
    }
}
