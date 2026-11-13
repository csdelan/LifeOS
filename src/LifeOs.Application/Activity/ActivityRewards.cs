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
    EventSource eventSource,
    ActivityRules? rules = null)
{
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
    /// The entries that bring the ledger in line with every activity day (<see cref="DayAwardPlanner"/>).
    /// A base is due when it's above 0 and its Oura day is on or after the one the wallet opened on.
    /// A key's first earn is dated at its trigger, or at the opening for a trigger earlier on the
    /// day the wallet opened.
    /// </summary>
    private async Task<IReadOnlyList<PlannedAward>> PlanAsync(
        DateTimeOffset openedAt, Func<Task<IReadOnlyList<DomainProgress>>> domains, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var openedOn = await OuraDayAtAsync(openedAt, cancellationToken);
        var candidates = await CandidatesAsync(now, cancellationToken);
        var held = await ledger.GetKeyedAwardsAsync(ActivityAwards.Triggers, cancellationToken);
        var policy = new DayAwardPolicy(
            Rules.Currency, eventSource.Id, CauseEventId: null,
            IsDue: award => award.Day >= openedOn,
            FirstEarnAt: award => award.TriggeredAt < openedAt ? openedAt : award.TriggeredAt,
            ReversalNote: key => ActivityAwards.DayOf(key) is { } day ? Note(AwardKeys.TriggerOf(key), day) : null);
        return await DayAwardPlanner.PlanAsync(policy, candidates, held, now, domains);
    }

    /// <summary>
    /// The activity awards the days from <see cref="ActivityRules.XpFrom"/> through today could
    /// pay, each with its base (0 included), the instant it became due, and the events it's
    /// priced from: steps and exercise for each day Oura has finished, sleep for each day with
    /// a score.
    /// </summary>
    private async Task<IReadOnlyList<DayAward>> CandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var from = Rules.XpFrom;
        if (today < from)
        {
            return [];
        }

        var ouraToday = await OuraDayAtAsync(now, cancellationToken);
        var due = new List<DayAward>();

        foreach (var day in await activity.GetActivityDaysAsync(from, today, cancellationToken))
        {
            if (day.Day < ouraToday && day.Steps is { } steps)
            {
                due.Add(new DayAward(
                    ActivityAwards.StepsKey(day.Day), ActivityAwards.StepsTrigger, day.Day, Rules.StepsXp(steps),
                    day.SyncedAt, [day.EventId], Note(ActivityAwards.StepsTrigger, day.Day)));
            }
        }

        var counted = (await activity.GetWorkoutsAsync(from, today, cancellationToken))
            .Where(w => w.Verdict.Counted && w.Day < ouraToday)
            .GroupBy(w => w.Day);
        foreach (var workouts in counted)
        {
            var seconds = ActivityRules.ExerciseSeconds(workouts);
            due.Add(new DayAward(
                ActivityAwards.ExerciseKey(workouts.Key), ActivityAwards.ExerciseTrigger, workouts.Key, Rules.ExerciseXp(seconds),
                workouts.Max(w => w.EndedAt), [.. workouts.Select(w => w.EventId)],
                Note(ActivityAwards.ExerciseTrigger, workouts.Key)));
        }

        foreach (var sleep in await activity.GetSleepDaysAsync(from, today, cancellationToken))
        {
            if (sleep is { Score: { } score, ScoreEventId: { } scoreEvent })
            {
                due.Add(new DayAward(
                    ActivityAwards.SleepKey(sleep.Day), ActivityAwards.SleepTrigger, sleep.Day, Rules.SleepXp(score),
                    sleep.WokeAt ?? sleep.SyncedAt, [scoreEvent], Note(ActivityAwards.SleepTrigger, sleep.Day)));
            }
        }

        return due;
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

}
