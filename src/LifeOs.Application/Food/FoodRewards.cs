using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Food;

/// <summary>
/// Diet XP on the ledger (FOOD-3, food-log plan §3.3–3.4): each food day pays up to two awards
/// of base Health XP, neither belonging to an item:
/// <list type="bullet">
/// <item><c>fast:&lt;day&gt;</c> — the fasting award for the longest fast ending that day, due
/// once it has ended</item>
/// <item><c>diet:&lt;day&gt;</c> — the food award (quality and calories), due once the day is
/// closed, by hand or because it's over</item>
/// </list>
/// More than <see cref="DietRules.DrinksFree"/> drinks on a day cut both (FOOD-6, plan §7.2):
/// the food award when it pays at the close, and the fast award, paid in full when the fast
/// ended, re-scored once the day is closed.
/// Settling compares every food day with the ledger. Unlike every other award, a food award is
/// re-priced when the day it's for changes: the key's net is reversed and the new base earned,
/// in one batch. A paid award cites the event versions its day was scored from, and is left
/// alone while the day still reads exactly those, so a retune of <see cref="DietRules"/> never
/// rewrites a past day; only a change to the day's entries or fasts re-scores it.
/// <para>
/// Each earn is credited to its day, at that day's Health multiplier as it stands when it's
/// written (<see cref="DomainPricing"/>), and dated at its trigger — the fast's end, the close,
/// or for a day that closed itself the last change to it — or, for a re-price, at the change.
/// Either way it's dated after the key's previous earn, which <c>bsk.v_award_credit</c> would
/// otherwise keep reading. A fact from before the wallet opened never pays; a food write that
/// makes something pay opens the wallet, as an item's does.
/// </para>
/// </summary>
public sealed class FoodRewards(
    IFoodLogRepository food, ILedgerRepository ledger, RewardService rewards, IHomeCalendar calendar, IClock clock,
    EventSource eventSource)
{
    private static readonly DateOnly Earliest = new(2000, 1, 1);

    /// <summary>The diet rules days are scored with.</summary>
    public DietRules Rules { get; } = DietRules.Default;

    /// <summary>
    /// Settles every food day: earns what's due and unpaid, re-prices what's paid at a base
    /// the day no longer scores, and reverses what's no longer due. Answers what it wrote and
    /// each life domain whose level or rank moved.
    /// <para>
    /// <paramref name="causeEventId"/> is the food write that prompted it, cited by any
    /// reversal; <paramref name="touched"/> the days that write touched. Before the wallet has
    /// ever opened, a write that makes one of those days pay opens it, dated at that award's
    /// trigger; a settle with no cause never opens it.
    /// </para>
    /// </summary>
    public async Task<Earned> SettleAsync(
        Guid? causeEventId = null, IReadOnlyCollection<DateOnly>? touched = null,
        CancellationToken cancellationToken = default)
    {
        var openedAt = await ledger.GetOpenedAtAsync(cancellationToken);
        if (openedAt is null)
        {
            if (causeEventId is null || touched is not { Count: > 0 })
            {
                return Earned.Nothing;
            }

            var first = (await CandidatesAsync(touched.Min(), touched.Max(), cancellationToken))
                .Where(d => d.Base > 0 && touched.Contains(d.Day))
                .Select(d => (DateTimeOffset?)d.TriggeredAt)
                .Min();
            if (first is null)
            {
                return Earned.Nothing;
            }

            openedAt = (await rewards.OpenAsync(first, cancellationToken)).OpenedAt;
        }

        var opened = openedAt.Value;
        return await rewards.EarnAsync(
            (domains, token) => PlanAsync(opened, causeEventId, domains, token), cancellationToken);
    }

    /// <summary>
    /// The entries that bring the ledger in line with every food day (<see cref="DayAwardPlanner"/>).
    /// A base is due when it's above 0 and its trigger came on or after the wallet opened.
    /// </summary>
    private async Task<IReadOnlyList<PlannedAward>> PlanAsync(
        DateTimeOffset openedAt, Guid? causeEventId, Func<Task<IReadOnlyList<DomainProgress>>> domains,
        CancellationToken cancellationToken)
    {
        var today = await calendar.TodayAsync(cancellationToken);
        var candidates = await CandidatesAsync(Earliest, today, cancellationToken);
        var held = await ledger.GetKeyedAwardsAsync(FoodAwards.Triggers, cancellationToken);
        var policy = new DayAwardPolicy(
            Rules.Currency, eventSource.Id, causeEventId,
            IsDue: award => award.TriggeredAt >= openedAt,
            FirstEarnAt: award => award.TriggeredAt,
            ReversalNote: key => FoodAwards.DayOf(key) is { } day ? Note(AwardKeys.TriggerOf(key), day) : null);
        return await DayAwardPlanner.PlanAsync(policy, candidates, held, clock.UtcNow, domains);
    }

    /// <summary>
    /// The food awards the days from <paramref name="from"/> to <paramref name="to"/> could pay —
    /// a fast award for each day a fast ended on, a food award for each closed day with entries —
    /// each with its base (0 included), the instant it became due, and the events it's scored
    /// from.
    /// </summary>
    private async Task<IReadOnlyList<DayAward>> CandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var days = await food.GetDaysAsync(from, to, cancellationToken);
        var fastDays = days.Where(d => d.LongestFastId is not null).Select(d => d.Day).ToList();
        var fasts = fastDays.Count == 0
            ? new Dictionary<Guid, Fast>()
            : (await food.GetFastsEndedAsync(fastDays.Min(), fastDays.Max(), cancellationToken)).ToDictionary(f => f.Id);

        var due = new List<DayAward>();
        foreach (var day in days)
        {
            var score = Rules.Score(day.Facts);
            if (day is { LongestFastId: { } fastId, FastEventId: { } fastEvent }
                && fasts.TryGetValue(fastId, out var fast)
                && fast.EndedAt is { } endedAt)
            {
                // The fast pays in full when it ends; the day's drinks cut it only once the day
                // is closed (plan §7.2). A closed day with drinks cites them (and its close), so
                // a drink added, removed or flagged re-scores the fast — whatever the rules'
                // numbers, so a retune still leaves a paid fast alone.
                IReadOnlyList<Guid> sources = day is { Closed: true, Drinks: > 0 }
                    ? [fastEvent, .. day.DrinkVersions, .. day.CloseEventId is { } closeEvent ? [closeEvent] : Array.Empty<Guid>()]
                    : [fastEvent];
                due.Add(new DayAward(
                    FoodAwards.FastKey(day.Day), FoodAwards.FastTrigger, day.Day,
                    day.Closed ? score.FastAward : score.FastAwardUncut, endedAt,
                    sources, $"{Note(FoodAwards.FastTrigger, day.Day)} ({FoodText.Duration(fast.Seconds ?? 0)})"));
            }

            if (day is { Closed: true, Meals: > 0 }
                && (day.ClosedByHand ? day.ClosedAt : day.LastRecordedAt) is { } closedAt)
            {
                IReadOnlyList<Guid> sources = day.CloseEventId is { } close ? [.. day.FoodEventIds, close] : day.FoodEventIds;
                due.Add(new DayAward(
                    FoodAwards.DietKey(day.Day), FoodAwards.DietTrigger, day.Day, score.FoodAward, closedAt,
                    sources, Note(FoodAwards.DietTrigger, day.Day)));
            }
        }

        return due;
    }

    /// <summary>What a food award is for, in words: "Food day Tue Oct 20", "Fast ending Tue Oct 20".</summary>
    public static string Note(string trigger, DateOnly day)
        => (trigger == FoodAwards.FastTrigger ? "Fast ending " : "Food day ")
           + day.ToString("ddd MMM d", CultureInfo.InvariantCulture);

}

/// <summary>What the ledger pays a food day: its fast and its food, each null when unpaid.</summary>
public sealed record FoodDayPaid(DayAwardPaid? Fast, DayAwardPaid? Diet)
{
    /// <summary>Health XP paid for the day's diet, after the multiplier.</summary>
    public long Total => (Fast?.Paid ?? 0) + (Diet?.Paid ?? 0);

    /// <summary>Base Health XP paid for the day's diet, before the multiplier.</summary>
    public long BaseTotal => (Fast?.Base ?? 0) + (Diet?.Base ?? 0);

    public static readonly FoodDayPaid None = new(null, null);
}
