using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// One local day of the rewards history (REWARD-5): per currency, what the day's entries
/// earned, gave back and spent (positive totals), and <see cref="Net"/> — the signed sum of
/// everything that day, so a later kind of entry (a penalty) still counts. Each map is in
/// display order and leaves out currencies at zero. Weeks and months are roll-ups of these
/// days; a correction shows on the day it was made, so a past day never silently changes.
/// </summary>
public sealed record LedgerDay(
    DateOnly Date,
    IReadOnlyDictionary<string, long> Earned,
    IReadOnlyDictionary<string, long> Reversed,
    IReadOnlyDictionary<string, long> Spent,
    IReadOnlyDictionary<string, long> Net);

/// <summary>
/// The rules of a daily-history read, shared by <c>bsk ledger --daily</c> and
/// <c>GET /api/ledger/daily</c>, and the fold of the repository's day × currency rows into
/// <see cref="LedgerDay"/>s.
/// </summary>
public static class LedgerDays
{
    /// <summary>The widest range one read covers: two years of days.</summary>
    public const int MaxRangeDays = 731;

    /// <summary>
    /// Refuses a range that doesn't end after it starts or spans more than
    /// <see cref="MaxRangeDays"/> days, and a time zone that is blank or can't be a zone
    /// name. Whether the zone exists is the database's call.
    /// </summary>
    public static void Validate(DateOnly from, DateOnly to, string? timeZone)
    {
        if (to <= from)
        {
            throw new ArgumentException("The history range must end after it starts.");
        }

        var days = to.DayNumber - from.DayNumber;
        if (days > MaxRangeDays)
        {
            throw new ArgumentException(
                $"The history range can span at most {MaxRangeDays} days; this one spans {days}.");
        }

        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 64 || timeZone.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Unknown time zone '{timeZone}'.");
        }
    }

    /// <summary>The days that have entries, oldest first.</summary>
    public static IReadOnlyList<LedgerDay> Fold(IEnumerable<LedgerDayTotal> totals)
        => totals
            .GroupBy(t => t.Date)
            .OrderBy(day => day.Key)
            .Select(day => new LedgerDay(
                day.Key,
                Amounts(day, t => t.Earned),
                Amounts(day, t => t.Reversed),
                Amounts(day, t => t.Spent),
                Amounts(day, t => t.Net)))
            .ToList();

    private static IReadOnlyDictionary<string, long> Amounts(
        IEnumerable<LedgerDayTotal> day, Func<LedgerDayTotal, long> pick)
        => day
            .Where(t => pick(t) != 0)
            .OrderBy(t => DisplayOrder(t.Currency))
            .ThenBy(t => t.Currency, StringComparer.Ordinal)
            .ToDictionary(t => t.Currency, pick);

    // A currency the vocabulary no longer names sorts last.
    private static int DisplayOrder(string currency)
    {
        for (var i = 0; i < Currencies.All.Count; i++)
        {
            if (string.Equals(Currencies.All[i].Key, currency, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
