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
    IFoodLogRepository food, ILedgerRepository ledger, RewardService rewards, IClock clock, string sourceId)
{
    private static readonly DateOnly Earliest = new(2000, 1, 1);

    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
            var current = byDay.GetValueOrDefault(day) ?? new FoodDayPaid(null, null);
            byDay[day] = award.AwardKey.StartsWith(FoodAwards.FastTrigger + ":", StringComparison.Ordinal)
                ? current with { Fast = paid }
                : current with { Diet = paid };
        }

        return byDay;
    }

    /// <summary>
    /// The entries that bring the ledger in line with every food day. For each key that could
    /// pay or is paid:
    /// <list type="bullet">
    /// <item>paid, and its day still reads the events it was scored from: nothing</item>
    /// <item>paid at the base due now: nothing</item>
    /// <item>paid otherwise: a reversal of what it pays, then an earn at the base due (if any)</item>
    /// <item>unpaid with a base due: an earn</item>
    /// </list>
    /// A base is due when it's above 0 and its trigger came on or after the wallet opened.
    /// </summary>
    private async Task<IReadOnlyList<PlannedAward>> PlanAsync(
        DateTimeOffset openedAt, Guid? causeEventId, Func<Task<IReadOnlyList<DomainProgress>>> domains,
        CancellationToken cancellationToken)
    {
        var today = await food.TodayAsync(cancellationToken);
        var candidates = (await CandidatesAsync(Earliest, today, cancellationToken))
            .ToDictionary(c => c.Key, StringComparer.Ordinal);
        var held = (await ledger.GetKeyedAwardsAsync(FoodAwards.Triggers, cancellationToken))
            .ToDictionary(k => k.AwardKey, StringComparer.Ordinal);

        var now = clock.UtcNow;
        var planned = new List<PlannedAward>();
        var keys = candidates.Keys.Union(held.Values.Where(h => h.Paid).Select(h => h.AwardKey)).Order(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            candidates.TryGetValue(key, out var candidate);
            held.TryGetValue(key, out var paid);
            var due = candidate is { Base: > 0 } && candidate.TriggeredAt >= openedAt ? candidate : null;
            var earnAt = now;
            if (paid is { Paid: true })
            {
                if (candidate is not null && candidate.Sources.ToHashSet().SetEquals(paid.LatestSources))
                {
                    continue;
                }

                if (due is not null && paid.LatestBase.GetValueOrDefault(Rules.Currency) == due.Base)
                {
                    continue;
                }

                // The new earn follows the reversal, so the ledger reads them in that order.
                planned.Add(Reversal(paid, causeEventId, now));
                earnAt = now.AddMilliseconds(1);
            }

            if (due is not null)
            {
                planned.Add(Earn(due, paid, earnAt, await domains()));
            }
        }

        return planned;
    }

    /// <summary>
    /// The food awards the days from <paramref name="from"/> to <paramref name="to"/> could pay —
    /// a fast award for each day a fast ended on, a food award for each closed day with entries —
    /// each with its base (0 included), the instant it became due, and the events it's scored
    /// from.
    /// </summary>
    private async Task<IReadOnlyList<DueFood>> CandidatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var days = await food.GetDaysAsync(from, to, cancellationToken);
        var fastDays = days.Where(d => d.LongestFastId is not null).Select(d => d.Day).ToList();
        var fasts = fastDays.Count == 0
            ? new Dictionary<Guid, Fast>()
            : (await food.GetFastsEndedAsync(fastDays.Min(), fastDays.Max(), cancellationToken)).ToDictionary(f => f.Id);

        var due = new List<DueFood>();
        foreach (var day in days)
        {
            var score = Rules.Score(day.Facts);
            if (day is { LongestFastId: { } fastId, FastEventId: { } fastEvent }
                && fasts.TryGetValue(fastId, out var fast)
                && fast.EndedAt is { } endedAt)
            {
                due.Add(new DueFood(
                    FoodAwards.FastKey(day.Day), FoodAwards.FastTrigger, day.Day, score.FastAward, endedAt,
                    [fastEvent], $"{Note(FoodAwards.FastTrigger, day.Day)} ({FoodText.Duration(fast.Seconds ?? 0)})"));
            }

            if (day is { Closed: true, Meals: > 0 }
                && (day.ClosedByHand ? day.ClosedAt : day.LastRecordedAt) is { } closedAt)
            {
                IReadOnlyList<Guid> sources = day.CloseEventId is { } close ? [.. day.FoodEventIds, close] : day.FoodEventIds;
                due.Add(new DueFood(
                    FoodAwards.DietKey(day.Day), FoodAwards.DietTrigger, day.Day, score.FoodAward, closedAt,
                    sources, Note(FoodAwards.DietTrigger, day.Day)));
            }
        }

        return due;
    }

    /// <summary>
    /// An earn of <paramref name="award"/> at its day's multiplier: dated at its trigger the
    /// first time the key pays, at the change that re-scored it after that, and always after
    /// the key's previous earn.
    /// </summary>
    private PlannedAward Earn(
        DueFood award, KeyedAward? previous, DateTimeOffset now, IReadOnlyList<DomainProgress> domains)
    {
        var earn = DomainPricing.Price(new Dictionary<string, long> { [Rules.Currency] = award.Base }, award.Day, domains);
        var at = previous?.LatestEarnAt is null ? award.TriggeredAt : now;
        if (previous?.LatestEarnAt is { } last && at <= last)
        {
            at = last.AddMilliseconds(1);
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                entry = LedgerEntries.Earn,
                amounts = earn.Amounts,
                award_key = award.Key,
                trigger = award.Trigger,
                credited_on = award.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                @base = earn.Base,
                multipliers = earn.Multipliers,
                note = award.Note
            },
            OmitNulls);
        var newEvent = new NewEvent(
            EventKinds.Ledger, Provenances.Derived, at, now, sourceId, PayloadJson: payload, DerivedFrom: award.Sources);
        return new PlannedAward(
            new LedgerWrite(newEvent, [], GuardBalance: false, AwardKey: award.Key, ExpectPaid: false),
            new Award(
                Guid.Empty, null, LedgerEntries.Earn, award.Trigger, award.Key, earn.Amounts, award.Note,
                earn.Base, earn.Multipliers));
    }

    /// <summary>
    /// A reversal of everything <paramref name="paid"/> pays, citing the write that prompted it
    /// (else the key's newest entry), guarded on the latest earn it read.
    /// </summary>
    private PlannedAward Reversal(KeyedAward paid, Guid? causeEventId, DateTimeOffset now)
    {
        var amounts = paid.Net.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => -kv.Value);
        var trigger = AwardKeys.TriggerOf(paid.AwardKey);
        var note = FoodAwards.DayOf(paid.AwardKey) is { } day ? Note(trigger, day) : null;
        var payload = JsonSerializer.Serialize(
            new
            {
                entry = LedgerEntries.Reversal,
                amounts,
                award_key = paid.AwardKey,
                trigger,
                note
            },
            OmitNulls);
        var newEvent = new NewEvent(
            EventKinds.Ledger, Provenances.Derived, now, now, sourceId, PayloadJson: payload,
            DerivedFrom: [causeEventId ?? paid.LastEventId]);
        return new PlannedAward(
            new LedgerWrite(
                newEvent, [], GuardBalance: false, AwardKey: paid.AwardKey, ExpectPaid: true,
                ExpectLatestEarn: paid.LatestEarnId),
            new Award(Guid.Empty, null, LedgerEntries.Reversal, trigger, paid.AwardKey, amounts, note));
    }

    /// <summary>What a food award is for, in words: "Food day Tue Oct 20", "Fast ending Tue Oct 20".</summary>
    public static string Note(string trigger, DateOnly day)
        => (trigger == FoodAwards.FastTrigger ? "Fast ending " : "Food day ")
           + day.ToString("ddd MMM d", CultureInfo.InvariantCulture);

    /// <summary>A food award a day could pay: its key, trigger, day, base, when it became due, the events it's scored from, and its note.</summary>
    private sealed record DueFood(
        string Key, string Trigger, DateOnly Day, long Base, DateTimeOffset TriggeredAt,
        IReadOnlyList<Guid> Sources, string Note);
}

/// <summary>What the ledger pays for one food award: its base, what it paid, and the multiplier (null at 1.0).</summary>
public sealed record FoodPaid(long Base, long Paid, decimal? Multiplier);

/// <summary>What the ledger pays a food day: its fast and its food, each null when unpaid.</summary>
public sealed record FoodDayPaid(FoodPaid? Fast, FoodPaid? Diet)
{
    /// <summary>Health XP paid for the day's diet, after the multiplier.</summary>
    public long Total => (Fast?.Paid ?? 0) + (Diet?.Paid ?? 0);

    /// <summary>Base Health XP paid for the day's diet, before the multiplier.</summary>
    public long BaseTotal => (Fast?.Base ?? 0) + (Diet?.Base ?? 0);

    public static readonly FoodDayPaid None = new(null, null);
}
