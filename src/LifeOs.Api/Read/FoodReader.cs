using System.Globalization;
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

    /// <summary>
    /// The drinks drunk between <paramref name="from"/> and <paramref name="to"/> (FOOD-6). Each
    /// end is a home-zone day (<c>yyyy-MM-dd</c>, both days included) or an ISO instant
    /// (<paramref name="to"/> excluded). By default, the last 30 days with today.
    /// </summary>
    public async Task<FoodDrinksRead> GetDrinksAsync(string? from, string? to, CancellationToken cancellationToken = default)
    {
        var today = await reads.TodayAsync(cancellationToken);
        var start = ParseEnd(from, "from") switch
        {
            DateOnly day => await reads.StartOfDayAsync(day, cancellationToken),
            DateTimeOffset instant => instant,
            _ => await reads.StartOfDayAsync(today.AddDays(-(DefaultDrinkDays - 1)), cancellationToken)
        };
        var end = ParseEnd(to, "to") switch
        {
            DateOnly day => await reads.StartOfDayAsync(day.AddDays(1), cancellationToken),
            DateTimeOffset instant => instant,
            _ => await reads.StartOfDayAsync(today.AddDays(1), cancellationToken)
        };
        return FoodRows.From(await reads.GetDrinksAsync(start, end, cancellationToken));
    }

    /// <summary>The days <see cref="GetDrinksAsync"/> reads by default: the last 30, today among them.</summary>
    public const int DefaultDrinkDays = 30;

    /// <summary>A range end: a day, an instant, or null when left out.</summary>
    private static object? ParseEnd(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
               && value.Contains('T')
               && (value.EndsWith('Z') || value.LastIndexOfAny(['+', '-']) > value.IndexOf('T'))
            ? instant
            : throw new ArgumentException(
                $"'{text}' isn't a {name}: give a day (YYYY-MM-DD) or a time with an offset (2026-10-22T18:00:00-07:00).");
    }

    /// <summary>The latest entry of each name, newest first — "Log again".</summary>
    public async Task<IReadOnlyList<MealRow>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
        => (await reads.GetRecentAsync(limit, cancellationToken)).Select(m => FoodRows.From(m)).ToList();
}
