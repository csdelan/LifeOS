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
    private static DietDayFacts Day(double? fastHours, params (int Calories, int Score)[] entries)
        => new(
            entries.Sum(e => (long)e.Calories),
            entries.Sum(e => (long)e.Calories * e.Score),
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
    public void Quality_is_a_straight_line_from_1_to_5()
    {
        for (var score = 1; score <= 5; score++)
        {
            Assert.Equal(7m * (score - 1), Rules.Score(Day(null, (1000, score))).Quality);
        }
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
