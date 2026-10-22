import { MoonIcon } from "lucide-react";
import { ScoreFigure } from "@/components/activity/sleep-parts";
import { WorkoutRowButton } from "@/components/activity/workout-parts";
import { ActivityXpList } from "@/components/activity/xp-parts";
import { Button } from "@/components/ui/button";
import { BAND_TONE, describeSteps, formatDuration, stepBand, stepsVerdict } from "@/lib/activity";
import { dayXp } from "@/lib/activity-xp";
import type { ActivitySummaryRead, WorkoutRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** "over the minimum" → "Over the minimum." */
function sentence(words: string): string {
  return `${words[0].toUpperCase()}${words.slice(1)}.`;
}

/**
 * Steps so far against the thresholds: a bar to the 7,500 goal with the 5,000 minimum marked,
 * toned by band, and the count said in words beside it.
 */
function StepProgress({ steps, minimum, goal }: { steps: number; minimum: number; goal: number }) {
  const band = stepBand(steps, { minimumSteps: minimum, goalSteps: goal });
  const words = describeSteps(steps, { minimumSteps: minimum, goalSteps: goal }, true);
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

/**
 * Today (activity plan §3.7): steps so far as the big number with progress past the minimum
 * toward the goal; last night's sleep, readiness and time asleep, or a nudge to open the Oura
 * app when it hasn't synced; today's counted workouts; and today's Health XP from them (A9):
 * each part earned or projected, never a projection shown as earned.
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
  const day = summary.todaySoFar;
  const counted = summary.todayWorkouts.filter((w) => w.counted);
  const night = day.sleepScore != null || day.readinessScore != null || day.asleepS != null;
  const xp = dayXp(day, summary.todayWorkouts);

  return (
    <section aria-labelledby="today-title" className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id="today-title" className="type-scale-section text-muted-foreground">
        Today
      </h2>

      <div className="space-y-2">
        <p className="text-xs text-muted-foreground">Steps so far</p>
        <p className="font-heading text-4xl leading-none tabular-nums">{(day.steps ?? 0).toLocaleString()}</p>
        <StepProgress steps={day.steps ?? 0} minimum={summary.minimumSteps} goal={summary.goalSteps} />
        <p className="text-sm text-muted-foreground">
          {day.steps == null ? "No steps from Oura yet today." : sentence(stepsVerdict(day.steps, summary))}
        </p>
      </div>

      <div className="border-t pt-3">
        <div className="mb-2 flex items-center justify-between gap-2">
          <h3 className="text-sm font-medium">Last night</h3>
          {night ? (
            <Button
              variant="ghost"
              size="sm"
              className="-my-1 text-xs"
              aria-label="Last night in full"
              onClick={() => onOpenDay(summary.today)}
            >
              In full
            </Button>
          ) : null}
        </div>
        {night ? (
          <dl className="grid grid-cols-3 gap-3">
            <ScoreFigure label="Sleep" value={day.sleepScore} />
            <ScoreFigure label="Readiness" value={day.readinessScore} />
            <div>
              <dt className="text-xs text-muted-foreground">Asleep</dt>
              <dd className="text-xl font-semibold tabular-nums">
                {day.asleepS != null ? formatDuration(day.asleepS) : "—"}
              </dd>
              {day.inBedS != null ? (
                <dd className="text-xs text-muted-foreground">of {formatDuration(day.inBedS)} in bed</dd>
              ) : null}
            </div>
          </dl>
        ) : (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <MoonIcon className="size-4" aria-hidden />
            Open the Oura app to sync last night.
          </p>
        )}
      </div>

      <div className="border-t pt-3">
        <h3 className="mb-1 text-sm font-medium">Exercise today</h3>
        {counted.length > 0 ? (
          <ul className="-mx-2">
            {counted.map((w) => (
              <li key={w.id}>
                <WorkoutRowButton workout={w} today={summary.today} onOpen={onOpenWorkout} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">No exercise recorded yet today.</p>
        )}
      </div>

      <div className="border-t pt-3">
        <ActivityXpList xp={xp} title="Health XP today" />
      </div>
    </section>
  );
}
