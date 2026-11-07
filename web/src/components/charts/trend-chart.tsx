import { useId, useState } from "react";
import { dayNumber } from "@/lib/metric-trend";
import { cn } from "@/lib/utils";

/** One day with a value: a dot, and the trend through it. */
export interface TrendChartPoint {
  /** `yyyy-MM-dd`: where it sits on the time axis. */
  day: string;
  value: number;
  trend: number;
  /** The readout's first words: "Today", "Tue, Nov 4". */
  title: string;
  /** The readout: "192.4 lb, trend 193.1 lb". */
  text: string;
  /** A second line under it: "2 weigh-ins: the first counts". */
  detail?: string;
}

/** A level across the chart, labelled on the axis: the goal. */
export interface TrendChartTarget {
  value: number;
  /** The legend's words: "Goal 185.0 lb". */
  label: string;
}

/** A gap longer than this many days breaks the trend line: across it, the line would guess. */
const TREND_GAP_DAYS = 7;

/** A clean step for an axis spanning `span`: about four steps, from 0.1 up. */
function niceStep(span: number): number {
  const raw = Math.max(span, 0.0001) / 4;
  const scale = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].find((s) => s * scale >= raw) ?? 10;
  return step * scale;
}

/** The axis's bottom and top: around the values, the trend and the target, on clean steps. */
function trendScale(values: readonly number[], minSpan = 2): { lo: number; hi: number } {
  if (values.length === 0) return { lo: 0, hi: 1 };
  let lo = Math.min(...values);
  let hi = Math.max(...values);
  if (hi - lo < minSpan) {
    const mid = (hi + lo) / 2;
    lo = mid - minSpan / 2;
    hi = mid + minSpan / 2;
  }
  const pad = (hi - lo) * 0.08;
  const step = niceStep(hi - lo + 2 * pad);
  return { lo: Math.floor((lo - pad) / step) * step, hi: Math.ceil((hi + pad) / step) * step };
}

/** The trend's unbroken runs, as polyline points in the chart's viewBox. */
function trendRuns(points: readonly TrendChartPoint[], x: (day: string) => number, y: (v: number) => number): string[] {
  const runs: string[] = [];
  let run: string[] = [];
  let previous: number | null = null;
  for (const p of points) {
    const n = dayNumber(p.day);
    if (previous != null && n - previous > TREND_GAP_DAYS) {
      if (run.length > 1) runs.push(run.join(" "));
      run = [];
    }
    run.push(`${x(p.day)},${y(p.trend)}`);
    previous = n;
  }
  if (run.length > 1) runs.push(run.join(" "));
  return runs;
}

/** The viewBox's height: values map onto 0–1000, top down. */
const HEIGHT = 1000;

/**
 * A metric over time (measurements plan §4.7), in the house style of `DailyMetricChart`: a dot
 * for each day's value, the smoothed trend as a line, and the target as a dashed line labelled
 * on the axis. The time axis is spaced by date, so a day without a value is a gap, and a gap of
 * more than a week breaks the trend line. Hover or the arrow keys read a day in words. The axis
 * runs from `from` to `to` whatever the values, so charts that share a range line up (M5
 * stacks several). The card around it owns the range switch and the Table view.
 */
export function TrendChart({
  label,
  points,
  from,
  to,
  target,
  formatValue,
  trendLabel = "Trend",
  valueLabel = "Each day's value",
  axisLabel,
  className,
}: {
  /** What the chart shows, for screen readers: "Weight by day, last 90 days". */
  label: string;
  /** The days with a value, oldest first. */
  points: TrendChartPoint[];
  /** The axis's first and last days, `yyyy-MM-dd`. */
  from: string;
  to: string;
  target?: TrendChartTarget;
  /** How the axis says a value: "190". */
  formatValue: (n: number) => string;
  trendLabel?: string;
  valueLabel?: string;
  /** Under the axis at its ends and middle: "Oct 9", "Today". */
  axisLabel: (day: string) => string;
  className?: string;
}) {
  const [hovered, setHovered] = useState<number | null>(null);
  const readoutId = useId();

  const first = dayNumber(from);
  const slots = Math.max(dayNumber(to) - first + 1, 1);
  const { lo, hi } = trendScale([
    ...points.flatMap((p) => [p.value, p.trend]),
    ...(target ? [target.value] : []),
  ]);
  const xOf = (day: string) => dayNumber(day) - first + 0.5;
  const yOf = (v: number) => ((hi - v) / (hi - lo)) * HEIGHT;
  const left = (day: string) => `${(xOf(day) / slots) * 100}%`;
  const top = (v: number) => `${(yOf(v) / HEIGHT) * 100}%`;
  const runs = trendRuns(points, xOf, yOf);
  const shown = hovered != null ? points[hovered] : null;
  const dense = points.length > 120;
  const middle = Math.floor((first + first + slots - 1) / 2);
  const middleDay = new Date(middle * 86_400_000).toISOString().slice(0, 10);

  function pick(clientX: number, el: HTMLElement) {
    const box = el.getBoundingClientRect();
    if (points.length === 0 || box.width === 0) return;
    const at = ((clientX - box.left) / box.width) * slots;
    let best = 0;
    points.forEach((p, i) => {
      if (Math.abs(xOf(p.day) - at) < Math.abs(xOf(points[best].day) - at)) best = i;
    });
    setHovered(best);
  }

  function onKey(e: React.KeyboardEvent) {
    if (points.length === 0) return;
    const last = points.length - 1;
    const now = hovered ?? last;
    const next =
      e.key === "ArrowLeft" ? now - 1 : e.key === "ArrowRight" ? now + 1 : e.key === "Home" ? 0 : e.key === "End" ? last : null;
    if (next == null) return;
    e.preventDefault();
    setHovered(Math.min(Math.max(next, 0), last));
  }

  return (
    <div className={className}>
      <div className="flex gap-2">
        <div className="relative h-48 w-10 shrink-0 text-right text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
          <span className="absolute right-0 top-0 -translate-y-1/2">{formatValue(hi)}</span>
          {target ? (
            <span className="absolute right-0 -translate-y-1/2 font-semibold text-foreground" style={{ top: top(target.value) }}>
              {formatValue(target.value)}
            </span>
          ) : null}
          <span className="absolute bottom-0 right-0 translate-y-1/2">{formatValue(lo)}</span>
        </div>
        <div
          role="group"
          aria-label={`${label}. Use the arrow keys to read a day.`}
          aria-describedby={readoutId}
          tabIndex={0}
          onKeyDown={onKey}
          onFocus={() => setHovered((h) => h ?? (points.length > 0 ? points.length - 1 : null))}
          onBlur={() => setHovered(null)}
          onPointerMove={(e) => pick(e.clientX, e.currentTarget)}
          onPointerLeave={() => setHovered(null)}
          data-testid="trend-chart"
          className="relative h-48 min-w-0 flex-1 touch-pan-y rounded-sm border-b border-foreground/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <div className="absolute inset-x-0 top-0 border-t border-foreground/8" aria-hidden />
          {target ? (
            <div
              data-testid="trend-target"
              className="pointer-events-none absolute inset-x-0 border-t-2 border-dashed border-foreground/70"
              style={{ top: top(target.value) }}
              aria-hidden
            />
          ) : null}
          {shown ? (
            <div
              className="pointer-events-none absolute inset-y-0 border-l border-foreground/25"
              style={{ left: left(shown.day) }}
              aria-hidden
            />
          ) : null}
          {points.map((p, i) => (
            <span
              key={p.day}
              data-testid="trend-dot"
              className={cn(
                "pointer-events-none absolute -translate-x-1/2 -translate-y-1/2 rounded-full bg-chart-earned",
                dense ? "size-1.5" : "size-2",
                hovered === i && "size-3 ring-2 ring-foreground/50",
              )}
              style={{ left: left(p.day), top: top(p.value) }}
              aria-hidden
            />
          ))}
          {runs.length > 0 ? (
            <svg
              className="pointer-events-none absolute inset-0 size-full overflow-visible"
              viewBox={`0 0 ${slots} ${HEIGHT}`}
              preserveAspectRatio="none"
              aria-hidden
            >
              {runs.map((run) => (
                <g key={run}>
                  <polyline
                    points={run}
                    fill="none"
                    className="stroke-card"
                    strokeWidth={4}
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    vectorEffect="non-scaling-stroke"
                  />
                  <polyline
                    data-testid="trend-line"
                    points={run}
                    fill="none"
                    className="stroke-metric-average"
                    strokeWidth={2}
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    vectorEffect="non-scaling-stroke"
                  />
                </g>
              ))}
            </svg>
          ) : null}
          {shown ? (
            <div
              className="pointer-events-none absolute top-1 z-10 w-max max-w-60 rounded-md bg-popover px-2 py-1 text-xs text-popover-foreground shadow-md ring-1 ring-foreground/10"
              style={{
                left: left(shown.day),
                transform: `translateX(${xOf(shown.day) < slots / 2 ? "-10%" : "-90%"})`,
              }}
              aria-hidden
            >
              <span className="block font-semibold">{shown.title}</span>
              <span className="block">{shown.text}</span>
              {shown.detail ? <span className="block text-muted-foreground">{shown.detail}</span> : null}
            </div>
          ) : null}
        </div>
      </div>
      <div className="flex gap-2 pt-1 text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
        <div className="w-10 shrink-0" />
        <div className="flex min-w-0 flex-1 justify-between">
          <span>{axisLabel(from)}</span>
          <span>{slots > 2 ? axisLabel(middleDay) : ""}</span>
          <span>{slots > 1 ? axisLabel(to) : ""}</span>
        </div>
      </div>
      <p id={readoutId} className="sr-only" aria-live="polite">
        {shown ? `${shown.title}: ${shown.text}${shown.detail ? `; ${shown.detail}` : ""}` : ""}
      </p>
      <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
        <li className="flex items-center gap-1.5">
          <span className="size-2 rounded-full bg-chart-earned" aria-hidden />
          {valueLabel}
        </li>
        <li className="flex items-center gap-1.5">
          <span className="h-0.5 w-3 rounded-full bg-metric-average" aria-hidden />
          {trendLabel}
        </li>
        {target ? (
          <li className="flex items-center gap-1.5">
            <span className="w-3 border-t-2 border-dashed border-foreground/70" aria-hidden />
            {target.label}
          </li>
        ) : null}
      </ul>
    </div>
  );
}
