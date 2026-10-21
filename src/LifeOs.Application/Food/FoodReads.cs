using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Food;

/// <summary>
/// The Food Log's reads (FOOD-1..4, food-log plan §3.6): one day, a range of days and recent
/// entries, each day with its score and what the ledger pays it. Nothing here writes, so a
/// host can build it on the reader connection (the API's <c>FoodReader</c>) as well as behind
/// <see cref="FoodLogService"/>, and both read alike.
/// </summary>
public sealed class FoodReads(IFoodLogRepository food, ILedgerRepository ledger)
{
    /// <summary>The widest range <see cref="GetDaysAsync"/> answers.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>The most recent entries <see cref="GetRecentAsync"/> answers.</summary>
    public const int MaxRecent = 100;

    /// <summary>The diet rules days are scored with.</summary>
    public DietRules Rules { get; } = DietRules.Default;

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => food.TodayAsync(cancellationToken);

    /// <summary>
    /// One day of the Food Log (default today): its entries, the fasts that ended on it, the
    /// running fast, when food with calories was last eaten (for Begin fast), its score, and
    /// what the ledger pays it.
    /// </summary>
    public async Task<FoodDayView> GetDayAsync(DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        var today = await food.TodayAsync(cancellationToken);
        var target = day ?? today;
        var facts = (await food.GetDaysAsync(target, target, cancellationToken)).FirstOrDefault()
                    ?? FoodDay.Empty(target, target < today);
        var meals = await food.GetMealsAsync(target, target, cancellationToken);
        var fasts = await food.GetFastsEndedAsync(target, target, cancellationToken);
        var running = await food.GetRunningFastAsync(cancellationToken);
        var last = (await food.GetLatestMealsAsync(1, withCalories: true, cancellationToken)).FirstOrDefault();
        var paid = await GetPaidAsync(target, target, cancellationToken);
        return new FoodDayView(
            target, today, meals.OrderByDescending(m => m.EatenAt).ToList(), fasts, running, last, facts,
            Rules.Score(facts.Facts), await EatenDuringFastsAsync(meals, cancellationToken),
            paid.GetValueOrDefault(target) ?? FoodDayPaid.None);
    }

    /// <summary>
    /// Every day from <paramref name="from"/> to <paramref name="to"/> (inclusive, at most
    /// <see cref="MaxRangeDays"/>), each with its facts, score and what's paid; a day with
    /// nothing on it is empty.
    /// </summary>
    public async Task<FoodRange> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The range must end on or after the day it starts.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            throw new ArgumentException($"Ask for at most {MaxRangeDays} days at a time.");
        }

        var today = await food.TodayAsync(cancellationToken);
        var byDay = (await food.GetDaysAsync(from, to, cancellationToken)).ToDictionary(d => d.Day);
        var paid = await GetPaidAsync(from, to, cancellationToken);
        var days = new List<FoodDaySummary>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var facts = byDay.TryGetValue(d, out var found) ? found : FoodDay.Empty(d, d < today);
            days.Add(new FoodDaySummary(facts, Rules.Score(facts.Facts), paid.GetValueOrDefault(d) ?? FoodDayPaid.None));
        }

        return new FoodRange(from, to, today, days);
    }

    /// <summary>
    /// The latest entry of each distinct name (any case), newest first, at most
    /// <paramref name="limit"/> — what "Log again" offers.
    /// </summary>
    public async Task<IReadOnlyList<Meal>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxRecent)
        {
            throw new ArgumentException($"Ask for 1 to {MaxRecent} recent entries.");
        }

        var latest = await food.GetLatestMealsAsync(limit * 10, withCalories: false, cancellationToken);
        return latest
            .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// What the ledger pays each day from <paramref name="from"/> to <paramref name="to"/> for
    /// its fast and its food: base, paid and multiplier, for the keys paid now.
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, FoodDayPaid>> GetPaidAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var byDay = new Dictionary<DateOnly, FoodDayPaid>();
        foreach (var award in await ledger.GetKeyedAwardsAsync(FoodAwards.Triggers, cancellationToken))
        {
            if (!award.Paid || FoodAwards.DayOf(award.AwardKey) is not { } day || day < from || day > to)
            {
                continue;
            }

            var paid = new FoodPaid(
                award.LatestBase.GetValueOrDefault(Rules.Currency),
                award.Net.GetValueOrDefault(Rules.Currency),
                award.LatestMultipliers.TryGetValue(Rules.Currency, out var multiplier) ? multiplier : null);
            var current = byDay.GetValueOrDefault(day) ?? FoodDayPaid.None;
            byDay[day] = award.AwardKey.StartsWith(FoodAwards.FastTrigger + ":", StringComparison.Ordinal)
                ? current with { Fast = paid }
                : current with { Diet = paid };
        }

        return byDay;
    }

    /// <summary>
    /// Which of <paramref name="meals"/> (with calories) were eaten strictly inside a recorded
    /// fast, and which fast: the day view flags them and offers to end that fast there.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Guid>> EatenDuringFastsAsync(
        IReadOnlyList<Meal> meals, CancellationToken cancellationToken)
    {
        var eating = meals.Where(m => m.Calories > 0).ToList();
        if (eating.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var span = new FastSpan(eating.Min(m => m.EatenAt), eating.Max(m => m.EatenAt));
        var fasts = await food.GetOverlappingFastsAsync(span, cancellationToken);
        var flagged = new Dictionary<Guid, Guid>();
        foreach (var meal in eating)
        {
            var inside = fasts.FirstOrDefault(f => f.StartedAt < meal.EatenAt && (f.EndedAt is null || meal.EatenAt < f.EndedAt));
            if (inside is not null)
            {
                flagged[meal.Id] = inside.Id;
            }
        }

        return flagged;
    }
}
