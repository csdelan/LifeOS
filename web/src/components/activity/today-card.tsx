import { useId, useState } from "react";
import { ChevronLeftIcon, ChevronRightIcon, MoonIcon, SunIcon } from "lucide-react";
import { ActivityCard, ActivityCardTitle } from "@/components/activity/activity-card";
import { ScoreFigure } from "@/components/activity/sleep-parts";
import { WorkoutRowButton } from "@/components/activity/workout-parts";
import { ActivityXpList } from "@/components/activity/xp-parts";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { BAND_TONE, dayTitle, dayWhen, describeSteps, formatDuration, stepBand, stepsVerdict } from "@/lib/activity";
import { dayXp } from "@/lib/activity-xp";
import { addDays } from "@/lib/dates";
import type { ActivityDayRow, ActivitySummaryRead, WorkoutRow } from "@/lib/production-ui-types";
import { useActivityDay } from "@/lib/queries";
import { cn } from "@/lib/utils";

/** "over the minimum" → "Over the minimum." */
function sentence(words: string): string {
  return `${words[0].toUpperCase()}${words.slice(1)}.`;
}

/**
 * Steps against the thresholds: a bar to the 7,500 goal with the 5,000 minimum marked, toned by
 * band, and the count said in words beside it ("so far" while the day is still going).
 */
function StepProgress({ steps, minimum, goal, soFar }: { steps: number; minimum: number; goal: number; soFar: boolean }) {
  const band = stepBand(steps, { minimumSteps: minimum, goalSteps: goal });
  const words = describeSteps(steps, { minimumSteps: minimum, goalSteps: goal }, soFar);
  return (
    <div>
      <div
        role="meter"
        aria-label="Steps toward the goal"
        aria-valuemin={0}
        aria-valuemax={goal}
        aria-valuenow={Math.min(steps, goal)}
        aria-valuetext={words}
        className="relative h-2.5 overflow-visible rounded-full bg-muted"
      >
        <div
          className={cn("h-full rounded-full", BAND_TONE[band])}
          style={{ width: `${Math.min(steps / goal, 1) * 100}%` }}
        />
        <div
          className="absolute -top-1 h-[calc(100%+0.5rem)] border-l-2 border-dotted border-foreground/60"
          style={{ left: `${(minimum / goal) * 100}%` }}
          aria-hidden
        />
      </div>
      <div className="relative mt-1 h-4 text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
        <span className="absolute -translate-x-1/2" style={{ left: `${(minimum / goal) * 100}%` }}>
          {minimum.toLocaleString()} minimum
        </span>
        <span className="absolute right-0">{goal.toLocaleString()} goal</span>
      </div>
    </div>
  );
}

/** The day the card shows: today's so far from the summary, an earlier day from its own read. */
function DayBody({
  row,
  workouts,
  today,
  title,
  minimum,
  goal,
  onOpenWorkout,
  onOpenDay,
}: {
  row: ActivityDayRow;
  workouts: WorkoutRow[];
  today: string;
  title: string;
  minimum: number;
  goal: number;
  onOpenWorkout: (w: WorkoutRow) => void;
  onOpenDay: (day: string) => void;
}) {
  const isToday = row.day === today;
  const counted = workouts.filter((w) => w.counted);
  const night = row.sleepScore != null || row.readinessScore != null || row.asleepS != null;
  const xp = dayXp(row, workouts);
  const targets = { minimumSteps: minimum, goalSteps: goal };

  return (
    <>
      <div className="space-y-2">
        <p className="text-xs text-muted-foreground">{isToday ? "Steps so far" : "Steps"}</p>
        <p className="font-heading text-4xl leading-none tabular-nums">{(row.steps ?? 0).toLocaleString()}</p>
        <StepProgress steps={row.steps ?? 0} minimum={minimum} goal={goal} soFar={isToday} />
        <p className="text-sm text-muted-foreground">
          {row.steps == null
            ? isToday
              ? "No steps from Oura yet today."
              : "No steps from Oura that day."
            : sentence(stepsVerdict(row.steps, targets))}
        </p>
      </div>

      <div className="border-t pt-3">
        <div className="mb-2 flex items-center justify-between gap-2">
          <h3 className="text-sm font-medium">{isToday ? "Last night" : "The night before"}</h3>
          {night ? (
            <Button
              variant="ghost"
              size="sm"
              className="-my-1 text-xs"
              aria-label={isToday ? "Last night in full" : `${title} in full`}
              onClick={() => onOpenDay(row.day)}
            >
              In full
            </Button>
          ) : null}
        </div>
        {night ? (
          <dl className="grid grid-cols-3 gap-3">
            <ScoreFigure label="Sleep" value={row.sleepScore} />
            <ScoreFigure label="Readiness" value={row.readinessScore} />
            <div>
              <dt className="text-xs text-muted-foreground">Asleep</dt>
              <dd className="text-xl font-semibold tabular-nums">
                {row.asleepS != null ? formatDuration(row.asleepS) : "—"}
              </dd>
              {row.inBedS != null ? (
                <dd className="text-xs text-muted-foreground">of {formatDuration(row.inBedS)} in bed</dd>
              ) : null}
            </div>
          </dl>
        ) : (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <MoonIcon className="size-4" aria-hidden />
            {isToday ? "Open the Oura app to sync last night." : "Oura has nothing for that night."}
          </p>
        )}
      </div>

      <div className="border-t pt-3">
        <h3 className="mb-1 text-sm font-medium">{isToday ? "Exercise today" : "Exercise"}</h3>
        {counted.length > 0 ? (
          <ul className="-mx-2">
            {counted.map((w) => (
              <li key={w.id}>
                <WorkoutRowButton workout={w} today={today} onOpen={onOpenWorkout} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">
            {isToday ? "No exercise recorded yet today." : "No exercise recorded that day."}
          </p>
        )}
      </div>

      <div className="border-t pt-3">
        <ActivityXpList
          xp={xp}
          title={isToday ? "Health XP today" : title === "Yesterday" ? "Health XP yesterday" : `Health XP on ${title}`}
        />
      </div>
    </>
  );
}

/**
 * Today, or a day before it (activity plan §3.7): steps as the big number with progress past
 * the minimum toward the goal; the night before it, with sleep, readiness and time asleep (or a
 * nudge to open the Oura app when last night hasn't synced); the day's counted workouts; and
 * its Health XP (A9), each part earned or projected, never a projection shown as earned. The
 * arrows step a day at a time, back to the start of what's synced from Oura; Today comes back.
 * Today reads from the summary; an earlier day from its own read, the one its sheet uses.
 */
export function TodayCard({
  summary,
  onOpenWorkout,
  onOpenDay,
}: {
  summary: ActivitySummaryRead;
  onOpenWorkout: (w: WorkoutRow) => void;
  onOpenDay: (day: string) => void;
}) {
  const titleId = useId();
  const today = summary.today;
  // Null follows today, so the card moves on to a new day by itself.
  const [picked, setPicked] = useState<string | null>(null);
  const day = picked != null && picked < today ? picked : today;
  const isToday = day === today;
  const detail = useActivityDay(isToday ? null : day);
  const earliest = summary.oura.historyFrom;
  const title = dayTitle(day, today);

  const read = !isToday && detail.data?.day === day ? detail.data : null;
  const row = isToday ? summary.todaySoFar : read?.summary;
  const workouts = isToday ? summary.todayWorkouts : (read?.workouts ?? []);
  const step = (by: number) => {
    const next = addDays(day, by);
    setPicked(next >= today ? null : next);
  };

  return (
    <ActivityCard tone="today" titleId={titleId}>
      <ActivityCardTitle id={titleId} tone="today" icon={SunIcon} title={title} description={dayWhen(day, today)} live>
        {/* On a phone the arrows take their own row, one at each end, so they never move as the title changes. */}
        <nav aria-label="Days" className="flex w-full items-center justify-between gap-1 sm:w-auto sm:justify-start">
          <Button
            variant="ghost"
            size="icon"
            aria-label="Previous day"
            disabled={earliest != null && day <= earliest}
            onClick={() => step(-1)}
          >
            <ChevronLeftIcon />
          </Button>
          <Button variant="outline" size="sm" disabled={isToday} onClick={() => setPicked(null)}>
            Today
          </Button>
          <Button variant="ghost" size="icon" aria-label="Next day" disabled={isToday} onClick={() => step(1)}>
            <ChevronRightIcon />
          </Button>
        </nav>
      </ActivityCardTitle>

      {row ? (
        <DayBody
          row={row}
          workouts={workouts}
          today={today}
          title={title}
          minimum={summary.minimumSteps}
          goal={summary.goalSteps}
          onOpenWorkout={onOpenWorkout}
          onOpenDay={onOpenDay}
        />
      ) : detail.isError ? (
        <p role="alert" className="text-sm text-destructive">
          This day failed to load.
        </p>
      ) : (
        <ListSkeleton rows={4} />
      )}
    </ActivityCard>
  );
}
