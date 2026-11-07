import { useId, useState } from "react";
import { PencilIcon, TargetIcon, Trash2Icon } from "lucide-react";
import { toast } from "sonner";
import { MetricTable } from "@/components/charts/daily-metric-chart";
import { TrendChart, type TrendChartPoint } from "@/components/charts/trend-chart";
import { GoalDialog } from "@/components/measurements/goal-dialog";
import { DeleteReadingDialog } from "@/components/measurements/log-reading-sheet";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Button } from "@/components/ui/button";
import { dayTitle } from "@/lib/activity";
import { formatDay } from "@/lib/dates";
import {
  RANGES,
  formatValue,
  goalText,
  rangeDays,
  readingMessage,
  readingWhen,
  withUnit,
  type MetricRange,
} from "@/lib/measurements";
import type { MetricDetailRead, MetricReadingRow, MetricRow } from "@/lib/production-ui-types";
import { useMetric, useWrites } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const VIEW = "measurements.weight";

/** The chart's points: each day's value and trend, read out with how many weigh-ins it had. */
function chartPoints(read: MetricDetailRead): TrendChartPoint[] {
  const perDay = new Map<string, number>();
  for (const r of read.readings) perDay.set(r.takenOn, (perDay.get(r.takenOn) ?? 0) + 1);
  return read.days.map((d) => {
    const count = perDay.get(d.day) ?? 1;
    return {
      day: d.day,
      value: d.value,
      trend: d.trend,
      title: dayTitle(d.day, read.today),
      text: `${withUnit(d.value, read.metric)}, trend ${withUnit(d.trend, read.metric)}`,
      detail: count > 1 ? `${count} weigh-ins: the first counts` : undefined,
    };
  });
}

/**
 * The Weight card (MEASURE-1, MEASURE-2, measurements plan §4.7): the trend now and the last
 * weigh-in, the goal date in words (§2.3), and the chart — a dot each day, the smoothed trend,
 * the dashed goal — over 30 days, 90 days, a year or all of it, or as a table of every weigh-in
 * with Change and Delete. Logging and changing open the page's sheet.
 */
export function WeightCard({
  metric,
  onLog,
  onChange,
}: {
  metric: MetricRow;
  onLog: () => void;
  onChange: (reading: MetricReadingRow) => void;
}) {
  const titleId = useId();
  const writes = useWrites();
  const [range, setRangeState] = useState<MetricRange>(() => loadView<{ range: MetricRange }>(VIEW, { range: "90" }).range);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const [goalOpen, setGoalOpen] = useState(false);
  const [deleting, setDeleting] = useState<MetricReadingRow | null>(null);
  const read = useMetric(metric.id, rangeDays(range));

  const setRange = (next: MetricRange) => {
    setRangeState(next);
    try {
      saveView(VIEW, { range: next });
    } catch {
      // Storage can be full or blocked; the range still applies to this view.
    }
  };

  const data = read.data;
  const today = data?.today ?? "";
  const rangeLabel = range === "all" ? "all time" : `last ${RANGES.find((r) => r.id === range)!.label}`;
  const trendNow = data ? (data.days.at(-1)?.trend ?? data.goal.now) : null;
  const last = data?.metric.lastReading ?? metric.lastReading;
  const goal = data && data.metric.readings > 0 ? goalText(data.goal, data.metric, today, "weigh-in") : null;
  // When the whole history fits in the range, the axis starts at the first day rather than
  // leaving the range's empty start; otherwise at the range's start, so a gap shows as one.
  const chartFrom =
    data && (data.from == null || data.metric.readings === data.readings.length)
      ? (data.days[0]?.day ?? today)
      : (data?.from ?? today);
  const counted = new Set(data?.days.map((d) => d.readingId));
  const trendOf = new Map(data?.days.map((d) => [d.readingId, d.trend]));

  async function remove(reading: MetricReadingRow) {
    try {
      await writes.deleteReading(reading.id);
      toast.success(readingMessage("deleted", null, metric.decimals));
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "That didn't delete. Nothing was written.");
    }
  }

  return (
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
        <div>
          <h2 id={titleId} className="font-heading text-xl">
            {metric.name}
          </h2>
          {trendNow != null ? (
            <p className="flex flex-wrap items-baseline gap-x-2">
              <span className="font-heading text-4xl tabular-nums" data-testid="weight-trend">
                {withUnit(trendNow, metric)}
              </span>
              <span className="text-sm text-muted-foreground">trend</span>
            </p>
          ) : null}
          <p className="text-xs text-muted-foreground" data-testid="weight-last">
            {last
              ? `Last weigh-in ${withUnit(last.value, metric)} · ${readingWhen(last, today || last.takenOn)}`
              : "No weigh-ins yet: log your first to start the trend."}
          </p>
        </div>
        <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-0" onClick={() => setGoalOpen(true)}>
          <TargetIcon />
          {metric.target != null ? "Change goal" : "Set a goal"}
        </Button>
      </div>

      {goal ? (
        <p className="text-sm" data-testid="weight-goal">
          {goal}
        </p>
      ) : null}

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

      {read.isLoading ? <ListSkeleton rows={3} /> : null}
      {read.isError ? (
        <p role="alert" className="text-sm text-destructive">
          {metric.name}'s weigh-ins failed to load.
        </p>
      ) : null}

      {data ? (
        <div
          aria-busy={read.isPlaceholderData || undefined}
          className={cn("transition-opacity", read.isPlaceholderData && "opacity-60")}
        >
          {data.readings.length === 0 ? (
            <div className="space-y-3 rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              <p>{data.metric.readings === 0 ? "Nothing logged yet." : `No weigh-ins in the ${rangeLabel}.`}</p>
              <Button type="button" variant="outline" size="sm" onClick={onLog}>
                Log {metric.name.toLowerCase()}
              </Button>
            </div>
          ) : mode === "chart" ? (
            <TrendChart
              label={`${metric.name} by day, ${rangeLabel}`}
              points={chartPoints(data)}
              from={chartFrom}
              to={today}
              target={
                data.metric.target != null
                  ? { value: data.metric.target, label: `Goal ${withUnit(data.metric.target, data.metric)}` }
                  : undefined
              }
              formatValue={(n) => formatValue(n, n % 1 === 0 ? 0 : metric.decimals)}
              valueLabel="Each day's first weigh-in"
              axisLabel={(day) => (day === today ? "Today" : formatDay(day))}
            />
          ) : (
            <MetricTable
              label={`${metric.name} weigh-ins, as a table`}
              caption={`${metric.name} weigh-ins, ${rangeLabel}, newest first`}
              columns={[
                { key: "when", label: "When" },
                { key: "value", label: metric.name, numeric: true },
                { key: "trend", label: "Trend", numeric: true },
                { key: "actions", label: "Change" },
              ]}
              rows={data.readings.map((r) => {
                const when = readingWhen(r, today);
                const what = `${withUnit(r.value, metric)} · ${when}`;
                return {
                  key: r.id,
                  cells: [
                    <span key="when" className="block">
                      {when}
                      {r.note ? <span className="block text-xs text-muted-foreground">{r.note}</span> : null}
                      {!counted.has(r.id) ? (
                        <span className="block text-xs text-muted-foreground">Not the day's first</span>
                      ) : null}
                    </span>,
                    withUnit(r.value, metric),
                    trendOf.has(r.id) ? withUnit(trendOf.get(r.id)!, metric) : "—",
                    <span key="actions" className="flex gap-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={`Change ${what}`}
                        onClick={() => onChange(r)}
                      >
                        <PencilIcon />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={`Delete ${what}`}
                        onClick={() => setDeleting(r)}
                      >
                        <Trash2Icon />
                      </Button>
                    </span>,
                  ],
                };
              })}
            />
          )}
        </div>
      ) : null}

      <GoalDialog open={goalOpen} onOpenChange={setGoalOpen} metric={data?.metric ?? metric} />
      <DeleteReadingDialog
        open={deleting != null}
        onOpenChange={(open) => {
          if (!open) setDeleting(null);
        }}
        what={deleting ? `${withUnit(deleting.value, metric)} · ${readingWhen(deleting, today)}` : ""}
        noun="weigh-in"
        onDelete={() => {
          if (deleting) void remove(deleting);
          setDeleting(null);
        }}
      />
    </section>
  );
}
