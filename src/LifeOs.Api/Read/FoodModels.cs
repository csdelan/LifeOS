using System.Globalization;
using LifeOs.Application.Food;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/food/day</c> (FOOD-1..4, food-log plan §3.6): one home-zone day of the Food Log.
/// Its entries come oldest first (FOOD-7); its fasts are those that ended on it.
/// <see cref="Running"/> is the fast running now, whatever the day. <see cref="LastMeal"/> is
/// the newest entry with calories, whose time Begin fast offers. <see cref="Sobriety"/> is the
/// sobriety streak as of the day (FOOD-6), null before the Food Log's first day.
/// </summary>
public sealed class FoodDayRead
{
    public string Day { get; set; } = "";
    public string Today { get; set; } = "";
    public IReadOnlyList<MealRow> Meals { get; set; } = [];
    public IReadOnlyList<FastRow> Fasts { get; set; } = [];
    public FastRow? Running { get; set; }
    public MealRow? LastMeal { get; set; }
    public FoodDaySummaryRow Summary { get; set; } = new();
    public SobrietyRow? Sobriety { get; set; }
}

/// <summary>
/// <c>GET /api/food/days?from&amp;to</c>: every day of the range, oldest first, at most 62 —
/// what the Week and Month views total. <see cref="Sobriety"/> is the streak as of the range's
/// last day so far (today, when the range holds it), null when that's before the Food Log's
/// first day.
/// </summary>
public sealed class FoodDaysRead
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Today { get; set; } = "";
    public IReadOnlyList<FoodDaySummaryRow> Days { get; set; } = [];
    public SobrietyRow? Sobriety { get; set; }
}

/// <summary>
/// A sobriety streak (FOOD-6, food-log plan §7.3) as of <see cref="AsOf"/>: <see cref="Days"/>
/// completed drink-free days in a row, counted from <see cref="Since"/> to the day itself once
/// it's over, or to yesterday while it's today (0 when it has a drink); the best run since the
/// Food Log's <see cref="FirstDay"/>; and the last day with a drink, if any. A day with nothing
/// logged counts as drink-free.
/// </summary>
public sealed class SobrietyRow
{
    public string AsOf { get; set; } = "";
    public int Days { get; set; }
    public int BestDays { get; set; }
    public string Since { get; set; } = "";
    public string? LastDrinkOn { get; set; }
    public string FirstDay { get; set; } = "";
}

/// <summary>
/// <c>GET /api/food/drinks?from&amp;to</c> (FOOD-6): the drinks drunk from <see cref="From"/> up to
/// (not including) <see cref="To"/>, on how many home-zone days, and the most in one day
/// (<see cref="MostOn"/>, the first such day).
/// </summary>
public sealed class FoodDrinksRead
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int Drinks { get; set; }
    public int DrinkDays { get; set; }
    public int MostInADay { get; set; }
    public string? MostOn { get; set; }
}

/// <summary>
/// A food entry (FOOD-1): its score (1–5 in half steps), calories and the optional nutrients
/// in whole grams.
/// <see cref="EatenAt"/> is an instant; <see cref="EatenOn"/> the home-zone day it counts on.
/// <see cref="DuringFast"/> names the recorded fast it was eaten inside, if any (a day read
/// only), so the page can offer to end that fast there. <see cref="Alcoholic"/> marks one
/// alcoholic drink (FOOD-6), whose score is always 1.
/// </summary>
public sealed class MealRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>1 to 5 in whole or half points (3.5).</summary>
    public decimal Score { get; set; }
    public int Calories { get; set; }
    public bool Alcoholic { get; set; }
    public int? ProteinG { get; set; }
    public int? CarbsG { get; set; }
    public int? FatG { get; set; }
    /// <summary><c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c> or <c>drink</c>.</summary>
    public string MealType { get; set; } = "";
    public DateTimeOffset EatenAt { get; set; }
    public string EatenOn { get; set; } = "";
    public Guid? PhotoArtifactId { get; set; }
    public Guid? DuringFast { get; set; }
    public DateTimeOffset FirstLoggedAt { get; set; }
    public DateTimeOffset LoggedAt { get; set; }
    /// <summary>How many versions the entry has: 1 until it's edited.</summary>
    public int Versions { get; set; }
}

/// <summary>
/// A fast (FOOD-2). While it runs, <see cref="EndedAt"/>, <see cref="EndedOn"/> and
/// <see cref="Seconds"/> are null and the page counts from <see cref="StartedAt"/>.
/// <see cref="MealsDuring"/> counts live entries with calories eaten inside it.
/// </summary>
public sealed class FastRow
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string StartedOn { get; set; } = "";
    public string? EndedOn { get; set; }
    public bool Running { get; set; }
    public long? Seconds { get; set; }
    public int MealsDuring { get; set; }
}

/// <summary>
/// A day's diet (FOOD-3, plan §2): its facts, the three parts of its score, the two awards it
/// projects in base Health XP (<see cref="FoodAward"/> once it's closed, <see cref="FastAward"/>
/// once its fast has ended), and what the ledger pays it now.
/// <para>
/// <see cref="AverageScore"/> is the calorie-weighted score <c>S</c> (null with no calories).
/// <see cref="Closed"/> is true once the day is closed by hand (<see cref="ClosedByHand"/>,
/// <see cref="ClosedAt"/>) or is over.
/// </para>
/// <para>
/// <see cref="Drinks"/> counts the day's alcoholic drinks (FOOD-6); <see cref="DrinkFactor"/>
/// is what they leave of its diet XP (1 up to two drinks, 0.9 for three, … 0 from twelve). Both
/// awards are shown cut, as they pay once the day is closed; before the close a fast is paid
/// in full.
/// </para>
/// </summary>
public sealed class FoodDaySummaryRow
{
    public string Day { get; set; } = "";
    public int Meals { get; set; }
    public long Calories { get; set; }
    public decimal? AverageScore { get; set; }
    public long? LongestFastSeconds { get; set; }
    public bool Closed { get; set; }
    public bool ClosedByHand { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Food quality, 0–28.</summary>
    public decimal Quality { get; set; }
    /// <summary>The calorie part, 0–4.</summary>
    public decimal CaloriesPart { get; set; }
    /// <summary>Fasting, 0–8.</summary>
    public decimal Fasting { get; set; }
    public long FoodAward { get; set; }
    public long FastAward { get; set; }
    /// <summary>The food and fast awards together, at most <see cref="Cap"/>.</summary>
    public long Total { get; set; }
    public int Cap { get; set; }
    public int Drinks { get; set; }
    public decimal DrinkFactor { get; set; } = 1m;
    public FoodPaidRow Paid { get; set; } = new();
}

/// <summary>
/// What the ledger pays a food day: its fast and its food (each null while unpaid), and their
/// totals before (<see cref="BaseTotal"/>) and after (<see cref="Total"/>) the multiplier.
/// </summary>
public sealed class FoodPaidRow
{
    public FoodAwardPaidRow? Fast { get; set; }
    public FoodAwardPaidRow? Diet { get; set; }
    public long Total { get; set; }
    public long BaseTotal { get; set; }
}

/// <summary>One paid food award: its base, what it paid, and the multiplier (null at 1.0).</summary>
public sealed class FoodAwardPaidRow
{
    public long Base { get; set; }
    public long Paid { get; set; }
    public decimal? Multiplier { get; set; }
}

/// <summary>
/// The response of a food write (<c>POST /api/food/meals</c>, <c>/fasts</c>, <c>/close</c>):
/// what it did (<c>logged</c>, <c>edited</c>, <c>deleted</c>, <c>fast_begun</c>,
/// <c>fast_ended</c>, <c>fast_recorded</c>, <c>fast_edited</c>, <c>fast_deleted</c>,
/// <c>closed</c>, <c>already_closed</c>), the entry or fast as it now stands (as it stood, for
/// a delete), the day it closed, the days it touched, and what settling the food days paid.
/// </summary>
public sealed class FoodWriteResultRow
{
    public string Action { get; set; } = "";
    public MealRow? Meal { get; set; }
    public FastRow? Fast { get; set; }
    public string? Day { get; set; }
    public IReadOnlyList<string> Days { get; set; } = [];
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
}

/// <summary>Maps the Application's Food Log records onto the API's shapes.</summary>
public static class FoodRows
{
    public static FoodDayRead From(FoodDayView view, DietRules rules) => new()
    {
        Day = Day(view.Day),
        Today = Day(view.Today),
        Meals = view.Meals.Select(m => From(m, view.EatenDuringFast.TryGetValue(m.Id, out var fast) ? fast : null)).ToList(),
        Fasts = view.Fasts.Select(From).ToList(),
        Running = view.Running is { } running ? From(running) : null,
        LastMeal = view.LastMeal is { } last ? From(last, null) : null,
        Summary = From(new FoodDaySummary(view.Facts, view.Score, view.Paid), rules),
        Sobriety = From(view.Sobriety)
    };

    public static FoodDaysRead From(FoodRange range, DietRules rules) => new()
    {
        From = Day(range.From),
        To = Day(range.To),
        Today = Day(range.Today),
        Days = range.Days.Select(d => From(d, rules)).ToList(),
        Sobriety = From(range.Sobriety)
    };

    public static SobrietyRow? From(SobrietyStreak? streak) => streak is null
        ? null
        : new SobrietyRow
        {
            AsOf = Day(streak.AsOf),
            Days = streak.Days,
            BestDays = streak.BestDays,
            Since = Day(streak.Since),
            LastDrinkOn = streak.LastDrinkOn is { } last ? Day(last) : null,
            FirstDay = Day(streak.FirstDay)
        };

    public static FoodDrinksRead From(DrinkTally tally) => new()
    {
        From = tally.From,
        To = tally.To,
        Drinks = tally.Drinks,
        DrinkDays = tally.DrinkDays,
        MostInADay = tally.MostInADay,
        MostOn = tally.MostOn is { } most ? Day(most) : null
    };

    public static MealRow From(Meal meal, Guid? duringFast = null) => new()
    {
        Id = meal.Id,
        Name = meal.Name,
        Score = meal.Score,
        Calories = meal.Calories,
        Alcoholic = meal.Alcoholic,
        ProteinG = meal.ProteinG,
        CarbsG = meal.CarbsG,
        FatG = meal.FatG,
        MealType = meal.MealType,
        EatenAt = meal.EatenAt,
        EatenOn = Day(meal.EatenOn),
        PhotoArtifactId = meal.PhotoArtifactId,
        DuringFast = duringFast,
        FirstLoggedAt = meal.FirstLoggedAt,
        LoggedAt = meal.LoggedAt,
        Versions = meal.Versions
    };

    public static FastRow From(Fast fast) => new()
    {
        Id = fast.Id,
        StartedAt = fast.StartedAt,
        EndedAt = fast.EndedAt,
        StartedOn = Day(fast.StartedOn),
        EndedOn = fast.EndedOn is { } on ? Day(on) : null,
        Running = fast.Running,
        Seconds = fast.Seconds,
        MealsDuring = fast.MealsDuring
    };

    public static FoodDaySummaryRow From(FoodDaySummary day, DietRules rules)
    {
        var (facts, score, paid) = (day.Facts, day.Score, day.Paid);
        return new FoodDaySummaryRow
        {
            Day = Day(facts.Day),
            Meals = facts.Meals,
            Calories = facts.Calories,
            AverageScore = score.AverageScore is { } s ? Math.Round(s, 2) : null,
            LongestFastSeconds = facts.LongestFastSeconds,
            Closed = facts.Closed,
            ClosedByHand = facts.ClosedByHand,
            ClosedAt = facts.ClosedAt,
            Quality = Math.Round(score.Quality, 2),
            CaloriesPart = Math.Round(score.CaloriesPart, 2),
            Fasting = Math.Round(score.Fasting, 2),
            FoodAward = score.FoodAward,
            FastAward = score.FastAward,
            Total = score.Total,
            Cap = rules.Cap,
            Drinks = score.Drinks,
            DrinkFactor = score.DrinkFactor,
            Paid = new FoodPaidRow
            {
                Fast = Paid(paid.Fast),
                Diet = Paid(paid.Diet),
                Total = paid.Total,
                BaseTotal = paid.BaseTotal
            }
        };
    }

    public static FoodWriteResultRow From(FoodWriteResult result) => new()
    {
        Action = result.Action,
        Meal = result.Meal is { } meal ? From(meal) : null,
        Fast = result.Fast is { } fast ? From(fast) : null,
        Day = result.Day is { } day ? Day(day) : null,
        Days = result.Days.Select(Day).ToList(),
        Awards = result.Earned.Awards.Select(RewardRows.From).ToList(),
        Progress = result.Earned.Progress.Select(RewardRows.From).ToList()
    };

    private static FoodAwardPaidRow? Paid(FoodPaid? paid)
        => paid is null ? null : new FoodAwardPaidRow { Base = paid.Base, Paid = paid.Paid, Multiplier = paid.Multiplier };

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
