using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// Activity XP on the ledger (ACTIVITY-5, activity plan §3.8, A8): each Oura day from
/// <see cref="ActivityRules.XpFrom"/> pays up to three awards of base Health XP, none belonging
/// to an item:
/// <list type="bullet">
/// <item><c>steps:&lt;day&gt;</c> — the day's steps, due once Oura's day is over (4 a.m. the next
/// morning in the home zone)</item>
/// <item><c>exercise:&lt;day&gt;</c> — the day's counted exercise, due once Oura's day is over</item>
/// <item><c>sleep:&lt;day&gt;</c> — the night ending that morning, due once it has a sleep score</item>
/// </list>
/// Settling compares every day with the ledger, as <c>FoodRewards</c> does for food days: an
/// award is re-priced when the Oura data it was priced from changes — the key's net reversed and
/// the new base earned, in one batch. A paid award cites the event versions it was priced from
/// (the day's activity record, its counted workouts, its sleep score) and is left alone while
/// the day still reads exactly those, so a retune of <see cref="ActivityRules"/> never rewrites
/// a past day.
/// <para>
/// Each earn is credited to its Oura day, at that day's Health multiplier as it stands when it's
/// written (<see cref="DomainPricing"/>), and dated at its trigger — the day's last sync for
/// steps, the last counted workout's end for exercise, the wake time (else the score's sync) for
/// sleep — or, for a re-price, at the change; always after the key's previous earn. Missing data
/// pays nothing: a day without a step count or a sleep score has no award for it. Readiness
/// never pays.
/// </para>
/// <para>
/// Activity never opens the wallet, and pays the Oura days from the one the wallet opened on: a
/// day is whole, so the night that ended the morning the wallet opened, or a run before it, pays
/// like the rest of that day, dated at the opening rather than before it. A day before the
/// opening never pays, whenever it syncs. (Until 2026-10-23 it went by the trigger's instant,
/// and Chris's first day lost its sleep: he woke before 7 and the wallet opened at 11.)
/// </para>
/// </summary>
public sealed class ActivityRewards(
    IActivityRepository activity,
    ILedgerRepository ledger,
    RewardService rewards,
    IHomeCalendar calendar,
    IClock clock,
    string sourceId,
    ActivityRules? rules = null)
{
    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The activity rules days are scored with.</summary>
    public ActivityRules Rules { get; } = rules ?? ActivityRules.Default;

    /// <summary>
    /// Settles every activity day: earns what's due and unpaid, re-prices what's paid at a base
    /// the day no longer scores, and reverses what's no longer due. Answers what it wrote and
    /// each life domain whose level or rank moved. Safe to repeat: nothing pays twice.
    /// </summary>
    public async Task<Earned> SettleAsync(CancellationToken cancellationToken = default)
    {
        if (await ledger.GetOpenedAtAsync(cancellationToken) is not { } openedAt)
        {
            return Earned.Nothing;
        }

        return await rewards.EarnAsync((domains, token) => PlanAsync(openedAt, domains, token), cancellationToken);
    }

    /// <summary>
    /// The day Oura's activity day is on at <paramref name="instant"/>: the home-zone date four
    /// hours earlier, since Oura's day runs from 4 a.m. to 4 a.m.
    /// </summary>
    public Task<DateOnly> OuraDayAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(instant.AddHours(-ActivityAwards.DayStartHour), cancellationToken);

    /// <summary>
    /// The entries that bring the ledger in line with every activity day. For each key that
    /// could pay or is paid:
    /// <list type="bullet">
    /// <item>paid, and its day still reads the events it was priced from: nothing</item>
    /// <item>paid at the base due now: nothing</item>
    /// <item>paid otherwise: a reversal of what it pays, then an earn at the base due (if any)</item>
    /// <item>unpaid with a base due: an earn</item>
    /// </list>
    /// A base is due when it's above 0 and its Oura day is on or after the one the wallet opened on.
    /// </summary>
    private async Task<IReadOnlyList<PlannedAward>> PlanAsync(
        DateTimeOffset openedAt, Func<Task<IReadOnlyList<DomainProgress>>> domains, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var openedOn = await OuraDayAtAsync(openedAt, cancellationToken);
        var candidates = (await CandidatesAsync(now, cancellationToken)).ToDictionary(c => c.Key, StringComparer.Ordinal);
        var held = (await ledger.GetKeyedAwardsAsync(ActivityAwards.Triggers, cancellationToken))
            .ToDictionary(k => k.AwardKey, StringComparer.Ordinal);

        var planned = new List<PlannedAward>();
        var keys = candidates.Keys.Union(held.Values.Where(h => h.Paid).Select(h => h.AwardKey)).Order(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            candidates.TryGetValue(key, out var candidate);
            held.TryGetValue(key, out var paid);
            var due = candidate is { Base: > 0 } && candidate.Day >= openedOn ? candidate : null;
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
                planned.Add(Reversal(paid, now));
                earnAt = now.AddMilliseconds(1);
            }

            if (due is not null)
            {
                planned.Add(Earn(due, paid, earnAt, openedAt, await domains()));
            }
        }

        return planned;
    }

    /// <summary>
    /// The activity awards the days from <see cref="ActivityRules.XpFrom"/> through today could
    /// pay, each with its base (0 included), the instant it became due, and the events it's
    /// priced from: steps and exercise for each day Oura has finished, sleep for each day with
    /// a score.
    /// </summary>
    private async Task<IReadOnlyList<DueActivity>> CandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var from = Rules.XpFrom;
        if (today < from)
        {
            return [];
        }

        var ouraToday = await OuraDayAtAsync(now, cancellationToken);
        var due = new List<DueActivity>();

        foreach (var day in await activity.GetActivityDaysAsync(from, today, cancellationToken))
        {
            if (day.Day < ouraToday && day.Steps is { } steps)
            {
                due.Add(new DueActivity(
                    ActivityAwards.StepsKey(day.Day), ActivityAwards.StepsTrigger, day.Day, Rules.StepsXp(steps),
                    day.SyncedAt, [day.EventId]));
            }
        }

        var counted = (await activity.GetWorkoutsAsync(from, today, cancellationToken))
            .Where(w => w.Verdict.Counted && w.Day < ouraToday)
            .GroupBy(w => w.Day);
        foreach (var workouts in counted)
        {
            var seconds = ActivityRules.ExerciseSeconds(workouts);
            due.Add(new DueActivity(
                ActivityAwards.ExerciseKey(workouts.Key), ActivityAwards.ExerciseTrigger, workouts.Key, Rules.ExerciseXp(seconds),
                workouts.Max(w => w.EndedAt), [.. workouts.Select(w => w.EventId)]));
        }

        foreach (var sleep in await activity.GetSleepDaysAsync(from, today, cancellationToken))
        {
            if (sleep is { Score: { } score, ScoreEventId: { } scoreEvent })
            {
                due.Add(new DueActivity(
                    ActivityAwards.SleepKey(sleep.Day), ActivityAwards.SleepTrigger, sleep.Day, Rules.SleepXp(score),
                    sleep.WokeAt ?? sleep.SyncedAt, [scoreEvent]));
            }
        }

        return due;
    }

    /// <summary>
    /// An earn of <paramref name="award"/> at its day's multiplier: dated at its trigger the
    /// first time the key pays (at the opening, for a trigger earlier on the day the wallet
    /// opened), at the change that re-priced it after that, and always after the key's previous
    /// earn.
    /// </summary>
    private PlannedAward Earn(
        DueActivity award, KeyedAward? previous, DateTimeOffset now, DateTimeOffset openedAt,
        IReadOnlyList<DomainProgress> domains)
    {
        var earn = DomainPricing.Price(new Dictionary<string, long> { [Rules.Currency] = award.Base }, award.Day, domains);
        var at = previous?.LatestEarnAt is null
            ? (award.TriggeredAt < openedAt ? openedAt : award.TriggeredAt)
            : now;
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

    /// <summary>A reversal of everything <paramref name="paid"/> pays, citing the key's newest entry, guarded on the latest earn it read.</summary>
    private PlannedAward Reversal(KeyedAward paid, DateTimeOffset now)
    {
        var amounts = paid.Net.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => -kv.Value);
        var trigger = AwardKeys.TriggerOf(paid.AwardKey);
        var note = ActivityAwards.DayOf(paid.AwardKey) is { } day ? Note(trigger, day) : null;
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
            DerivedFrom: [paid.LastEventId]);
        return new PlannedAward(
            new LedgerWrite(
                newEvent, [], GuardBalance: false, AwardKey: paid.AwardKey, ExpectPaid: true,
                ExpectLatestEarn: paid.LatestEarnId),
            new Award(Guid.Empty, null, LedgerEntries.Reversal, trigger, paid.AwardKey, amounts, note));
    }

    /// <summary>What an activity award is for, in words: "Steps Thu Oct 22", "Exercise Thu Oct 22", "Sleep Thu Oct 22".</summary>
    public static string Note(string trigger, DateOnly day)
        => trigger switch
        {
            ActivityAwards.StepsTrigger => "Steps ",
            ActivityAwards.ExerciseTrigger => "Exercise ",
            _ => "Sleep "
        }
        + day.ToString("ddd MMM d", CultureInfo.InvariantCulture);

    /// <summary>
    /// An activity award a day could pay: its key, trigger, day, base, when it became due, and
    /// the events it's priced from. Its note names the part and the day only: Oura's numbers
    /// stay out of the append-only ledger, so purging Oura's data (activity plan §7) never has
    /// to touch it.
    /// </summary>
    private sealed record DueActivity(
        string Key, string Trigger, DateOnly Day, long Base, DateTimeOffset TriggeredAt, IReadOnlyList<Guid> Sources)
    {
        public string Note => ActivityRewards.Note(Trigger, Day);
    }
}
