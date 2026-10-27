using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-9, D18 (environment plan §3, §4.9): the Environment simulator's regression scenarios —
/// seeded years of a home routine rated across Chris's nine spaces, scored by the real
/// <see cref="HomeScore"/> and walked by the real fold, multiplier included. Each lands in the
/// band §4.9 sets, so a retune that breaks how the home holds the level fails the build. The
/// bands were set from these C# runs; the plan's JS model left the multiplier off lifetime XP,
/// which only the top standard notices (level 100 about a year sooner with it).
/// </summary>
public sealed class HomeSimulatorTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);
    private static readonly DomainRules Environment = LifeDomains.Environment;
    private static readonly SimulationOptions SixYears = new(Years: 6, Runs: 8, Seed: 1);

    /// <summary>The plan's routine: a weekly walk-through, midweek checks of Office and Kitchen, settled at 4.5/4/3.5 on 25 XP a day.</summary>
    private static readonly HomeSimulationPattern Clean = new();

    private static SimulationResult Simulate(HomeSimulationPattern pattern, SimulationOptions? options = null)
        => HomeSimulator.Run(Environment, pattern, options ?? SixYears, Start);

    private static RankOutcome Rank(SimulationResult result, string title) => result.Ranks.Single(r => r.Rule.Title == title);

    /// <summary>Each run's shown level on the day <paramref name="years"/> in, sorted.</summary>
    private static List<int> LevelsAt(SimulationResult result, int years)
        => result.Runs.Select(r => r.StandingOn(Start.AddYears(years).AddDays(-1)).Level).Order().ToList();

    private static void InBand(Spread<int> spread, int low, int high)
    {
        Assert.InRange(spread.P10, low, high);
        Assert.InRange(spread.P90, low, high);
    }

    [Fact]
    public void Already_clean_holds_72_to_76()
    {
        var result = Simulate(Clean);

        InBand(result.FinalLevel, 72, 76);
        InBand(result.LowestLevel, 70, 76);
        Assert.Equal(0, Rank(result, "Champion").RunsReached);
        Assert.InRange(result.AverageActiveShare, 0.80m, 0.90m);
    }

    [Fact]
    public void Daily_checks_hold_89_to_94()
    {
        var result = Simulate(Clean with { Checks = HomeChecks.Daily, Tidiness = 4.75m, Cleanliness = 4.5m, Aesthetics = 4m, SteadyXp = 35 });

        InBand(result.FinalLevel, 89, 94);
        Assert.Equal(0, Rank(result, "Grandmaster").RunsReached);
    }

    [Fact]
    public void The_top_standard_reaches_level_100_in_3_to_4_years_or_4_to_5_without_the_multiplier()
    {
        var top = Clean with
        {
            WalkEvery = 3, Checks = HomeChecks.None, Tidiness = 4.75m, Cleanliness = 4.75m, Aesthetics = 4.5m, DecorEvery = 30,
            SteadyXp = 40
        };

        var grandmaster = Rank(Simulate(top), "Grandmaster");
        Assert.Equal(8, grandmaster.RunsReached);
        Assert.InRange(grandmaster.Years!.P10, 3.0, 4.0);
        Assert.InRange(grandmaster.Years.P90, 3.0, 4.0);

        var without = Rank(Simulate(top, SixYears with { Multiplier = false }), "Grandmaster");
        Assert.Equal(8, without.RunsReached);
        Assert.InRange(without.Years!.P10, 4.0, 5.0);
        Assert.InRange(without.Years.P90, 4.0, 5.0);
    }

    [Fact]
    public void Sunday_only_holds_65_to_70()
    {
        var result = Simulate(Clean with { Checks = HomeChecks.None, SteadyXp = 20 });

        InBand(result.FinalLevel, 65, 70);
    }

    [Fact]
    public void A_messy_weekly_home_holds_22_to_27()
    {
        var result = Simulate(Clean with { Checks = HomeChecks.None, Tidiness = 3m, Cleanliness = 3m, Aesthetics = 3m, SteadyXp = 15 });

        InBand(result.FinalLevel, 22, 27);
    }

    [Fact]
    public void Monthly_ratings_hold_21_to_26_however_clean()
    {
        var result = Simulate(Clean with { WalkEvery = 30, Checks = HomeChecks.None, SteadyXp = 20 });

        InBand(result.FinalLevel, 21, 26);
    }

    [Fact]
    public void A_three_month_catch_up_ends_within_2_of_the_clean_start()
    {
        var catchUp = Simulate(Clean with { CatchUpDays = 91, CatchUpXp = 80 });
        var clean = Simulate(Clean);

        // A year in, the catch-up's extra XP has it ahead of the clean start's lifetime level.
        var year1 = LevelsAt(catchUp, 1);
        Assert.InRange(year1[0], 50, 62);
        Assert.InRange(year1[^1], 50, 62);

        // By year 5 the home holds both the same.
        var (caughtUp, started) = (LevelsAt(catchUp, 5), LevelsAt(clean, 5));
        Assert.InRange(caughtUp[caughtUp.Count / 2] - started[started.Count / 2], -2, 2);
    }

    [Fact]
    public void A_marked_break_moves_nothing_during_the_span()
    {
        // 30 exception days four years in: no ratings, no XP, fades paused.
        var span = new SimulationLapse(AfterDays: 4 * 365, LengthDays: 30);
        var result = Simulate(Clean with { Exceptions = [span] }, SixYears with { Runs = 4 });

        var first = Start.AddDays(span.AfterDays);
        Assert.All(result.Runs, run =>
        {
            var before = run.StandingOn(first.AddDays(-1));
            var score = run.Periods.Single(p => p.Start == first.AddDays(-1)).HomeScore;
            for (var day = first; day < first.AddDays(span.LengthDays); day = day.AddDays(1))
            {
                var standing = run.StandingOn(day);
                Assert.Equal((before.Level, before.ShownExact, true, false), (standing.Level, standing.ShownExact, standing.Exception, standing.Counted));
                Assert.Equal(score, run.Periods.Single(p => p.Start == day).HomeScore);
            }
        });
    }

    [Fact]
    public void A_run_is_the_same_for_the_same_seed()
    {
        var options = new SimulationOptions(Years: 1, Runs: 2, Seed: 7);

        var (a, b) = (Simulate(Clean, options), Simulate(Clean, options));

        Assert.Equal(a.Runs.Select(r => r.TotalXp), b.Runs.Select(r => r.TotalXp));
        Assert.Equal(a.Runs.Select(r => r.Level), b.Runs.Select(r => r.Level));
        Assert.Same(Clean, a.Home);
    }

    [Fact]
    public void Only_a_domain_held_by_the_home_simulates_a_routine()
    {
        Assert.Throws<ArgumentException>(() => HomeSimulator.Run(LifeDomains.Health, Clean, SixYears, Start));
        Assert.Throws<ArgumentException>(() => Simulate(Clean with { WalkEvery = 0 }));
        Assert.Throws<ArgumentException>(() => Simulate(Clean with { Tidiness = 5.5m }));
        Assert.Throws<ArgumentException>(() => Simulate(Clean with { Spaces = [] }));
    }
}
