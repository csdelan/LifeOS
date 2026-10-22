import { useId, useState } from "react";
import { MoonIcon } from "lucide-react";
import { ScoreCell, ScoreFigure, StageBar, StatTile } from "@/components/activity/sleep-parts";
import {
  DailyMetricChart,
  MetricTable,
  type MetricBar,
  type MetricLegendItem,
  type MetricReference,
} from "@/components/charts/daily-metric-chart";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Button } from "@/components/ui/button";
import {
  bedToWake,
  dayName,
  formatDuration,
  formatHours,
  formatWeekday,
  METRIC_RANGES,
  metricWeeks,
  OURA_SCORE_LINES,
  ouraScoreWords,
  rangeFrom,
  rangeStart,
  rollingMean,
  SCORE_TONE,
  scoreBand,
  SLEEP_METRICS,
  sleepDayText,
  beforeStart,
  BEFORE_START,
  sleepValue,
  sleepValueWords,
  weekTitle,
  type MetricRange,
  type SleepMetric,
} from "@/lib/activity";
import { formatDay } from "@/lib/dates";
import type { ActivitySummaryRead, ActivityWeekRow } from "@/lib/production-ui-types";
import { useActivityDay, useActivityDays } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const VIEW = "activity.sleep";

type SleepView = { range: MetricRange; metric: SleepMetric };

/** A score's tone, or Time asleep's one tone. */
function tone(value: number, metric: SleepMetric): string {
  return metric === "asleep" ? "bg-sleep-time" : SCORE_TONE[scoreBand(value)];
}

function weekNote(thisWeek: number | null, lastWeek: number | null): string {
  const band = thisWeek != null ? `${ouraScoreWords(thisWeek)} · ` : "";
  return `${band}${lastWeek != null ? `last week ${lastWeek}` : "nothing last week"}`;
}

/**
 * Last night in full (activity plan §3.7): both scores with Oura's band, time asleep against
 * time in bed, bed and wake, the stages, HRV and the lowest heart rate; "Last night in full"
 * opens the day.
 */
function LastNight({ today, onOpenDay }: { today: string; onOpenDay: (day: string) => void }) {
  const detail = useActivityDay(today);

  if (detail.isLoading) return <ListSkeleton rows={2} />;
  if (detail.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        Last night failed to load.
      </p>
    );
  }
  const data = detail.data;
  if (!data) return null;
  const { summary: day, night } = data;
  if (day.sleepScore == null && day.readinessScore == null && night == null) {
    return (
      <p className="flex items-center gap-2 text-sm text-muted-foreground">
        <MoonIcon className="size-4" aria-hidden />
        Open the Oura app to sync last night.
      </p>
    );
  }

  return (
    <div className="space-y-3">
      <dl className="grid grid-cols-3 gap-3">
        <ScoreFigure label="Sleep score" value={day.sleepScore} large />
        <ScoreFigure label="Readiness" value={day.readinessScore} large />
        <div>
          <dt className="text-xs text-muted-foreground">Asleep</dt>
          <dd className="text-2xl font-semibold tabular-nums">{day.asleepS != null ? formatDuration(day.asleepS) : "—"}</dd>
          {day.inBedS != null ? (
            <dd className="text-xs text-muted-foreground">of {formatDuration(day.inBedS)} in bed</dd>
          ) : null}
        </div>
      </dl>
      {night ? (
        <>
          <StageBar period={night} />
          <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-xs text-muted-foreground">Bed to wake</dt>
              <dd className="tabular-nums">{bedToWake(night)}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Average HRV</dt>
              <dd className="tabular-nums">{night.averageHrv != null ? `${night.averageHrv} ms` : "—"}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Lowest heart rate</dt>
              <dd className="tabular-nums">{night.lowestHr != null ? `${night.lowestHr} bpm` : "—"}</dd>
            </div>
          </dl>
        </>
      ) : null}
      <Button variant="outline" size="sm" onClick={() => onOpenDay(today)}>
        Last night in full
      </Button>
    </div>
  );
}

function WeekTiles({ thisWeek, lastWeek }: { thisWeek: ActivityWeekRow; lastWeek: ActivityWeekRow }) {
  return (
    <dl className="grid grid-cols-2 gap-2">
      <StatTile
        label="Sleep score, this week"
        value={thisWeek.averageSleep != null ? String(thisWeek.averageSleep) : "—"}
        note={weekNote(thisWeek.averageSleep, lastWeek.averageSleep)}
      />
      <StatTile
        label="Readiness, this week"
        value={thisWeek.averageReadiness != null ? String(thisWeek.averageReadiness) : "—"}
        note={weekNote(thisWeek.averageReadiness, lastWeek.averageReadiness)}
      />
    </dl>
  );
}

/**
 * Sleep and readiness (ACTIVITY-4, activity plan §3.7): last night in full, this week against
 * last, and a chart of the sleep score, readiness or time asleep over 30 days, 90 days or a year
 * (a bar per week), with the 7-day average and, on the scores, Oura's lines at 70 and 85. A
 * Table lists every day; a day opens in full from either.
 */
export function SleepCard({
  summary,
  onOpenDay,
}: {
  summary: ActivitySummaryRead;
  onOpenDay: (day: string) => void;
}) {
  const [view, setViewState] = useState<SleepView>(() => loadView<SleepView>(VIEW, { range: "30", metric: "sleep" }));
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const titleId = useId();
  const today = summary.today;
  const start = summary.oura.startDate;
  const { range, metric } = view;
  const days = useActivityDays(rangeFrom(range, today), today);

  const setView = (patch: Partial<SleepView>) => {
    const next = { ...view, ...patch };
    setViewState(next);
    try {
      saveView(VIEW, next);
    } catch {
      // Storage can be full or blocked; the choice still applies to this view.
    }
  };

  const all = days.data?.days ?? [];
  const values = all.map((d) => sleepValue(d, metric));
  const averages = rollingMean(values);
  const yearly = range === "365";
  const first = rangeStart(range, today);
  const shown = all.map((d, i) => ({ day: d, average: averages[i] })).filter(({ day }) => day.day >= first);
  const rangeLabel = METRIC_RANGES.find((r) => r.id === range)!.label;
  const metricLabel = SLEEP_METRICS.find((m) => m.id === metric)!.label;
  const scores = metric !== "asleep";

  const references: MetricReference[] = scores
    ? [
        { value: OURA_SCORE_LINES.good, label: "Good", pattern: "dotted" },
        { value: OURA_SCORE_LINES.optimal, label: "Optimal", pattern: "dashed" },
      ]
    : [];
  const bandLegend: MetricLegendItem[] = scores
    ? [
        { kind: "bar", tone: SCORE_TONE.attention, label: "Pay attention" },
        { kind: "bar", tone: SCORE_TONE.good, label: "Good" },
        { kind: "bar", tone: SCORE_TONE.optimal, label: "Optimal" },
      ]
    : [{ kind: "bar", tone: "bg-sleep-time", label: "Time asleep" }];
  const lineLegend: MetricLegendItem[] = scores
    ? [
        { kind: "dotted", label: `${OURA_SCORE_LINES.good} good` },
        { kind: "dashed", label: `${OURA_SCORE_LINES.optimal} optimal` },
      ]
    : [];

  const hoursTop = Math.max(10, Math.ceil(Math.max(0, ...values.filter((v): v is number => v != null)) * 1.1));
  const unit = (v: number) => sleepValueWords(v, metric);

  let bars: MetricBar[];
  let legend: MetricLegendItem[];
  const weeks = yearly
    ? metricWeeks(
        shown.map(({ day }) => day),
        today,
        (d) => sleepValue(d, metric),
      )
    : [];
  if (yearly) {
    bars = weeks.map((w) => ({
      key: w.from,
      value: w.average,
      tone: w.average != null ? tone(w.average, metric) : "",
      partial: w.partial,
      title: weekTitle(w.from, today),
      text:
        w.average == null
          ? beforeStart(w.to, start)
            ? BEFORE_START
            : "nothing recorded"
          : scores
            ? `${metricLabel.toLowerCase()} ${Math.round(w.average)} on average${w.partial ? " so far" : ""}, ${ouraScoreWords(w.average).toLowerCase()}`
            : `${formatHours(w.average)} asleep a night${w.partial ? " so far" : ""}`,
      detail: w.nights > 0 ? `over ${w.nights} night${w.nights === 1 ? "" : "s"}` : undefined,
      axis: w.partial ? "This week" : formatDay(w.from),
    }));
    legend = [...bandLegend, { kind: "so-far", tone: scores ? SCORE_TONE.good : "bg-sleep-time", label: "This week, so far" }, ...lineLegend];
  } else {
    bars = shown.map(({ day, average }) => {
      const value = sleepValue(day, metric);
      return {
        key: day.day,
        value,
        tone: value != null ? tone(value, metric) : "",
        title: dayName(day.day, today),
        text: sleepDayText(day, metric, today, start),
        detail: average != null ? `7-day average ${unit(average)}` : undefined,
        axis: day.day === today ? "Today" : formatDay(day.day),
      };
    });
    legend = [...bandLegend, { kind: "line", label: "7-day average" }, ...lineLegend];
  }

  // The year's Table lists all three measures by week, newest first.
  const weekRows = yearly
    ? (() => {
        const shownDays = shown.map(({ day }) => day);
        const [sleep, readiness, asleep] = (["sleep", "readiness", "asleep"] as const).map((m) =>
          metricWeeks(shownDays, today, (d) => sleepValue(d, m)),
        );
        const rounded = (v: number | null) => (v != null ? Math.round(v) : null);
        return sleep
          .map((w, i) => ({
            key: w.from,
            cells: [
              `${weekTitle(w.from, today)}${w.partial ? ", so far" : ""}`,
              <ScoreCell key="s" value={rounded(w.average)} />,
              <ScoreCell key="r" value={rounded(readiness[i].average)} />,
              asleep[i].average != null ? formatHours(asleep[i].average) : "—",
            ],
          }))
          .reverse();
      })()
    : [];

  return (
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id={titleId} className="type-scale-section text-muted-foreground">
        Sleep and readiness
      </h2>

      <div className="space-y-2">
        <h3 className="text-sm font-medium">Last night</h3>
        <LastNight today={today} onOpenDay={onOpenDay} />
      </div>

      <WeekTiles thisWeek={summary.thisWeek} lastWeek={summary.lastWeek} />

      <div className="flex flex-wrap items-center gap-3 border-t pt-3">
        <ToggleGroup label="Range" value={range} onChange={(r) => setView({ range: r })} options={[...METRIC_RANGES]} />
        <ToggleGroup
          label="Show as"
          value={mode}
          onChange={setMode}
          options={[
            { id: "chart", label: "Chart" },
            { id: "table", label: "Table" },
          ]}
        />
        {mode === "chart" ? (
          <ToggleGroup label="Measure" value={metric} onChange={(m) => setView({ metric: m })} options={SLEEP_METRICS} />
        ) : null}
      </div>

      {days.isLoading ? <ListSkeleton rows={3} /> : null}
      {days.isError ? (
        <p role="alert" className="text-sm text-destructive">
          Sleep and readiness failed to load.
        </p>
      ) : null}

      {days.data ? (
        <div
          aria-busy={days.isPlaceholderData || undefined}
          className={cn("space-y-2 transition-opacity", days.isPlaceholderData && "opacity-60")}
        >
          {mode === "chart" ? (
            <DailyMetricChart
              label={yearly ? `${metricLabel}, average a night by week, last year` : `${metricLabel} by day, last ${rangeLabel}`}
              bars={bars}
              average={yearly ? undefined : shown.map(({ average }) => average)}
              references={references}
              legend={legend}
              top={scores ? 100 : hoursTop}
              formatValue={scores ? undefined : (n) => `${n} h`}
              onSelect={yearly ? undefined : (i) => onOpenDay(shown[i].day.day)}
            />
          ) : yearly ? (
            <MetricTable
              label="Sleep and readiness by week, as a table"
              caption="Sleep and readiness, average a night by week, last year, newest first"
              columns={[
                { key: "week", label: "Week" },
                { key: "sleep", label: "Sleep score", numeric: true },
                { key: "readiness", label: "Readiness", numeric: true },
                { key: "asleep", label: "Asleep", numeric: true },
              ]}
              rows={weekRows}
            />
          ) : (
            <MetricTable
              label="Sleep and readiness by day, as a table"
              caption={`Sleep and readiness by day, last ${rangeLabel}, newest first`}
              columns={[
                { key: "day", label: "Day" },
                { key: "sleep", label: "Sleep score", numeric: true },
                { key: "readiness", label: "Readiness", numeric: true },
                { key: "asleep", label: "Asleep", numeric: true },
              ]}
              rows={[...shown].reverse().map(({ day }) => ({
                key: day.day,
                cells: [
                  <button
                    key="day"
                    type="button"
                    className="text-left underline-offset-2 hover:underline focus-visible:underline"
                    onClick={() => onOpenDay(day.day)}
                  >
                    {day.day === today ? "Today" : formatWeekday(day.day)}
                  </button>,
                  <ScoreCell key="s" value={day.sleepScore} />,
                  <ScoreCell key="r" value={day.readinessScore} />,
                  day.asleepS != null ? formatDuration(day.asleepS) : "—",
                ],
              }))}
            />
          )}
          <p className="text-xs text-muted-foreground">
            A night counts on the morning it ends.{yearly ? "" : " Choose a day to see it in full."}
          </p>
        </div>
      ) : null}
    </section>
  );
}
