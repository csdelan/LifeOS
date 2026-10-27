using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-3 (food-log plan §2): the diet score and its drift test. The rules live in code, so
/// these checks keep a retune honest: the parts add up to the cap, the thresholds rise, and
/// the plan's example table still holds.
/// </summary>
public sealed class DietRulesTests
{
    private static readonly DietRules Rules = DietRules.Default;

    /// <summary>A day's facts from (calories, score) entries and a fast of <paramref name="fastHours"/>.</summary>
    private static DietDayFacts Day(double? fastHours, params (int Calories, decimal Score)[] entries)
        => new(
            entries.Sum(e => (long)e.Calories),
            entries.Sum(e => e.Calories * e.Score),
            fastHours is { } h ? (long)Math.Round(h * 3600) : null);

    [Fact]
    public void The_parts_add_up_to_the_cap_and_the_thresholds_rise()
    {
        Assert.Equal(Currencies.HealthXp, Rules.Currency);
        Assert.Equal(40, Rules.Cap);
        Assert.Equal(Rules.Cap, Rules.QualityMax + Rules.CaloriesMax + Rules.FastingMax);
        Assert.Equal(Rules.QualityMax + Rules.CaloriesMax, Rules.FoodMax);
        Assert.True(Rules.QualityMax > 0 && Rules.CaloriesMax > 0 && Rules.FastingMax > 0);
        Assert.True(0 < Rules.CalorieFloor, "the floor is above 0");
        Assert.True(Rules.CalorieFloor < Rules.CalorieFullTo, "the floor is below full credit");
        Assert.True(Rules.CalorieFullTo < Rules.CalorieZeroAt, "full credit ends before it fades out");
        Assert.True(0 < Rules.FastFromHours && Rules.FastFromHours < Rules.FastFullAtHours, "fasting starts paying before it's full");
        Assert.True(Currencies.Find(Rules.Currency)!.LifeDomain);
    }

    /// <summary>The plan's examples (§1): the parts unrounded, then the two awards.</summary>
    [Fact]
    public void The_example_table_holds()
    {
        // 3,000 calories of ice cream (all 1s), no fast.
        var iceCream = Rules.Score(Day(null, (3000, 1)));
        Assert.Equal((0m, 0m, 0m, 0L, 0L, 0L), (iceCream.Quality, iceCream.CaloriesPart, iceCream.Fasting, iceCream.FoodAward, iceCream.FastAward, iceCream.Total));

        // 1,200 calories of salmon salad (5) and a 23-hour fast: 28 + 4 + 7.3 → 32 + 7 = 39.
        var salmon = Rules.Score(Day(23, (1200, 5)));
        Assert.Equal(28m, salmon.Quality);
        Assert.Equal(4m, salmon.CaloriesPart);
        Assert.Equal(7.3m, Math.Round(salmon.Fasting, 1));
        Assert.Equal((32L, 7L, 39L), (salmon.FoodAward, salmon.FastAward, salmon.Total));
        Assert.Equal(5m, salmon.AverageScore);

        // A good day: 1,800 calories (700 at 5, 200 at 4, 900 at 4) and a 16-hour fast.
        var good = Rules.Score(Day(16, (700, 5), (200, 4), (900, 4)));
        Assert.Equal(23.7m, Math.Round(good.Quality, 1));
        Assert.Equal(4m, good.CaloriesPart);
        Assert.Equal(2.7m, Math.Round(good.Fasting, 1));
        Assert.Equal((28L, 3L, 31L), (good.FoodAward, good.FastAward, good.Total));

        // ... where one rounding of its total would give 30.
        Assert.Equal(30L, DietRules.Round(good.Quality + good.CaloriesPart + good.Fasting));

        // An ordinary day: 2,200 calories (400 at 4, 700 at 3, 900 at 2, 200 at 1) and a 14-hour fast.
        var ordinary = Rules.Score(Day(14, (400, 4), (700, 3), (900, 2), (200, 1)));
        Assert.Equal(11.1m, Math.Round(ordinary.Quality, 1));
        Assert.Equal(2.4m, ordinary.CaloriesPart);
        Assert.Equal(1.3m, Math.Round(ordinary.Fasting, 1));
        Assert.Equal((14L, 1L, 15L), (ordinary.FoodAward, ordinary.FastAward, ordinary.Total));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(499, 0)]
    [InlineData(500, 4)]
    [InlineData(1500, 4)]
    [InlineData(2000, 4)]
    [InlineData(2250, 2)]
    [InlineData(2499, 0.008)]
    [InlineData(2500, 0)]
    [InlineData(4000, 0)]
    public void Calories_pay_from_the_floor_then_fade_out(long calories, double part)
        => Assert.Equal((decimal)part, Rules.CaloriesPartFor(calories));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(18, 4)]
    [InlineData(24, 8)]
    [InlineData(36, 8)]
    public void Fasting_pays_from_12_hours_up_to_24(int hours, int part)
        => Assert.Equal(part, Rules.FastingFor(hours * 3600L));

    [Fact]
    public void Fasting_counts_by_the_second_and_its_award_rounds_halves_up()
    {
        // 12 h 45 m is 0.75 h over: 8 × 0.75 / 12 = 0.5, which rounds up to 1.
        var halfway = Rules.Score(new DietDayFacts(0, 0, (12 * 60 + 45) * 60L));
        Assert.Equal(0.5m, halfway.Fasting);
        Assert.Equal(1L, halfway.FastAward);

        Assert.True(Rules.FastingFor(12 * 3600L + 1) > 0m);
    }

    [Fact]
    public void A_day_without_calories_has_no_average_and_no_quality()
    {
        var coffeeOnly = Rules.Score(Day(null, (0, 5)));
        Assert.Null(coffeeOnly.AverageScore);
        Assert.Equal(0m, coffeeOnly.Quality);
        Assert.Equal(0L, coffeeOnly.Total);

        // A fast still pays on a day with no food.
        Assert.Equal(8L, Rules.Score(Day(24)).Total);
    }

    [Fact]
    public void Quality_is_a_straight_line_from_1_to_5_halves_included()
    {
        for (var score = FoodEntries.ScoreMin; score <= FoodEntries.ScoreMax; score += FoodEntries.ScoreStep)
        {
            Assert.Equal(7m * (score - 1), Rules.Score(Day(null, (1000, score))).Quality);
        }
    }

    /// <summary>Food-log plan §8: a half pays halfway between its neighbours, through the same line.</summary>
    [Fact]
    public void A_half_score_pays_halfway_and_mixes_into_the_average()
    {
        // 1,200 calories of turkey wraps at 3.5: 17.5 + 4 = 21.5, which rounds up to 22.
        var wraps = Rules.Score(Day(null, (1200, 3.5m)));
        Assert.Equal((17.5m, 4m, 22L, 3.5m), (wraps.Quality, wraps.CaloriesPart, wraps.FoodAward, wraps.AverageScore));

        // 600 at 3.5 and 600 at 4 average 3.75: 19.25 + 4 → 23.
        var mixed = Rules.Score(Day(null, (600, 3.5m), (600, 4)));
        Assert.Equal((3.75m, 19.25m, 23L), (mixed.AverageScore, mixed.Quality, mixed.FoodAward));

        // An odd number of calories leaves a half in score x calories: 3.5 x 333 = 1,165.5.
        Assert.Equal(1165.5m, Day(null, (333, 3.5m)).ScoreCalories);
        Assert.Equal(3.5m, Rules.Score(Day(null, (333, 3.5m))).AverageScore);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(3.5, true)]
    [InlineData(4.5, true)]
    [InlineData(5, true)]
    [InlineData(0.5, false)]
    [InlineData(0, false)]
    [InlineData(3.25, false)]
    [InlineData(3.7, false)]
    [InlineData(5.5, false)]
    [InlineData(6, false)]
    public void A_score_is_1_to_5_in_half_steps(double score, bool valid)
        => Assert.Equal(valid, FoodEntries.IsScore((decimal)score));

    [Fact]
    public void A_score_is_kept_and_shown_without_trailing_zeros()
    {
        Assert.Equal(("4", "3.5"), (FoodEntries.ScoreText(FoodEntries.TidyScore(4.0m)), FoodEntries.ScoreText(FoodEntries.TidyScore(3.50m))));
        Assert.Equal((0, 1), (FoodEntries.TidyScore(4.00m).Scale, FoodEntries.TidyScore(3.500m).Scale));
        Assert.Equal(1m, FoodEntries.DrinkScore);
    }

    [Theory]
    [InlineData(4, 0, "breakfast")]
    [InlineData(10, 59, "breakfast")]
    [InlineData(11, 0, "lunch")]
    [InlineData(15, 59, "lunch")]
    [InlineData(16, 0, "dinner")]
    [InlineData(21, 59, "dinner")]
    [InlineData(22, 0, "snack")]
    [InlineData(3, 59, "snack")]
    public void The_meal_type_is_guessed_from_the_time(int hour, int minute, string expected)
        => Assert.Equal(expected, FoodEntries.GuessMealType(new TimeOnly(hour, minute)));

    [Fact]
    public void Meal_types_are_found_in_any_case()
    {
        Assert.Equal(FoodEntries.Lunch, FoodEntries.FindMealType(" LUNCH "));
        Assert.Null(FoodEntries.FindMealType("brunch"));
        Assert.Null(FoodEntries.FindMealType(null));
    }

    /// <summary>The salmon day of the plan's §1 plus <paramref name="beers"/> beers of 150 calories (a 1 each).</summary>
    private static DietDayFacts SalmonDayWithBeers(int beers)
    {
        var plain = Day(23, (1200, 5));
        return plain with
        {
            Calories = plain.Calories + 150L * beers,
            ScoreCalories = plain.ScoreCalories + 150m * beers * FoodEntries.DrinkScore,
            Drinks = beers
        };
    }

    [Fact]
    public void The_drinks_cut_starts_at_the_first_and_leaves_nothing_at_ten()
    {
        Assert.Equal((0, 10), (Rules.DrinksFree, Rules.DrinkCutPercent));
        Assert.True(Rules.DrinksFree >= 0 && Rules.DrinkCutPercent is > 0 and <= 100);
        Assert.Equal(FoodEntries.ScoreMin, FoodEntries.DrinkScore);
        Assert.Equal(10, Rules.DrinksToNothing);
        Assert.Equal(1m, Rules.DrinkFactorFor(0));
        Assert.Equal(1m, Rules.DrinkFactorFor(Rules.DrinksFree));
        Assert.Equal(0.9m, Rules.DrinkFactorFor(Rules.DrinksFree + 1));
        Assert.Equal(0.1m, Rules.DrinkFactorFor(Rules.DrinksToNothing - 1));
        Assert.Equal(0m, Rules.DrinkFactorFor(Rules.DrinksToNothing));
        Assert.Equal(0m, Rules.DrinkFactorFor(40));
    }

    /// <summary>The plan's drinks table (§7.2): the salmon day with 0, 1, 2, 3, 4 and 10 beers.</summary>
    [Theory]
    [InlineData(0, 28.0, 4.0, 32, 7, 39)]
    [InlineData(1, 24.9, 4.0, 26, 7, 33)]
    [InlineData(2, 22.4, 4.0, 21, 6, 27)]
    [InlineData(3, 20.4, 4.0, 17, 5, 22)]
    [InlineData(4, 18.7, 4.0, 14, 4, 18)]
    [InlineData(10, 12.4, 0.0, 0, 0, 0)]
    public void The_drinks_table_holds(int beers, double quality, double calories, long food, long fast, long total)
    {
        var score = Rules.Score(SalmonDayWithBeers(beers));
        Assert.Equal((decimal)quality, Math.Round(score.Quality, 1));
        Assert.Equal((decimal)calories, score.CaloriesPart);
        Assert.Equal(7.3m, Math.Round(score.Fasting, 1));
        Assert.Equal((food, fast, total), (score.FoodAward, score.FastAward, score.Total));
        Assert.Equal(beers, score.Drinks);
        Assert.Equal(beers > Rules.DrinksFree, score.Cut);

        // Before the close the fast pays in full, whatever was drunk.
        Assert.Equal(7L, score.FastAwardUncut);
    }

    [Fact]
    public void Each_award_is_cut_before_it_is_rounded()
    {
        // A 23-hour fast is 7.33; one drink leaves 90% of it, 6.6, which rounds to 7. Cutting
        // the rounded 7 instead would give 6.3, so 6.
        var one = Rules.Score(Day(23) with { Drinks = 1 });
        Assert.Equal((7L, 7L), (one.FastAward, one.FastAwardUncut));

        // Two leave 80%: 5.87 → 6.
        Assert.Equal(6L, Rules.Score(Day(23) with { Drinks = 2 }).FastAward);
    }

    [Fact]
    public void A_streak_counts_completed_drink_free_days_from_the_last_drink_or_the_first_day()
    {
        var first = new DateOnly(2026, 10, 20);
        var later = first.AddDays(30); // a day well after the ones read, so each of them is over

        // Nothing before the Food Log's first day.
        Assert.Null(Sobriety.On(first.AddDays(-1), later, first, []));

        // No drink yet: every day from the first counts once it's over, the first day included;
        // today doesn't count until it's over.
        var none = Sobriety.On(first.AddDays(9), later, first, [])!;
        Assert.Equal((10, 10, first, (DateOnly?)null), (none.Days, none.BestDays, none.Since, none.LastDrinkOn));
        var noneToday = Sobriety.On(first.AddDays(9), first.AddDays(9), first, [])!;
        Assert.Equal((9, 9), (noneToday.Days, noneToday.BestDays));
        var firstToday = Sobriety.On(first, first, first, [])!;
        Assert.Equal((0, 0, false), (firstToday.Days, firstToday.BestDays, firstToday.DrinkThatDay));

        // Drinks on the 3rd and the 10th day. Seen on the 15th: 4 done since the last (the
        // 15th isn't over), best 6 between them; the 15th seen later has 5.
        var drinks = new[] { first.AddDays(2), first.AddDays(9) };
        var today = Sobriety.On(first.AddDays(14), first.AddDays(14), first, drinks)!;
        Assert.Equal((4, 6, first.AddDays(10), (DateOnly?)first.AddDays(9)), (today.Days, today.BestDays, today.Since, today.LastDrinkOn));
        Assert.Equal(5, Sobriety.On(first.AddDays(14), later, first, drinks)!.Days);

        // The day after a drink, seen that day: 0, with no drink on it; it's 1 the day after.
        var dayAfter = Sobriety.On(first.AddDays(10), first.AddDays(10), first, drinks)!;
        Assert.Equal((0, 6, first.AddDays(10), false), (dayAfter.Days, dayAfter.BestDays, dayAfter.Since, dayAfter.DrinkThatDay));
        Assert.Equal(1, Sobriety.On(first.AddDays(11), first.AddDays(11), first, drinks)!.Days);

        // A day with a drink has a streak of 0 at once, today or later; a drink after it doesn't count.
        var drinkDay = Sobriety.On(first.AddDays(9), later, first, drinks)!;
        Assert.Equal((0, 6, true), (drinkDay.Days, drinkDay.BestDays, drinkDay.DrinkThatDay));
        Assert.Equal(0, Sobriety.On(first.AddDays(9), first.AddDays(9), first, drinks)!.Days);
        var before = Sobriety.On(first.AddDays(5), later, first, drinks)!;
        Assert.Equal((3, 3, (DateOnly?)first.AddDays(2)), (before.Days, before.BestDays, before.LastDrinkOn));

        // A drink on the first day.
        var onFirst = Sobriety.On(first, later, first, [first])!;
        Assert.Equal((0, 0), (onFirst.Days, onFirst.BestDays));
    }

    [Fact]
    public void A_drinks_tally_counts_drinks_days_and_the_most_in_one_day()
    {
        var (from, to) = (DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(30));
        var day = new DateOnly(2026, 10, 1);
        var tally = DrinkTally.Of(from, to, [(day, 2), (day.AddDays(3), 4), (day.AddDays(5), 4), (day.AddDays(6), 1)]);
        Assert.Equal((11, 4, 4, (DateOnly?)day.AddDays(3)), (tally.Drinks, tally.DrinkDays, tally.MostInADay, tally.MostOn));

        var empty = DrinkTally.Of(from, to, []);
        Assert.Equal((0, 0, 0, (DateOnly?)null), (empty.Drinks, empty.DrinkDays, empty.MostInADay, empty.MostOn));
    }

    [Fact]
    public void Times_and_lengths_read_in_words()
    {
        Assert.Equal("Mon Oct 19, 8:00 pm", FoodText.When(new DateTime(2026, 10, 19, 20, 0, 0)));
        Assert.Equal("Tue Oct 20, 12:30 pm", FoodText.When(new DateTime(2026, 10, 20, 12, 30, 0)));
        Assert.Equal("7:05 am", FoodText.Clock(new DateTime(2026, 10, 20, 7, 5, 0)));
        Assert.Equal("12:00 am", FoodText.Clock(new DateTime(2026, 10, 20, 0, 0, 0)));
        Assert.Equal("45 m", FoodText.Duration(45 * 60 + 59));
        Assert.Equal("23 h 05 m", FoodText.Duration((23 * 60 + 5) * 60));
    }
}
