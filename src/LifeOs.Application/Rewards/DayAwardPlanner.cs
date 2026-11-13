using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// An award a day could pay (a food day, a fast, a day's steps, exercise or sleep): its key, its
/// trigger, the day it's credited to, its base (0 included), the instant it became due, the event
/// versions it's priced from, and what it's for in words.
/// </summary>
public sealed record DayAward(
    string Key, string Trigger, DateOnly Day, long Base, DateTimeOffset TriggeredAt,
    IReadOnlyList<Guid> Sources, string Note);

/// <summary>
/// What sets one feature's day awards apart (<see cref="DayAwardPlanner"/>): the currency they
/// pay, who writes them, the write that prompted the settle (cited by a reversal; else the key's
/// newest entry is), when a candidate is due, when a key's first earn is dated, and a reversal's
/// note for a key.
/// </summary>
public sealed record DayAwardPolicy(
    string Currency,
    string SourceId,
    Guid? CauseEventId,
    Func<DayAward, bool> IsDue,
    Func<DayAward, DateTimeOffset> FirstEarnAt,
    Func<string, string?> ReversalNote);

/// <summary>
/// Settles day awards against the ledger, the same way for food days (<c>FoodRewards</c>) and
/// activity days (<c>ActivityRewards</c>) (code review CR20). For each key that could pay or is
/// paid:
/// <list type="bullet">
/// <item>paid, and its day still reads the events it was priced from: nothing</item>
/// <item>paid at the base due now: nothing</item>
/// <item>paid otherwise: a reversal of what it pays, then an earn at the base due (if any)</item>
/// <item>unpaid with a base due: an earn</item>
/// </list>
/// A paid award is left alone while its day reads exactly the events it was priced from, so a
/// retune of the rules never rewrites a past day. An earn is priced at its day's multiplier as it
/// stands when it's written (<see cref="DomainPricing"/>), and dated at the policy's first-earn
/// instant the first time the key pays, at the change after that, and always after the key's
/// previous earn.
/// </summary>
public static class DayAwardPlanner
{
    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The entries that bring <paramref name="held"/> in line with <paramref name="candidates"/>.
    /// <paramref name="domains"/> is read only when something earns.
    /// </summary>
    public static async Task<IReadOnlyList<PlannedAward>> PlanAsync(
        DayAwardPolicy policy, IEnumerable<DayAward> candidates, IEnumerable<KeyedAward> held, DateTimeOffset now,
        Func<Task<IReadOnlyList<DomainProgress>>> domains)
    {
        var byKey = candidates.ToDictionary(c => c.Key, StringComparer.Ordinal);
        var heldByKey = held.ToDictionary(k => k.AwardKey, StringComparer.Ordinal);

        var planned = new List<PlannedAward>();
        var keys = byKey.Keys.Union(heldByKey.Values.Where(h => h.Paid).Select(h => h.AwardKey)).Order(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            byKey.TryGetValue(key, out var candidate);
            heldByKey.TryGetValue(key, out var paid);
            var due = candidate is { Base: > 0 } && policy.IsDue(candidate) ? candidate : null;
            var earnAt = now;
            if (paid is { Paid: true })
            {
                if (candidate is not null && candidate.Sources.ToHashSet().SetEquals(paid.LatestSources))
                {
                    continue;
                }

                if (due is not null && paid.LatestBase.GetValueOrDefault(policy.Currency) == due.Base)
                {
                    continue;
                }

                // The new earn follows the reversal, so the ledger reads them in that order.
                planned.Add(Reversal(policy, paid, now));
                earnAt = now.AddMilliseconds(1);
            }

            if (due is not null)
            {
                planned.Add(Earn(policy, due, paid, earnAt, await domains()));
            }
        }

        return planned;
    }

    private static PlannedAward Earn(
        DayAwardPolicy policy, DayAward award, KeyedAward? previous, DateTimeOffset now,
        IReadOnlyList<DomainProgress> domains)
    {
        var earn = DomainPricing.Price(new Dictionary<string, long> { [policy.Currency] = award.Base }, award.Day, domains);
        var at = previous?.LatestEarnAt is null ? policy.FirstEarnAt(award) : now;
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
            EventKinds.Ledger, Provenances.Derived, at, now, policy.SourceId, PayloadJson: payload, DerivedFrom: award.Sources);
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
    private static PlannedAward Reversal(DayAwardPolicy policy, KeyedAward paid, DateTimeOffset now)
    {
        var amounts = paid.Net.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => -kv.Value);
        var trigger = AwardKeys.TriggerOf(paid.AwardKey);
        var note = policy.ReversalNote(paid.AwardKey);
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
            EventKinds.Ledger, Provenances.Derived, now, now, policy.SourceId, PayloadJson: payload,
            DerivedFrom: [policy.CauseEventId ?? paid.LastEventId]);
        return new PlannedAward(
            new LedgerWrite(
                newEvent, [], GuardBalance: false, AwardKey: paid.AwardKey, ExpectPaid: true,
                ExpectLatestEarn: paid.LatestEarnId),
            new Award(Guid.Empty, null, LedgerEntries.Reversal, trigger, paid.AwardKey, amounts, note));
    }
}
