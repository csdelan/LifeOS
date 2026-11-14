import { useId, useState } from "react";
import { PencilIcon, SlidersHorizontalIcon, Trash2Icon } from "lucide-react";
import { toast } from "sonner";
import { MetricTable } from "@/components/charts/daily-metric-chart";
import { TrendChart, type TrendChartReadout, type TrendChartSeries } from "@/components/charts/trend-chart";
import { DeleteReadingDialog } from "@/components/measurements/log-reading-sheet";
import { MetricSettingsDialog } from "@/components/measurements/metric-settings-dialog";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { dayTitle } from "@/lib/activity";
import { formatDay } from "@/lib/dates";
import { resultRange } from "@/lib/features/measurements/labs";
import {
  RANGES,
  flagOf,
  flagsText,
  formatValue,
  goalText,
  groupRangeText,
  groupValue,
  lastSet,
  nameInText,
  rangeDays,
  rangeText,
  readingSets,
  readingWhen,
  setText,
  type MetricGroup,
  type MetricRange,
  type ReadingSet,
} from "@/lib/features/measurements/measurements";
import type { MetricDayRow, MetricDetailRead, MetricFlag } from "@/lib/features/measurements/types";
import { useWrites } from "@/lib/queries";
import { useMetricsOver } from "@/lib/features/measurements/queries";
import { usePersistedChoice } from "@/lib/view-state";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";
import { ReadFailed } from "@/components/primitives/read-state";

/**
 * A day's flag: its reading's, which for a lab result follows the range printed beside it (§4.9);
 * against the metric's range as it is now when the reading isn't in the read.
 */
function dayFlag(day: MetricDayRow, read: MetricDetailRead): MetricFlag | null {
  const reading = read.readings.find((r) => r.id === day.readingId);
  return reading ? reading.flag : flagOf(day.value, read.metric);
}

/** The chart's series: each metric's day values and trend, flagged against its range, its range shaded. */
function chartSeries(group: MetricGroup, reads: MetricDetailRead[]): TrendChartSeries[] {
  return reads.map((read, i) => ({
    key: read.metric.id,
    label: group.metrics.length === 1 ? `Each day's first ${group.noun}` : group.labels[i],
    points: read.days.map((d) => ({ day: d.day, value: d.value, trend: d.trend, flagged: dayFlag(d, read) != null })),
    band: read.metric.low != null || read.metric.high != null ? { low: read.metric.low, high: read.metric.high } : undefined,
  }));
}

/** A day in words across the group: "122/78 mmHg (high), trend 119/77 mmHg", and how many sets it had. */
function chartReadout(group: MetricGroup, reads: MetricDetailRead[], sets: ReadingSet[]) {
  const byDay = reads.map((read) => new Map(read.days.map((d) => [d.day, d])));
  const perDay = new Map<string, number>();
  for (const set of sets) {
    const day = set.find((r) => r != null)!.takenOn;
    perDay.set(day, (perDay.get(day) ?? 0) + 1);
  }
  const today = reads[0].today;
  return (day: string): TrendChartReadout => {
    const days = byDay.map((m) => m.get(day));
    const flags = flagsText(days.map((d, i) => (d ? { flag: dayFlag(d, reads[i]) } : null)));
    const count = perDay.get(day) ?? 1;
    return {
      title: dayTitle(day, today),
      text: `${groupValue(group, days.map((d) => d?.value ?? null))}${flags ? ` (${flags.toLowerCase()})` : ""}, trend ${groupValue(
        group,
        days.map((d) => d?.trend ?? null),
      )}`,
      detail: count > 1 ? `${count} ${group.noun}s: the first counts` : undefined,
    };
  };
}

/**
 * A metric's card (MEASURE-1 to MEASURE-3, measurements plan §4.7–§4.8), or blood pressure's two
 * as one: the trend now and the newest reading, the goal date in words (§2.3) and the normal
 * range, and the chart — a dot each day, the smoothed trend, the dashed goal, the range shaded
 * with a reading outside it hollow — over 30 days, 90 days, a year or all of it, or as a table
 * of every reading (each pair one row) with Change and Delete. Edit changes the metric or
 * archives it. As `card` it stands alone with its own heading (Weight, at the top of the page);
 * `inline` is a row's opened body, under the row's heading. A lab test's card (§4.9) leads with
 * its latest result, shades its newest report's range, and its table gives each result's range;
 * a result's Change opens its visit, where it's deleted too.
 */
export function MetricCard({
  group,
  layout,
  onLog,
  onChange,
}: {
  group: MetricGroup;
  layout: "card" | "inline";
  onLog: () => void;
  onChange: (set: ReadingSet) => void;
}) {
  const titleId = useId();
  const writes = useWrites();
  const removing = useWriteAction({ report: "toast", fallback: "That didn't delete. Nothing was written." });
  const view = `measurements.${group.key}`;
  // Labs come months apart, so a lab test opens on all of it; any other metric on 90 days.
  const [range, setRange] = usePersistedChoice<MetricRange>(
    view,
    "range",
    RANGES,
    group.metrics[0].panel != null ? "all" : "90",
  );
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const [editOpen, setEditOpen] = useState(false);
  const [deleting, setDeleting] = useState<ReadingSet | null>(null);
  const read = useMetricsOver(
    group.metrics.map((m) => m.id),
    rangeDays(range),
  );

  const reads = read.data;
  // The metrics as the reads have them (fresher than the list's), in the group's order.
  const fresh: MetricGroup = reads ? { ...group, metrics: reads.map((r) => r.metric) } : group;
  const today = reads?.[0].today ?? "";
  const tid = group.key;
  const rangeLabel = range === "all" ? "all time" : `last ${RANGES.find((r) => r.id === range)!.label}`;
  const trends = reads?.map((r) => r.days.at(-1)?.trend ?? r.goal.now) ?? null;
  const trendNow = trends && trends.some((t) => t != null) ? groupValue(fresh, trends) : null;
  const last = lastSet(fresh);
  const lastFlags = last ? flagsText(last) : null;
  const goals = reads
    ? reads
        .map((r) => (r.metric.readings > 0 ? goalText(r.goal, r.metric, today, group.noun) : null))
        .map((text, i) => (text && group.metrics.length > 1 ? `${group.labels[i]}: ${text}` : text))
        .filter((text): text is string => text != null)
    : [];
  const lab = fresh.metrics[0].panel != null;
  const labRange = lab ? rangeText(fresh.metrics[0]) : null;
  const normal = lab ? (labRange ? `Newest report's range: ${labRange}` : null) : groupRangeText(fresh);
  const sets = reads ? readingSets(reads.map((r) => r.readings)) : [];
  const total = reads?.reduce((n, r) => n + r.metric.readings, 0) ?? 0;
  const shown = reads?.reduce((n, r) => n + r.readings.length, 0) ?? 0;
  // When the whole history fits in the range, the axis starts at the first day rather than
  // leaving the range's empty start; otherwise at the range's start, so a gap shows as one.
  const firstDay = reads ? reads.flatMap((r) => r.days.map((d) => d.day)).sort()[0] : undefined;
  const chartFrom = reads && (reads[0].from == null || total === shown) ? (firstDay ?? today) : (reads?.[0].from ?? today);
  const counted = reads?.map((r) => new Set(r.days.map((d) => d.readingId))) ?? [];
  const trendOf = reads?.map((r) => new Map(r.days.map((d) => [d.readingId, d.trend]))) ?? [];
  const noun = group.noun;

  async function remove(set: ReadingSet) {
    await removing.run(
      async () => {
        for (const r of set) if (r) await writes.deleteReading(r.id);
      },
      () => toast.success("Deleted the reading"),
    );
  }

  const edit = (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="min-h-11 md:min-h-0"
      aria-label={`Edit ${group.name}`}
      onClick={() => setEditOpen(true)}
    >
      <SlidersHorizontalIcon />
      Edit
    </Button>
  );

  return (
    <section
      aria-labelledby={layout === "card" ? titleId : undefined}
      aria-label={layout === "inline" ? group.name : undefined}
      className={cn("space-y-4", layout === "card" && "rounded-xl bg-card p-4 ring-1 ring-foreground/10")}
    >
      {layout === "card" ? (
        <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
          <div>
            <h2 id={titleId} className="font-heading text-xl">
              {group.name}
            </h2>
            {trendNow != null ? (
              <p className="flex flex-wrap items-baseline gap-x-2">
                <span className="font-heading text-4xl tabular-nums" data-testid={`${tid}-trend`}>
                  {trendNow}
                </span>
                <span className="text-sm text-muted-foreground">trend</span>
              </p>
            ) : null}
            <p className="text-xs text-muted-foreground" data-testid={`${tid}-last`}>
              {last
                ? `Last ${noun} ${setText(fresh, last, today)}${lastFlags ? ` (${lastFlags.toLowerCase()})` : ""}`
                : `No ${noun}s yet: log your first to start the trend.`}
            </p>
          </div>
          {edit}
        </div>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
          <p className="text-sm">
            {lab && last ? (
              <>
                <span className="text-muted-foreground">Latest </span>
                <span className="font-semibold tabular-nums" data-testid={`${tid}-latest`}>
                  {setText(fresh, last, today)}
                </span>
                {lastFlags ? <span className="text-muted-foreground"> ({lastFlags.toLowerCase()})</span> : null}
              </>
            ) : trendNow != null ? (
              <>
                <span className="text-muted-foreground">Trend </span>
                <span className="font-semibold tabular-nums" data-testid={`${tid}-trend`}>
                  {trendNow}
                </span>
              </>
            ) : (
              <span className="text-muted-foreground">
                {lab ? "No results yet." : `No ${noun}s yet: log your first to start the trend.`}
              </span>
            )}
          </p>
          {edit}
        </div>
      )}

      {goals.length > 0 || normal ? (
        <div className="space-y-1 text-sm">
          {goals.length > 0 ? (
            <div data-testid={`${tid}-goal`}>
              {goals.map((text) => (
                <p key={text}>{text}</p>
              ))}
            </div>
          ) : null}
          {normal ? (
            <p className="text-muted-foreground" data-testid={`${tid}-range`}>
              {normal}
            </p>
          ) : null}
        </div>
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
      <ReadFailed read={read} what={`${group.name}'s ${noun}s`} />

      {reads ? (
        <div
          aria-busy={read.isPlaceholderData || undefined}
          className={cn("transition-opacity", read.isPlaceholderData && "opacity-60")}
        >
          {sets.length === 0 ? (
            <div className="space-y-3 rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              <p>{total === 0 ? "Nothing logged yet." : `No ${noun}s in the ${rangeLabel}.`}</p>
              <Button type="button" variant="outline" size="sm" onClick={onLog}>
                {lab ? "Log a lab visit" : `Log ${nameInText(group.name)}`}
              </Button>
            </div>
          ) : mode === "chart" ? (
            <TrendChart
              label={`${group.name} by day, ${rangeLabel}`}
              series={chartSeries(group, reads)}
              readout={chartReadout(fresh, reads, sets)}
              from={chartFrom}
              to={today}
              target={
                group.metrics.length === 1 && reads[0].metric.target != null
                  ? { value: reads[0].metric.target, label: `Goal ${groupValue(fresh, [reads[0].metric.target])}` }
                  : undefined
              }
              formatValue={(n) => formatValue(n, n % 1 === 0 ? 0 : fresh.metrics[0].decimals)}
              axisLabel={(day) => (day === today ? "Today" : formatDay(day))}
            />
          ) : (
            <MetricTable
              label={`${group.name} ${noun}s, as a table`}
              caption={`${group.name} ${noun}s, ${rangeLabel}, newest first`}
              columns={[
                { key: "when", label: "When" },
                { key: "value", label: group.name, numeric: true },
                lab ? { key: "range", label: "Range" } : { key: "trend", label: "Trend", numeric: true },
                { key: "actions", label: "Change" },
              ]}
              rows={sets.map((set) => {
                const taken = set.find((r) => r != null)!;
                const when = readingWhen(taken, today);
                const what = setText(fresh, set, today);
                const note = set.find((r) => r?.note)?.note;
                const flags = flagsText(set);
                const firsts = set.every((r, i) => r == null || counted[i].has(r.id));
                const trend = set.map((r, i) => (r ? (trendOf[i].get(r.id) ?? null) : null));
                const inVisit = set.some((r) => r?.visitId != null);
                return {
                  key: taken.id,
                  cells: [
                    <span key="when" className="block">
                      {when}
                      {note ? <span className="block text-xs text-muted-foreground">{note}</span> : null}
                      {!firsts ? <span className="block text-xs text-muted-foreground">Not the day's first</span> : null}
                    </span>,
                    <span key="value" className="inline-flex flex-wrap items-center justify-end gap-1.5">
                      {groupValue(fresh, set.map((r) => r?.value ?? null))}
                      {flags ? <Badge variant="outline">{flags}</Badge> : null}
                    </span>,
                    lab
                      ? (resultRange({ ...taken, unit: "" }, fresh.metrics[0].decimals) ?? "—")
                      : trend.some((t) => t != null)
                        ? groupValue(fresh, trend)
                        : "—",
                    <span key="actions" className="flex gap-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={inVisit ? `Change ${what} in its lab visit` : `Change ${what}`}
                        onClick={() => onChange(set)}
                      >
                        <PencilIcon />
                      </Button>
                      {inVisit ? null : (
                        <Button type="button" variant="ghost" size="icon" aria-label={`Delete ${what}`} onClick={() => setDeleting(set)}>
                          <Trash2Icon />
                        </Button>
                      )}
                    </span>,
                  ],
                };
              })}
            />
          )}
        </div>
      ) : null}

      <MetricSettingsDialog open={editOpen} onOpenChange={setEditOpen} group={fresh} />
      <DeleteReadingDialog
        open={deleting != null}
        onOpenChange={(open) => {
          if (!open) setDeleting(null);
        }}
        what={deleting ? setText(fresh, deleting, today) : ""}
        noun={noun}
        onDelete={() => {
          if (deleting) void remove(deleting);
          setDeleting(null);
        }}
      />
    </section>
  );
}
