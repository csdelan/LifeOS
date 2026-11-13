using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>What the ledger pays an Oura day's activity: its steps, exercise and sleep, each null when unpaid.</summary>
public sealed record ActivityDayPaid(DayAwardPaid? Steps, DayAwardPaid? Exercise, DayAwardPaid? Sleep)
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
        var paid = await ledger.GetDayPaidAsync(ActivityAwards.Triggers, from, to, rules.Currency, cancellationToken);
        return paid.ToDictionary(
            day => day.Key,
            day => new ActivityDayPaid(
                day.Value.GetValueOrDefault(ActivityAwards.StepsTrigger),
                day.Value.GetValueOrDefault(ActivityAwards.ExerciseTrigger),
                day.Value.GetValueOrDefault(ActivityAwards.SleepTrigger)));
    }

    /// <summary>What the ledger pays <paramref name="day"/>'s activity.</summary>
    public async Task<ActivityDayPaid> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default)
        => (await GetPaidAsync(day, day, cancellationToken)).GetValueOrDefault(day) ?? ActivityDayPaid.None;
}
