import { useId, useState } from "react";
import { FootprintsIcon } from "lucide-react";
import { ActivityCard, ActivityCardTitle } from "@/components/activity/activity-card";
import { StatTile } from "@/components/activity/sleep-parts";
import { XpCell } from "@/components/activity/xp-parts";
import {
  DailyMetricChart,
  MetricTable,
  type MetricBar,
  type MetricLegendItem,
  type MetricReference,
} from "@/components/charts/daily-metric-chart";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import {
  BAND_TONE,
  bandWords,
  dayName,
  formatDuration,
  formatWeekday,
  HIGH_NON_WEAR_S,
  METRIC_RANGES,
  rangeFrom,
  rangeStart,
  rollingAverage,
  stepBand,
  stepDayText,
  stepWeeks,
  weekTitle,
  type MetricRange,
  type StepTargets,
} from "@/lib/activity";
import { ACTIVITY_RULES } from "@/lib/activity-score";
import { dayPartXp, paidBetween, paidFor } from "@/lib/activity-xp";
import { formatDay } from "@/lib/dates";
import type { ActivityDayRow, ActivitySummaryRead } from "@/lib/production-ui-types";
import { useActivityDays } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const VIEW = "activity.steps";

function bandCell(steps: number | null, targets: StepTargets) {
  if (steps == null) return <span className="text-muted-foreground">—</span>;
  const band = stepBand(steps, targets);
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={cn("size-2.5 shrink-0 rounded-[2px]", BAND_TONE[band])} aria-hidden />
      {bandWords(band)}
    </span>
  );
}

/**
 * Steps (ACTIVITY-2, activity plan §3.7): the chart over 30 days, 90 days or a year (a bar per
 * week), toned by band, with the 7-day average and the two thresholds; a Table view of every
 * day; and tiles for the 7-day average, this week against last, days at the goal, days under
 * the minimum, the best day, and the Health XP the steps earned (A9). The Table gives each day's
 * steps XP: earned, or projected while it isn't paid yet.
 */
export function StepsCard({ summary }: { summary: ActivitySummaryRead }) {
  const [range, setRangeState] = useState<MetricRange>(() => loadView<{ range: MetricRange }>(VIEW, { range: "30" }).range);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const titleId = useId();
  const today = summary.today;
  const days = useActivityDays(rangeFrom(range, today), today);
  const targets: StepTargets = summary;

  const setRange = (next: MetricRange) => {
    setRangeState(next);
    try {
      saveView(VIEW, { range: next });
    } catch {
      // Storage can be full or blocked; the range still applies to this view.
    }
  };

  const all = days.data?.days ?? [];
  const averages = rollingAverage(all, today);
  const yearly = range === "365";
  const first = rangeStart(range, today);
  const shown = all.map((d, i) => ({ day: d, average: averages[i] })).filter(({ day }) => day.day >= first);
  const label = METRIC_RANGES.find((r) => r.id === range)!.label;

  const references: MetricReference[] = [
    { value: targets.minimumSteps, label: "Minimum", pattern: "dotted" },
    { value: targets.goalSteps, label: "Goal", pattern: "dashed" },
  ];

  const toneLegend: MetricLegendItem[] = [
    { kind: "bar", tone: BAND_TONE.under, label: "Under the minimum" },
    { kind: "bar", tone: BAND_TONE.minimum, label: "Over the minimum" },
    { kind: "bar", tone: BAND_TONE.goal, label: "At the goal" },
  ];

  let bars: MetricBar[];
  let legend: MetricLegendItem[];
  if (yearly) {
    const weeks = stepWeeks(
      shown.map(({ day }) => day),
      today,
    );
    bars = weeks.map((w) => ({
      key: w.from,
      value: w.average,
      tone: w.average != null ? BAND_TONE[stepBand(w.average, targets)] : "",
      partial: w.partial,
      title: weekTitle(w.from, today),
      text:
        w.average == null
          ? "no steps recorded"
          : `${w.average.toLocaleString()} steps a day${w.partial ? " so far" : ""}, ${bandWords(stepBand(w.average, targets))}`,
      detail: w.days > 0 ? `averaged over ${w.days} day${w.days === 1 ? "" : "s"}` : w.partial ? "today so far" : undefined,
      axis: w.partial ? "This week" : formatDay(w.from),
    }));
    legend = [
      ...toneLegend,
      { kind: "so-far", tone: BAND_TONE.minimum, label: "This week, so far" },
      { kind: "dotted", label: `${targets.minimumSteps.toLocaleString()} minimum` },
      { kind: "dashed", label: `${targets.goalSteps.toLocaleString()} goal` },
    ];
  } else {
    bars = shown.map(({ day, average }) => ({
      key: day.day,
      value: day.steps,
      tone: day.steps != null ? BAND_TONE[stepBand(day.steps, targets)] : "",
      partial: day.day === today,
      title: dayName(day.day, today),
      text: stepDayText(day, targets, today),
      detail: average != null ? `7-day average ${average.toLocaleString()}` : undefined,
      axis: day.day === today ? "Today" : formatDay(day.day),
    }));
    legend = [
      ...toneLegend,
      { kind: "so-far", tone: BAND_TONE.minimum, label: "Today, so far" },
      { kind: "line", label: "7-day average" },
      { kind: "dotted", label: `${targets.minimumSteps.toLocaleString()} minimum` },
      { kind: "dashed", label: `${targets.goalSteps.toLocaleString()} goal` },
    ];
  }

  // The tiles read the days the range shows.
  const inRange = shown.map(({ day }) => day);
  const withSteps = inRange.filter((d): d is ActivityDayRow & { steps: number } => d.steps != null);
  const complete = withSteps.filter((d) => d.day < today);
  const latestAverage = [...averages].reverse().find((a) => a != null) ?? null;
  const goalDays = withSteps.filter((d) => d.steps >= targets.goalSteps).length;
  const underDays = complete.filter((d) => d.steps < targets.minimumSteps).length;
  const best = withSteps.reduce<(typeof withSteps)[number] | null>((b, d) => (b == null || d.steps > b.steps ? d : b), null);
  const stepsPaid = paidFor(inRange, "steps");
  const thisWeek = summary.thisWeek.averageSteps;
  const lastWeek = summary.lastWeek.averageSteps;

  return (
    <ActivityCard tone="steps" titleId={titleId}>
      <ActivityCardTitle id={titleId} tone="steps" icon={FootprintsIcon} title="Steps">
        <div className="flex flex-wrap items-center gap-3">
          <ToggleGroup label="Range" value={range} onChange={setRange} options={[...METRIC_RANGES]} />
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
      </ActivityCardTitle>

      {days.isLoading ? <ListSkeleton rows={3} /> : null}
      {days.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The steps failed to load.
        </p>
      ) : null}

      {days.data ? (
        <div
          aria-busy={days.isPlaceholderData || undefined}
          className={cn("space-y-4 transition-opacity", days.isPlaceholderData && "opacity-60")}
        >
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <StatTile
              label="7-day average"
              value={latestAverage != null ? latestAverage.toLocaleString() : "—"}
              note={latestAverage != null ? bandWords(stepBand(latestAverage, targets)) : "No days yet"}
            />
            <StatTile
              label="This week, a day"
              value={thisWeek != null ? thisWeek.toLocaleString() : "—"}
              note={lastWeek != null ? `Last week ${lastWeek.toLocaleString()}` : "Nothing last week"}
            />
            <StatTile label="At the goal" value={String(goalDays)} note={`of ${withSteps.length} days with steps`} />
            <StatTile label="Under the minimum" value={String(underDays)} note={`of ${complete.length} full days`} />
            <StatTile
              label="Best day"
              value={best ? best.steps.toLocaleString() : "—"}
              note={best ? dayName(best.day, today) : undefined}
            />
            <StatTile label="Steps XP" value={stepsPaid.toLocaleString()} note={`Health XP earned, last ${label}`} />
          </dl>

          {mode === "chart" ? (
            <DailyMetricChart
              label={yearly ? `Average steps a day by week, last year` : `Steps by day, last ${label}`}
              bars={bars}
              average={yearly ? undefined : shown.map(({ average }) => average)}
              references={references}
              legend={legend}
            />
          ) : yearly ? (
            <MetricTable
              label="Steps by week, as a table"
              caption="Average steps a day by week, last year, newest first"
              columns={[
                { key: "week", label: "Week" },
                { key: "steps", label: "Steps a day", numeric: true },
                { key: "band", label: "Band" },
                { key: "days", label: "Days", numeric: true },
                { key: "xp", label: "Health XP", numeric: true },
              ]}
              rows={stepWeeks(inRange, today)
                .reverse()
                .map((w) => ({
                  key: w.from,
                  cells: [
                    weekTitle(w.from, today),
                    w.average != null ? `${w.average.toLocaleString()}${w.partial ? " so far" : ""}` : "—",
                    bandCell(w.average, targets),
                    w.days,
                    w.to < ACTIVITY_RULES.xpFrom ? "—" : paidBetween(inRange, "steps", w.from, w.to),
                  ],
                }))}
            />
          ) : (
            <MetricTable
              label="Steps by day, as a table"
              caption={`Steps by day, last ${label}, newest first`}
              columns={[
                { key: "day", label: "Day" },
                { key: "steps", label: "Steps", numeric: true },
                { key: "band", label: "Band" },
                { key: "average", label: "7-day average", numeric: true },
                { key: "ring", label: "Without the ring", numeric: true },
                { key: "xp", label: "Health XP", numeric: true },
              ]}
              rows={[...shown].reverse().map(({ day, average }) => ({
                key: day.day,
                cells: [
                  day.day === today ? "Today" : formatWeekday(day.day),
                  day.steps != null ? `${day.steps.toLocaleString()}${day.day === today ? " so far" : ""}` : "—",
                  bandCell(day.steps, targets),
                  average != null ? average.toLocaleString() : "—",
                  day.nonWearS != null && day.nonWearS >= HIGH_NON_WEAR_S ? formatDuration(day.nonWearS) : "—",
                  <XpCell key="xp" part={dayPartXp(day, "steps")} />,
                ],
              }))}
            />
          )}
        </div>
      ) : null}
    </ActivityCard>
  );
}
