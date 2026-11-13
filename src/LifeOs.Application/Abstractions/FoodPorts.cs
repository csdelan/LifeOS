using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// The Food Log (D13).

/// <summary>
/// Reads and appends the Food Log (FOOD-1..4, food-log plan §3.1–§3.2). Nothing is stored
/// but append-only events: food entries, fasts and day closes, folded by <c>bsk.v_meal</c>,
/// <c>bsk.v_fast</c> and <c>bsk.v_food_day</c>. Days and wall-clock times are in the
/// database's home zone (<c>bsk.home_zone()</c>), the same one those views read.
/// </summary>
public interface IFoodLogRepository
{
    /// <summary>The live entry with that id, or null (never logged, or deleted).</summary>
    Task<Meal?> GetMealAsync(Guid mealId, CancellationToken cancellationToken = default);

    /// <summary>Live entries whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<Meal>> FindMealsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Live entries eaten on the home-zone days [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<Meal>> GetMealsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest live entries, at most <paramref name="limit"/>; only those with calories when
    /// <paramref name="withCalories"/> is set.
    /// </summary>
    Task<IReadOnlyList<Meal>> GetLatestMealsAsync(
        int limit, bool withCalories, CancellationToken cancellationToken = default);

    /// <summary>The live fast with that id, or null.</summary>
    Task<Fast?> GetFastAsync(Guid fastId, CancellationToken cancellationToken = default);

    /// <summary>Live fasts whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<Fast>> FindFastsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>The fast running now, or null.</summary>
    Task<Fast?> GetRunningFastAsync(CancellationToken cancellationToken = default);

    /// <summary>Live fasts that ended on the home-zone days [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<Fast>> GetFastsEndedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The live fasts that overlap <paramref name="span"/>, other than its <c>Except</c>.</summary>
    Task<IReadOnlyList<Fast>> GetOverlappingFastsAsync(FastSpan span, CancellationToken cancellationToken = default);

    /// <summary>The days with food, fasts or closes in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<FoodDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the sobriety streak reads (FOOD-6): the Food Log's first day and the days up to
    /// <paramref name="to"/> with a drink.
    /// </summary>
    Task<DrinkHistory> GetDrinkHistoryAsync(DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The drinks drunk in [<paramref name="from"/>, <paramref name="to"/>), counted by the
    /// home-zone day they were drunk on, oldest first (FOOD-6).
    /// </summary>
    Task<IReadOnlyList<(DateOnly Day, int Drinks)>> CountDrinksAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>The home-zone day and wall-clock time of <paramref name="instant"/>.</summary>
    Task<DateTime> LocalAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default);

    /// <summary>The instant a home-zone wall-clock <paramref name="local"/> time names.</summary>
    Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends food events as one transaction under the food lock, after re-checking
    /// <paramref name="guard"/> under it. When the guard no longer holds, nothing is written
    /// and <see cref="FoodConflictException"/> is thrown. Returns the new event ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<NewEvent> events, FoodGuard guard, CancellationToken cancellationToken = default);
}
