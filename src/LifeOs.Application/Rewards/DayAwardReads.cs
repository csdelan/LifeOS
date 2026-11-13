using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// What the ledger pays one day award now (food-log plan §3.4, activity plan A9): its base, what
/// it paid after the multiplier, and the multiplier when one above 1.0 applied.
/// </summary>
public sealed record DayAwardPaid(long Base, long Paid, decimal? Multiplier);

/// <summary>
/// What the ledger pays now for the awards keyed by day (<see cref="DayAwardKeys"/>), one read
/// shared by the Food Log and the Activity page. It reads the keys of the days asked for, by the
/// ledger's award-key index, not every keyed award ever written (code review CR78).
/// </summary>
public static class DayAwardReads
{
    /// <summary>
    /// What the ledger pays now for each award of <paramref name="triggers"/> on each day from
    /// <paramref name="from"/> to <paramref name="to"/>, in <paramref name="currency"/>, by day and
    /// trigger. A day or trigger the ledger doesn't pay now has no entry.
    /// </summary>
    public static async Task<IReadOnlyDictionary<DateOnly, IReadOnlyDictionary<string, DayAwardPaid>>> GetDayPaidAsync(
        this ILedgerRepository ledger,
        IReadOnlyCollection<string> triggers,
        DateOnly from,
        DateOnly to,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var byDay = new Dictionary<DateOnly, IReadOnlyDictionary<string, DayAwardPaid>>();
        if (to < from)
        {
            return byDay;
        }

        foreach (var award in await ledger.GetDayAwardsAsync(triggers, from, to, cancellationToken))
        {
            if (!award.Paid || DayAwardKeys.Parse(award.AwardKey, triggers) is not { } key || key.Day < from || key.Day > to)
            {
                continue;
            }

            var parts = byDay.TryGetValue(key.Day, out var found)
                ? (Dictionary<string, DayAwardPaid>)found
                : new Dictionary<string, DayAwardPaid>(StringComparer.Ordinal);
            parts[key.Trigger] = new DayAwardPaid(
                award.LatestBase.GetValueOrDefault(currency),
                award.Net.GetValueOrDefault(currency),
                award.LatestMultipliers.TryGetValue(currency, out var multiplier) ? multiplier : null);
            byDay[key.Day] = parts;
        }

        return byDay;
    }
}
