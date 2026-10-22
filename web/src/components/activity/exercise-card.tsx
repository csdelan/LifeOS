import { useId, useState } from "react";
import { ChevronDownIcon } from "lucide-react";
import { WorkoutRowButton } from "@/components/activity/workout-parts";
import { DailyMetricChart, MetricTable, type MetricBar } from "@/components/charts/daily-metric-chart";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Button } from "@/components/ui/button";
import { EXERCISE_WEEKS, exerciseFrom, formatDuration, weekTitle, weeklyExercise, workoutWeeks } from "@/lib/activity";
import { formatDay, startOfWeekMonday } from "@/lib/dates";
import type { ActivitySummaryRead, ActivityWeekRow, WorkoutRow } from "@/lib/production-ui-types";
import { useActivityWorkouts } from "@/lib/queries";
import { cn } from "@/lib/utils";

function minutes(seconds: number): number {
  return Math.round(seconds / 60);
}

function Compare({
  label,
  now,
  before,
  format,
}: {
  label: string;
  now: number;
  before: number;
  format: (n: number) => string;
}) {
  return (
    <div className="rounded-lg bg-muted/60 p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-base font-semibold whitespace-nowrap tabular-nums sm:text-lg">{format(now)}</dd>
      <dd className="text-xs text-muted-foreground">Last week {format(before)}</dd>
    </div>
  );
}

function WeekCompare({ thisWeek, lastWeek }: { thisWeek: ActivityWeekRow; lastWeek: ActivityWeekRow }) {
  return (
    <dl className="grid grid-cols-3 gap-2" aria-label="This week against last week">
      <Compare label="Sessions" now={thisWeek.sessions} before={lastWeek.sessions} format={(n) => String(n)} />
      <Compare
        label="Minutes"
        now={thisWeek.exerciseSeconds}
        before={lastWeek.exerciseSeconds}
        format={(s) => (s === 0 ? "0 min" : formatDuration(s))}
      />
      <Compare
        label="Calories"
        now={thisWeek.exerciseCalories}
        before={lastWeek.exerciseCalories}
        format={(n) => n.toLocaleString()}
      />
    </dl>
  );
}

/**
 * Exercise (ACTIVITY-3, activity plan §3.7): this week against last, minutes per week for 12
 * weeks, and the counted workouts by week, newest first. "Show N not counted" adds the rest,
 * muted, each with its reason. A row opens the workout's sheet, where its note is.
 */
export function ExerciseCard({
  summary,
  onOpenWorkout,
}: {
  summary: ActivitySummaryRead;
  onOpenWorkout: (w: WorkoutRow) => void;
}) {
  const today = summary.today;
  const workouts = useActivityWorkouts(exerciseFrom(today), today);
  const [showAll, setShowAll] = useState(false);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const titleId = useId();
  const listId = useId();

  const all = workouts.data?.workouts ?? [];
  const notCounted = all.filter((w) => !w.counted).length;
  const visible = showAll ? all : all.filter((w) => w.counted);
  const weeks = weeklyExercise(all, today, EXERCISE_WEEKS);
  const thisMonday = startOfWeekMonday(today);

  const bars: MetricBar[] = weeks.map((w) => ({
    key: w.from,
    value: minutes(w.seconds),
    tone: "bg-chart-earned",
    partial: w.from === thisMonday,
    title: weekTitle(w.from, today),
    text:
      w.sessions === 0
        ? "no exercise recorded"
        : `${formatDuration(w.seconds)} of exercise${w.from === thisMonday ? " so far" : ""}, ${w.sessions} session${w.sessions === 1 ? "" : "s"}`,
    axis: w.from === thisMonday ? "This week" : formatDay(w.from),
  }));

  return (
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id={titleId} className="type-scale-section text-muted-foreground">
        Exercise
      </h2>

      <WeekCompare thisWeek={summary.thisWeek} lastWeek={summary.lastWeek} />

      {workouts.isLoading ? <ListSkeleton rows={4} /> : null}
      {workouts.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The workouts failed to load.
        </p>
      ) : null}

      {workouts.data ? (
        <div
          aria-busy={workouts.isPlaceholderData || undefined}
          className={cn("space-y-4 transition-opacity", workouts.isPlaceholderData && "opacity-60")}
        >
          <div className="space-y-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="text-sm font-medium">Minutes per week</h3>
              <ToggleGroup
                label="Show as"
                value={mode}
                onChange={setMode}
                options={[
                  { id: "chart", label: "Chart" },
                  { id: "table", label: "Table" },
                ]}
              />
            </div>
            {mode === "chart" ? (
              <DailyMetricChart
                label={`Minutes of counted exercise by week, last ${EXERCISE_WEEKS} weeks`}
                bars={bars}
                legend={[
                  { kind: "bar", tone: "bg-chart-earned", label: "Minutes of counted exercise" },
                  { kind: "so-far", tone: "bg-chart-earned", label: "This week, so far" },
                ]}
              />
            ) : (
              <MetricTable
                label="Minutes per week, as a table"
                caption={`Counted exercise by week, last ${EXERCISE_WEEKS} weeks, newest first`}
                columns={[
                  { key: "week", label: "Week" },
                  { key: "minutes", label: "Minutes", numeric: true },
                  { key: "sessions", label: "Sessions", numeric: true },
                ]}
                rows={[...weeks].reverse().map((w) => ({
                  key: w.from,
                  cells: [weekTitle(w.from, today), minutes(w.seconds), w.sessions],
                }))}
              />
            )}
          </div>

          <div id={listId} className="space-y-4">
            {visible.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No exercise recorded in the last {EXERCISE_WEEKS} weeks. Workouts you record in the Oura app show
                here.
              </p>
            ) : (
              workoutWeeks(visible).map((week) => {
                const counted = week.workouts.filter((w) => w.counted);
                const seconds = counted.reduce((s, w) => s + w.seconds, 0);
                return (
                  <section key={week.from} aria-label={weekTitle(week.from, today)}>
                    <h3 className="flex items-baseline justify-between gap-2 text-sm font-medium">
                      {weekTitle(week.from, today)}
                      <span className="text-xs font-normal text-muted-foreground tabular-nums">
                        {counted.length} session{counted.length === 1 ? "" : "s"}
                        {seconds > 0 ? ` · ${formatDuration(seconds)}` : ""}
                      </span>
                    </h3>
                    <ul className="-mx-2 mt-1">
                      {week.workouts.map((w) => (
                        <li key={w.id}>
                          <WorkoutRowButton workout={w} today={today} onOpen={onOpenWorkout} />
                        </li>
                      ))}
                    </ul>
                  </section>
                );
              })
            )}
          </div>

          {notCounted > 0 ? (
            <Button
              variant="ghost"
              size="sm"
              aria-expanded={showAll}
              aria-controls={listId}
              onClick={() => setShowAll((s) => !s)}
            >
              <ChevronDownIcon className={cn("transition-transform", showAll && "rotate-180")} aria-hidden />
              {showAll ? `Hide ${notCounted} not counted` : `Show ${notCounted} not counted`}
            </Button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
