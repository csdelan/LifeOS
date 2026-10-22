using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// What the ledger pays one activity award (activity plan §3.8, A9): its base Health XP, what
/// it paid after the multiplier, and the multiplier when one above 1.0 applied.
/// </summary>
public sealed record ActivityPaid(long Base, long Paid, decimal? Multiplier);

/// <summary>What the ledger pays an Oura day's activity: its steps, exercise and sleep, each null when unpaid.</summary>
public sealed record ActivityDayPaid(ActivityPaid? Steps, ActivityPaid? Exercise, ActivityPaid? Sleep)
{
    public static readonly ActivityDayPaid None = new(null, null, null);

    /// <summary>Health XP paid for the day's activity, after the multiplier.</summary>
    public long Total => (Steps?.Paid ?? 0) + (Exercise?.Paid ?? 0) + (Sleep?.Paid ?? 0);

    /// <summary>Base Health XP paid for the day's activity, before the multiplier.</summary>
    public long BaseTotal => (Steps?.Base ?? 0) + (Exercise?.Base ?? 0) + (Sleep?.Base ?? 0);
}

/// <summary>
/// Reads what the ledger pays each Oura day for its activity (activity plan A9), so the Activity
/// page can show what a day earned beside what it's projected to. Nothing here writes; paying
/// is <see cref="ActivityRewards"/>'.
/// </summary>
public sealed class ActivityPaidReads(ILedgerRepository ledger, ActivityRules? rules = null)
{
    private readonly ActivityRules rules = rules ?? ActivityRules.Default;

    /// <summary>What the ledger pays each day from <paramref name="from"/> to <paramref name="to"/>, for the keys paid now.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, ActivityDayPaid>> GetPaidAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var byDay = new Dictionary<DateOnly, ActivityDayPaid>();
        foreach (var award in await ledger.GetKeyedAwardsAsync(ActivityAwards.Triggers, cancellationToken))
        {
            if (!award.Paid || ActivityAwards.DayOf(award.AwardKey) is not { } day || day < from || day > to)
            {
                continue;
            }

            var paid = new ActivityPaid(
                award.LatestBase.GetValueOrDefault(rules.Currency),
                award.Net.GetValueOrDefault(rules.Currency),
                award.LatestMultipliers.TryGetValue(rules.Currency, out var multiplier) ? multiplier : null);
            var current = byDay.GetValueOrDefault(day) ?? ActivityDayPaid.None;
            byDay[day] = AwardKeys.TriggerOf(award.AwardKey) switch
            {
                ActivityAwards.StepsTrigger => current with { Steps = paid },
                ActivityAwards.ExerciseTrigger => current with { Exercise = paid },
                _ => current with { Sleep = paid },
            };
        }

        return byDay;
    }

    /// <summary>What the ledger pays <paramref name="day"/>'s activity.</summary>
    public async Task<ActivityDayPaid> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default)
        => (await GetPaidAsync(day, day, cancellationToken)).GetValueOrDefault(day) ?? ActivityDayPaid.None;
}
