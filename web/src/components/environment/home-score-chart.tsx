import { useId, useState } from "react";
import {
  DailyMetricChart,
  MetricTable,
  type MetricBar,
  type MetricLegendItem,
} from "@/components/charts/daily-metric-chart";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { dayName, formatWeekday } from "@/lib/activity";
import { formatDay } from "@/lib/dates";
import { barPlus } from "@/lib/domains";
import { shownScore } from "@/lib/environment";
import { useEnvironmentDays } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const RANGES = [
  { id: "30", label: "30 days" },
  { id: "90", label: "90 days" },
  { id: "365", label: "1 year" },
] as const;
type Range = (typeof RANGES)[number]["id"];

const VIEW = "environment.chart";

/**
 * The bar each day had to reach to be kept up, for the level going into it (environment plan
 * §2.3): drawn as a dashed step line over the scores, and shown rounded up.
 */
export interface LevelBarLine {
  /** The legend's words: "The bar for your level". */
  label: string;
  /** The Home Score each day needed, in points, by day. */
  byDay: Record<string, number>;
}

/**
 * The Home Score by day (HOME-3) over 30 days, 90 days or a year, with exception days hatched
 * (their fades paused) and a Table view of every day.
 */
export function HomeScoreChart({ levelBar }: { levelBar?: LevelBarLine }) {
  const titleId = useId();
  const [range, setRangeState] = useState<Range>(() => loadView<{ range: Range }>(VIEW, { range: "30" }).range);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const read = useEnvironmentDays(Number(range));

  const setRange = (next: Range) => {
    setRangeState(next);
    try {
      saveView(VIEW, { range: next });
    } catch {
      // Storage can be full or blocked; the range still applies to this view.
    }
  };

  const today = read.data?.today ?? "";
  const days = read.data?.days ?? [];
  const label = RANGES.find((r) => r.id === range)!.label;

  const bars: MetricBar[] = days.map((d) => ({
    key: d.day,
    value: d.homeScore,
    tone: "bg-chart-earned",
    marked: d.exception != null,
    title: dayName(d.day, today),
    text:
      (d.homeScore == null ? "no Home Score yet" : `Home Score ${shownScore(d.homeScore)}`) +
      (levelBar?.byDay[d.day] != null ? `, bar ${barPlus(levelBar.byDay[d.day])}` : ""),
    detail: d.exception != null ? `Exception: ${d.exception}` : undefined,
    axis: d.day === today ? "Today" : formatDay(d.day),
  }));
  const steps = levelBar ? days.map((d) => levelBar.byDay[d.day] ?? null) : undefined;
  const legend: MetricLegendItem[] = [
    { kind: "bar", tone: "bg-chart-earned", label: "Home Score" },
    ...(days.some((d) => d.exception != null) ? [{ kind: "marked" as const, label: "Exception day: fades paused" }] : []),
    ...(levelBar ? [{ kind: "dashed" as const, label: levelBar.label }] : []),
  ];

  return (
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
        <h2 id={titleId} className="font-heading text-xl">
          Home Score by day
        </h2>
        <div className="flex flex-wrap items-center gap-3">
          <ToggleGroup label="Range" value={range} onChange={setRange} options={[...RANGES]} />
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
      </div>

      {read.isLoading ? <ListSkeleton rows={3} /> : null}
      {read.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The Home Score's days failed to load.
        </p>
      ) : null}

      {read.data ? (
        <div
          aria-busy={read.isPlaceholderData || undefined}
          className={cn("transition-opacity", read.isPlaceholderData && "opacity-60")}
        >
          {mode === "chart" ? (
            <DailyMetricChart
              label={`Home Score by day, last ${label}`}
              bars={bars}
              steps={steps}
              top={100}
              formatValue={(n) => String(Math.round(n))}
              legend={legend}
            />
          ) : (
            <MetricTable
              label="Home Score by day, as a table"
              caption={`Home Score by day, last ${label}, newest first`}
              columns={[
                { key: "day", label: "Day" },
                { key: "score", label: "Home Score", numeric: true },
                ...(levelBar ? [{ key: "bar", label: "Bar", numeric: true }] : []),
                { key: "exception", label: "Exception" },
              ]}
              rows={[...days].reverse().map((d) => ({
                key: d.day,
                cells: [
                  d.day === today ? "Today" : formatWeekday(d.day),
                  shownScore(d.homeScore),
                  ...(levelBar ? [levelBar.byDay[d.day] != null ? barPlus(levelBar.byDay[d.day]) : "—"] : []),
                  d.exception ?? "",
                ],
              }))}
            />
          )}
        </div>
      ) : null}
    </section>
  );
}
