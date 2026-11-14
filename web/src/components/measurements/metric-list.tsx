import { useId, useState } from "react";
import { ChevronRightIcon, PlusIcon } from "lucide-react";
import { MetricCard } from "@/components/measurements/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { dayNumber, formatDay, todayIso } from "@/lib/dates";
import {
  flagsText,
  groupValue,
  lastSet,
  nameInText,
  readingWhen,
  type MetricGroup,
  type ReadingSet,
} from "@/lib/features/measurements/measurements";
import { useWrites } from "@/lib/queries";
import { useMetricsOver } from "@/lib/features/measurements/queries";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";

/** The days a row's small trend line covers. */
const SPARK_DAYS = 90;

/**
 * A row's small trend line (§4.8): each metric's trend over the last 90 days, scaled to its own
 * row. Decoration: the row says the newest value in words, and its card has the chart. Left out
 * on a phone, where the name needs the room.
 */
function Sparkline({ group }: { group: MetricGroup }) {
  const read = useMetricsOver(
    group.metrics.map((m) => m.id),
    SPARK_DAYS,
  );
  const reads = read.data;
  if (!reads) return <span className="hidden h-6 w-16 shrink-0 sm:block" aria-hidden />;
  const values = reads.flatMap((r) => r.days.map((d) => d.trend));
  const first = dayNumber(reads[0].from ?? reads[0].today);
  const span = Math.max(dayNumber(reads[0].today) - first, 1);
  const lo = Math.min(...values);
  const hi = Math.max(...values);
  const lines = reads
    .map((r) => r.days.map((d) => `${((dayNumber(d.day) - first) / span) * 100},${hi === lo ? 12 : 22 - ((d.trend - lo) / (hi - lo)) * 20}`))
    .filter((points) => points.length > 1);
  return (
    <svg
      viewBox="0 0 100 24"
      preserveAspectRatio="none"
      className="hidden h-6 w-16 shrink-0 overflow-visible sm:block"
      data-testid="metric-sparkline"
      aria-hidden
    >
      {lines.map((points) => (
        <polyline
          key={points[0]}
          points={points.join(" ")}
          fill="none"
          className="stroke-metric-average"
          strokeWidth={2}
          strokeLinejoin="round"
          strokeLinecap="round"
          vectorEffect="non-scaling-stroke"
        />
      ))}
    </svg>
  );
}

/**
 * A metric's row on the Measurements page (MEASURE-3, measurements plan §4.8): its name, its
 * newest value with High or Low, when that was, and a small trend line. The row opens the
 * metric's card in place (the heading is a disclosure button); Log opens the sheet. A lab test's
 * row (§4.9) has no Log: its results come with a visit, which `onLog` then starts from the card.
 */
export function MetricListRow({
  group,
  open,
  onToggle,
  onLog,
  onChange,
  showLog = true,
  heading: Heading = "h3",
}: {
  group: MetricGroup;
  open: boolean;
  onToggle: () => void;
  onLog: () => void;
  onChange: (set: ReadingSet) => void;
  showLog?: boolean;
  heading?: "h3" | "h4";
}) {
  const bodyId = useId();
  const last = lastSet(group);
  const flags = last ? flagsText(last) : null;
  const taken = last?.find((r) => r != null);
  const value = last ? groupValue(group, last.map((r) => r?.value ?? null)) : null;
  const when = taken ? readingWhen(taken, todayIso()) : null;
  // Read as one sentence: the row's parts sit in separate boxes, which run together otherwise.
  const spoken = value
    ? `${group.name}: ${value}${flags ? `, ${flags.toLowerCase()}` : ""}, ${when}`
    : `${group.name}: nothing logged yet`;
  return (
    <li className="rounded-xl bg-card ring-1 ring-foreground/10" data-testid="metric-list-row">
      <div className="flex items-center gap-2 py-1.5 pr-3 pl-1.5">
        <Heading className="min-w-0 flex-1">
          <button
            type="button"
            aria-label={spoken}
            aria-expanded={open}
            aria-controls={bodyId}
            onClick={onToggle}
            className="flex min-h-11 w-full items-center gap-2 rounded-lg px-1.5 py-1 text-left hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
          >
            <ChevronRightIcon
              className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")}
              aria-hidden
            />
            <span className="min-w-0 flex-1">
              <span className="block font-medium [overflow-wrap:anywhere]">{group.name}</span>
              <span className="block text-xs font-normal text-muted-foreground" data-testid="metric-row-when">
                {when ?? "Nothing logged yet"}
              </span>
            </span>
            {last ? <Sparkline group={group} /> : null}
            <span className="flex shrink-0 flex-col items-end gap-0.5 text-right">
              <span className="font-semibold tabular-nums" data-testid="metric-row-value">
                {value ?? "—"}
              </span>
              {flags ? (
                <Badge variant="outline" data-testid="metric-row-flag">
                  {flags}
                </Badge>
              ) : null}
            </span>
          </button>
        </Heading>
        {showLog ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="min-h-11 shrink-0 md:min-h-0"
            aria-label={`Log ${nameInText(group.name)}`}
            onClick={onLog}
          >
            <PlusIcon />
            Log
          </Button>
        ) : null}
      </div>
      {open ? (
        <div id={bodyId} className="border-t px-4 py-4">
          <MetricCard group={group} layout="inline" onLog={onLog} onChange={onChange} />
        </div>
      ) : null}
    </li>
  );
}

/**
 * The archived metrics, at the foot of the page (§4.8): each keeps its readings, takes no new
 * ones, and comes back with Restore. Blood pressure's two come back together.
 */
export function ArchivedMetrics({ groups }: { groups: MetricGroup[] }) {
  const writes = useWrites();
  const titleId = useId();
  const [pending, setPending] = useState<string | null>(null);
  const restoring = useWriteAction({ report: "toast", fallback: "That didn't restore. Nothing was written." });

  async function restore(group: MetricGroup) {
    setPending(group.key);
    await restoring.run(async () => {
      for (const m of group.metrics) await writes.restore(m.urn);
    });
    setPending(null);
  }

  return (
    <section aria-labelledby={titleId} className="space-y-2">
      <h2 id={titleId} className="text-sm font-medium text-muted-foreground">
        Archived
      </h2>
      <ul className="divide-y rounded-xl ring-1 ring-foreground/10" aria-label="Archived metrics">
        {groups.map((group) => {
          const on = group.metrics[0].archivedOn;
          const count = group.metrics.reduce((n, m) => n + m.readings, 0);
          return (
            <li key={group.metrics.map((m) => m.id).join()} className="flex items-center gap-3 px-3 py-2">
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm">{group.name}</span>
                <span className="block text-xs text-muted-foreground">
                  {count === 1 ? "1 reading" : `${count} readings`}
                  {on ? ` · archived ${formatDay(on)}` : ""}
                </span>
              </span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-11 md:min-h-0"
                aria-label={`Restore ${group.name}`}
                disabled={pending != null}
                onClick={() => void restore(group)}
              >
                {pending === group.key ? "Restoring…" : "Restore"}
              </Button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
