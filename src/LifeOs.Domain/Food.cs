namespace LifeOs.Domain;

/// <summary>
/// What a food entry can be (food-log plan §1): the meal types, with a guess from the time it
/// was eaten when none is given, and the limits every entry is checked against.
/// </summary>
public static class FoodEntries
{
    public const string Breakfast = "breakfast";
    public const string Lunch = "lunch";
    public const string Dinner = "dinner";
    public const string Snack = "snack";
    public const string Drink = "drink";

    /// <summary>Every meal type, in the order a picker shows them.</summary>
    public static readonly IReadOnlyList<string> MealTypes = [Breakfast, Lunch, Dinner, Snack, Drink];

    public const int NameMaxLength = 80;
    public const int ScoreMin = 1;
    public const int ScoreMax = 5;
    public const int CaloriesMax = 5000;
    public const int GramsMax = 1000;

    /// <summary>An alcoholic drink's score, always (FOOD-6): the lowest.</summary>
    public const int DrinkScore = ScoreMin;

    /// <summary>
    /// The meal type a home-zone time suggests: 04:00–10:59 breakfast, 11:00–15:59 lunch,
    /// 16:00–21:59 dinner, otherwise a snack.
    /// </summary>
    public static string GuessMealType(TimeOnly localTime) => localTime.Hour switch
    {
        >= 4 and < 11 => Breakfast,
        >= 11 and < 16 => Lunch,
        >= 16 and < 22 => Dinner,
        _ => Snack
    };

    /// <summary>The known meal type <paramref name="value"/> names (any case), or null.</summary>
    public static string? FindMealType(string? value)
        => MealTypes.FirstOrDefault(t => string.Equals(t, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One home-zone day as the diet score reads it (food-log plan §2.1, §7.2): its total calories,
/// the sum of score × calories over its live entries, the longest fast that ended on it, and
/// how many of its entries are alcoholic drinks.
/// </summary>
public sealed record DietDayFacts(long Calories, long ScoreCalories, long? LongestFastSeconds, int Drinks = 0);

/// <summary>
/// A day's diet score (food-log plan §2.1, §7.2). The three parts are unrounded, for showing
/// the working; <see cref="FoodAward"/> (quality + calories) and <see cref="FastAward"/> are
/// each cut by <see cref="DrinkFactor"/> and rounded to whole base Health XP, halves up: what
/// the ledger pays before the multiplier once the day is closed. <see cref="FastAwardUncut"/>
/// is what the fast pays before the close, when no drink cuts it yet.
/// <see cref="AverageScore"/> is the calorie-weighted average, or null when the day has no
/// calories.
/// </summary>
public sealed record DietScore(
    decimal Quality,
    decimal CaloriesPart,
    decimal Fasting,
    long FoodAward,
    long FastAward,
    decimal? AverageScore,
    int Drinks,
    decimal DrinkFactor,
    long FastAwardUncut)
{
    /// <summary>The day's diet XP before the multiplier, once it's closed: both awards.</summary>
    public long Total => FoodAward + FastAward;

    /// <summary>Whether the day's drinks cut its diet XP.</summary>
    public bool Cut => DrinkFactor < 1m;
}

/// <summary>
/// The diet rules (food-log plan §2): up to <see cref="Cap"/> base Health XP a day, from food
/// quality, calories and fasting. Tunable numbers kept in code like <see cref="LifeDomains"/>; a
/// drift test keeps them consistent. A retune never rewrites a past day: only a change to a
/// day's entries re-scores it.
/// </summary>
public sealed record DietRules(
    string Currency,
    int QualityMax,
    int CaloriesMax,
    int FastingMax,
    int CalorieFloor,
    int CalorieFullTo,
    int CalorieZeroAt,
    int FastFromHours,
    int FastFullAtHours,
    int DrinksFree,
    int DrinkCutPercent)
{
    /// <summary>
    /// Chris's first pass (2026-10-20): 28 quality + 4 calories + 8 fasting = 40; and
    /// (2026-10-22) 10% off the day for each drink above two.
    /// </summary>
    public static readonly DietRules Default = new(
        Currency: Currencies.HealthXp,
        QualityMax: 28,
        CaloriesMax: 4,
        FastingMax: 8,
        CalorieFloor: 500,
        CalorieFullTo: 2000,
        CalorieZeroAt: 2500,
        FastFromHours: 12,
        FastFullAtHours: 24,
        DrinksFree: 2,
        DrinkCutPercent: 10);

    /// <summary>The most base Health XP a day's diet can pay.</summary>
    public int Cap => QualityMax + CaloriesMax + FastingMax;

    /// <summary>The most the food award (quality + calories) can pay.</summary>
    public int FoodMax => QualityMax + CaloriesMax;

    /// <summary>The fewest drinks that leave a day's diet XP at nothing: 12.</summary>
    public int DrinksToNothing => DrinksFree + (100 + DrinkCutPercent - 1) / DrinkCutPercent;

    /// <summary>Scores one day (food-log plan §2.1, §7.2).</summary>
    public DietScore Score(DietDayFacts facts)
    {
        decimal? average = facts.Calories > 0 ? (decimal)facts.ScoreCalories / facts.Calories : null;
        var quality = average is { } s
            ? QualityMax * Math.Clamp(s - FoodEntries.ScoreMin, 0m, FoodEntries.ScoreMax - FoodEntries.ScoreMin)
              / (FoodEntries.ScoreMax - FoodEntries.ScoreMin)
            : 0m;
        var calories = CaloriesPartFor(facts.Calories);
        var fasting = FastingFor(facts.LongestFastSeconds ?? 0);
        var factor = DrinkFactorFor(facts.Drinks);
        return new DietScore(
            quality, calories, fasting, Round((quality + calories) * factor), Round(fasting * factor), average,
            Math.Max(0, facts.Drinks), factor, Round(fasting));
    }

    /// <summary>
    /// What a day's drinks leave of its diet XP (food-log plan §7.2): all of it up to
    /// <see cref="DrinksFree"/>, then <see cref="DrinkCutPercent"/>% less for each drink above,
    /// down to nothing.
    /// </summary>
    public decimal DrinkFactorFor(int drinks)
        => Math.Max(0m, 1m - DrinkCutPercent * Math.Max(0, drinks - DrinksFree) / 100m);

    /// <summary>
    /// The calorie part: 0 below <see cref="CalorieFloor"/>, full from there to
    /// <see cref="CalorieFullTo"/>, then fading evenly to 0 at <see cref="CalorieZeroAt"/>.
    /// </summary>
    public decimal CaloriesPartFor(long calories)
    {
        if (calories < CalorieFloor || calories >= CalorieZeroAt)
        {
            return 0m;
        }

        return calories <= CalorieFullTo
            ? CaloriesMax
            : (decimal)CaloriesMax * (CalorieZeroAt - calories) / (CalorieZeroAt - CalorieFullTo);
    }

    /// <summary>
    /// The fasting part for a fast of <paramref name="seconds"/>: nothing up to
    /// <see cref="FastFromHours"/>, then evenly up to <see cref="FastingMax"/> at
    /// <see cref="FastFullAtHours"/>, counted by the second; longer fasts pay the maximum.
    /// </summary>
    public decimal FastingFor(long seconds)
    {
        var hours = seconds / 3600m;
        var span = (decimal)(FastFullAtHours - FastFromHours);
        return FastingMax * Math.Clamp(hours - FastFromHours, 0m, span) / span;
    }

    /// <summary>Whole XP, halves up — the same rounding as the multiplier (<see cref="MultiplierRule.Apply"/>).</summary>
    public static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The two diet awards a day can pay (food-log plan §3.3), keyed by the home-zone day they're
/// credited to. Neither belongs to an item, like a rank-up:
/// <list type="bullet">
/// <item><c>fast:&lt;yyyy-MM-dd&gt;</c> — the longest fast ending that day, paid when it ends</item>
/// <item><c>diet:&lt;yyyy-MM-dd&gt;</c> — the day's food, paid once the day is closed</item>
/// </list>
/// Unlike every other award, a change to a day's entries re-prices them.
/// </summary>
public static class FoodAwards
{
    public const string FastTrigger = "fast";
    public const string DietTrigger = "diet";

    /// <summary>Both triggers.</summary>
    public static readonly IReadOnlyList<string> Triggers = [FastTrigger, DietTrigger];

    public static string FastKey(DateOnly day) => $"{FastTrigger}:{day:yyyy-MM-dd}";

    public static string DietKey(DateOnly day) => $"{DietTrigger}:{day:yyyy-MM-dd}";

    /// <summary>The day a food award key is for, or null when it isn't one.</summary>
    public static DateOnly? DayOf(string awardKey)
    {
        var separator = awardKey.IndexOf(':');
        return separator > 0
               && Triggers.Contains(awardKey[..separator])
               && DateOnly.TryParseExact(
                   awardKey[(separator + 1)..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var day)
            ? day
            : null;
    }
}

/// <summary>
/// A live food entry as <c>bsk.v_meal</c> reads it (FOOD-1): the newest version of its id.
/// <see cref="EatenLocal"/> is the wall-clock time it was eaten in the home zone, and
/// <see cref="EatenOn"/> the day it counts on. <see cref="Alcoholic"/> marks one alcoholic
/// drink (FOOD-6).
/// </summary>
public sealed record Meal(
    Guid Id,
    DateTimeOffset EatenAt,
    DateOnly EatenOn,
    DateTime EatenLocal,
    string Name,
    int Score,
    int Calories,
    int? ProteinG,
    int? CarbsG,
    int? FatG,
    string MealType,
    Guid? PhotoArtifactId,
    DateTimeOffset FirstLoggedAt,
    DateTimeOffset LoggedAt,
    int Versions,
    Guid EventId,
    bool Alcoholic = false);

/// <summary>
/// A live fast as <c>bsk.v_fast</c> reads it (FOOD-2): running while <see cref="EndedAt"/> is
/// null. <see cref="EndedOn"/> is the home-zone day it counts on; <see cref="MealsDuring"/>
/// counts food with calories eaten inside it.
/// </summary>
public sealed record Fast(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateOnly StartedOn,
    DateOnly? EndedOn,
    DateTime StartedLocal,
    DateTime? EndedLocal,
    long? Seconds,
    int MealsDuring,
    DateTimeOffset RecordedAt,
    Guid EventId)
{
    public bool Running => EndedAt is null;
}

/// <summary>
/// One home-zone day as <c>bsk.v_food_day</c> reads it (FOOD-3 / FOOD-4 / FOOD-6): its live
/// entries' totals, its drinks, the longest fast that ended on it, and whether it's closed — by
/// hand (<see cref="ClosedByHand"/>) or because it's over. <see cref="FoodEventIds"/>,
/// <see cref="DrinkEventIds"/> and <see cref="FastEventId"/> are the event versions a score of
/// it reads.
/// </summary>
public sealed record FoodDay(
    DateOnly Day,
    int Meals,
    long Calories,
    long ScoreCalories,
    long? LongestFastSeconds,
    Guid? LongestFastId,
    Guid? FastEventId,
    bool ClosedByHand,
    DateTimeOffset? ClosedAt,
    Guid? CloseEventId,
    bool Closed,
    DateTimeOffset? LastRecordedAt,
    IReadOnlyList<Guid> FoodEventIds,
    int Drinks = 0,
    IReadOnlyList<Guid>? DrinkEventIds = null)
{
    /// <summary>What the diet score reads of this day.</summary>
    public DietDayFacts Facts => new(Calories, ScoreCalories, LongestFastSeconds, Drinks);

    /// <summary>The drinks' event versions (none when the day has no drink).</summary>
    public IReadOnlyList<Guid> DrinkVersions => DrinkEventIds ?? [];

    /// <summary>A day with nothing on it.</summary>
    public static FoodDay Empty(DateOnly day, bool closed)
        => new(day, 0, 0, 0, null, null, null, false, null, null, closed, null, []);
}

/// <summary>
/// A sobriety streak as of one day (FOOD-6, food-log plan §7.3): <see cref="Days"/> drink-free
/// days in a row ending on it (0 when it has a drink), counted from <see cref="Since"/> — the
/// day after the last drink, or the Food Log's first day when there's been none.
/// <see cref="BestDays"/> is the longest run from <see cref="FirstDay"/> to that day.
/// </summary>
public sealed record SobrietyStreak(
    DateOnly AsOf, int Days, int BestDays, DateOnly Since, DateOnly? LastDrinkOn, DateOnly FirstDay);

/// <summary>
/// What the streak reads (FOOD-6): the Food Log's first day (null while it's empty) and the
/// days with a drink, oldest first.
/// </summary>
public sealed record DrinkHistory(DateOnly? FirstDay, IReadOnlyList<DateOnly> DrinkDays)
{
    /// <summary>The streak as of <paramref name="asOf"/>, or null before the first day.</summary>
    public SobrietyStreak? StreakOn(DateOnly asOf)
        => FirstDay is { } first ? Sobriety.On(asOf, first, DrinkDays) : null;
}

/// <summary>
/// The drinks drunk in <c>[From, To)</c> (FOOD-6, food-log plan §7.3): how many, on how many
/// home-zone days, and the most in one day (the first such day, when there's a tie).
/// </summary>
public sealed record DrinkTally(
    DateTimeOffset From, DateTimeOffset To, int Drinks, int DrinkDays, int MostInADay, DateOnly? MostOn)
{
    /// <summary>A tally from the drinks counted on each day.</summary>
    public static DrinkTally Of(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyList<(DateOnly Day, int Drinks)> days)
    {
        var withDrinks = days.Where(d => d.Drinks > 0).OrderBy(d => d.Day).ToList();
        var most = withDrinks.OrderByDescending(d => d.Drinks).ThenBy(d => d.Day).FirstOrDefault();
        return new DrinkTally(
            from, to, withDrinks.Sum(d => d.Drinks), withDrinks.Count, most.Drinks,
            withDrinks.Count > 0 ? most.Day : null);
    }
}

/// <summary>
/// The sobriety streak (FOOD-6, food-log plan §7.3). A drink-free day is one with no
/// alcoholic entry; a day with nothing logged counts as drink-free, since only a logged drink
/// breaks the streak (scout's honor, GEN-5). Nothing counts before the Food Log's first day.
/// </summary>
public static class Sobriety
{
    /// <summary>
    /// The streak as of <paramref name="asOf"/>, from the Food Log's <paramref name="firstDay"/>
    /// and the days with a drink; null before the first day.
    /// </summary>
    public static SobrietyStreak? On(DateOnly asOf, DateOnly firstDay, IEnumerable<DateOnly> drinkDays)
    {
        if (asOf < firstDay)
        {
            return null;
        }

        var drinks = drinkDays.Where(d => d >= firstDay && d <= asOf).Distinct().Order().ToList();
        var best = 0;
        var runStart = firstDay;
        foreach (var day in drinks)
        {
            best = Math.Max(best, day.DayNumber - runStart.DayNumber);
            runStart = day.AddDays(1);
        }

        var current = asOf.DayNumber - runStart.DayNumber + 1;
        best = Math.Max(best, current);
        DateOnly? last = drinks.Count > 0 ? drinks[^1] : null;
        return new SobrietyStreak(asOf, current, best, runStart, last, firstDay);
    }
}

/// <summary>
/// What must still hold when a food write lands (food-log plan §3.1), re-checked under the
/// food lock so two windows can't both start a fast or record overlapping ones.
/// <list type="bullet">
/// <item><see cref="RunningFast"/>: the fast that must be running (<see cref="Guid.Empty"/>
/// = none may be running); null = don't care.</item>
/// <item><see cref="Span"/>: a fast from <c>From</c> to <c>To</c> (null = still running, so
/// up to now) that no other live fast may overlap; <c>Except</c> is the fast being
/// changed.</item>
/// </list>
/// </summary>
public sealed record FoodGuard(Guid? RunningFast = null, FastSpan? Span = null)
{
    public static readonly FoodGuard None = new();
}

/// <summary>A fast's span for the overlap check: <paramref name="To"/> null runs to now.</summary>
public sealed record FastSpan(DateTimeOffset From, DateTimeOffset? To, Guid? Except = null);

/// <summary>Food Log times in words, in the home zone: "Mon Oct 19, 8:00 pm", "23 h 05 m".</summary>
public static class FoodText
{
    /// <summary>A home-zone wall-clock time: "Mon Oct 19, 8:00 pm".</summary>
    public static string When(DateTime local)
        => local.ToString("ddd MMM d, h:mm ", System.Globalization.CultureInfo.InvariantCulture)
           + (local.Hour < 12 ? "am" : "pm");

    /// <summary>A home-zone time of day: "8:00 pm".</summary>
    public static string Clock(DateTime local)
        => local.ToString("h:mm ", System.Globalization.CultureInfo.InvariantCulture) + (local.Hour < 12 ? "am" : "pm");

    /// <summary>A length of time: "23 h 05 m", or "45 m" under an hour.</summary>
    public static string Duration(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return minutes < 60 ? $"{minutes} m" : $"{minutes / 60} h {minutes % 60:00} m";
    }

    /// <summary>A fast's span: "from Mon Oct 19, 8:00 pm to Tue Oct 20, 2:00 pm", or "from … (still running)".</summary>
    public static string Span(Fast fast)
        => fast.EndedLocal is { } end
            ? $"from {When(fast.StartedLocal)} to {When(end)}"
            : $"from {When(fast.StartedLocal)} (still running)";
}
