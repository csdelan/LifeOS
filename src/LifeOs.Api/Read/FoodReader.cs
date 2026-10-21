using LifeOs.Application.Food;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;

namespace LifeOs.Api.Read;

/// <summary>
/// The Food Log reads (FOOD-1..4, food-log plan §3.6) on the <c>bsk_reader</c> connection, like
/// every other API read: <see cref="FoodReads"/> over the food views (<c>bsk.v_meal</c>,
/// <c>bsk.v_fast</c>, <c>bsk.v_food_day</c>) and the ledger's keyed awards, so a day reads
/// exactly as <c>bsk food</c> prints it. No writes live here; they go through
/// <see cref="FoodLogService"/>.
/// </summary>
public sealed class FoodReader(string readerConnectionString, string homeTimeZone = HomeZone.Default)
{
    private readonly FoodReads reads = new(
        new NpgsqlFoodLogRepository(readerConnectionString),
        new NpgsqlLedgerRepository(readerConnectionString, homeTimeZone));

    /// <summary>One day (default today).</summary>
    public async Task<FoodDayRead> GetDayAsync(DateOnly? day, CancellationToken cancellationToken = default)
        => FoodRows.From(await reads.GetDayAsync(day, cancellationToken), reads.Rules);

    /// <summary>
    /// Every day from <paramref name="from"/> to <paramref name="to"/>, at most
    /// <see cref="FoodReads.MaxRangeDays"/>; a missing end defaults to today, a missing start
    /// to the Monday of <paramref name="to"/>'s week.
    /// </summary>
    public async Task<FoodDaysRead> GetDaysAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var end = to ?? await reads.TodayAsync(cancellationToken);
        var start = from ?? end.AddDays(-(((int)end.DayOfWeek + 6) % 7));
        return FoodRows.From(await reads.GetDaysAsync(start, end, cancellationToken), reads.Rules);
    }

    /// <summary>The latest entry of each name, newest first — "Log again".</summary>
    public async Task<IReadOnlyList<MealRow>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
        => (await reads.GetRecentAsync(limit, cancellationToken)).Select(m => FoodRows.From(m)).ToList();
}
