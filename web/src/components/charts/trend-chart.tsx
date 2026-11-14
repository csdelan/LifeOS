import { useId } from "react";
import { dayNumber } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { useChartCursor } from "@/components/charts/use-chart-cursor";

/** One day with a value in a series: a dot, and the trend through it. */
export interface TrendChartPoint {
  /** `yyyy-MM-dd`: where it sits on the time axis. */
  day: string;
  value: number;
  trend: number;
  /** Outside the normal range (MEASURE-3): the dot is drawn hollow, so it shows without colour. */
  flagged?: boolean;
}

/**
 * One line on the chart: its dots and trend, and its normal range shaded behind them. Blood
 * pressure has two (systolic and diastolic); the second's dots are squares, so the two read
 * apart without colour.
 */
export interface TrendChartSeries {
  key: string;
  /** The legend's words for its dots: "Each day's first weigh-in", "Systolic". */
  label: string;
  /** The days with a value, oldest first. */
  points: TrendChartPoint[];
  /** The normal range; either end may be open (to the axis's edge). */
  band?: { low: number | null; high: number | null };
}

/** What a day reads as, when hovered or reached with the arrow keys. */
export interface TrendChartReadout {
  /** "Today", "Tue, Nov 4". */
  title: string;
  /** "192.4 lb, trend 193.1 lb". */
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

/** Each series' look: its dot, its hollow (flagged) dot, and its band. */
const TONES = [
  { dot: "rounded-full bg-chart-earned", hollow: "rounded-full border-2 border-chart-earned bg-card", band: "bg-chart-earned/12 border-chart-earned/30" },
  { dot: "rounded-[2px] bg-chart-3", hollow: "rounded-[2px] border-2 border-chart-3 bg-card", band: "bg-chart-3/12 border-chart-3/30" },
] as const;

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
 * A metric over time (measurements plan §4.7–§4.8), in the house style of `DailyMetricChart`:
 * for each series a dot for each day's value and the smoothed trend as a line, its normal range
 * shaded behind (a reading outside it is a hollow dot), and the target as a dashed line labelled
 * on the axis. The time axis is spaced by date, so a day without a value is a gap, and a gap of
 * more than a week breaks the trend line. Hover or the arrow keys read a day in words, across
 * every series. The axis runs from `from` to `to` whatever the values, so charts that share a
 * range line up (M5 stacks several). The card around it owns the range switch and the Table view.
 */
export function TrendChart({
  label,
  series,
  readout,
  from,
  to,
  target,
  formatValue,
  trendLabel = "Trend",
  bandLabel = "Normal range",
  axisLabel,
  className,
}: {
  /** What the chart shows, for screen readers: "Weight by day, last 90 days". */
  label: string;
  /** One or two series; the first is drawn last, on top. */
  series: TrendChartSeries[];
  /** A day in words, across the series. */
  readout: (day: string) => TrendChartReadout;
  /** The axis's first and last days, `yyyy-MM-dd`. */
  from: string;
  to: string;
  target?: TrendChartTarget;
  /** How the axis says a value: "190". */
  formatValue: (n: number) => string;
  trendLabel?: string;
  /** The legend's words for the shading: "Normal range 90–119 mmHg". */
  bandLabel?: string;
  /** Under the axis at its ends and middle: "Oct 9", "Today". */
  axisLabel: (day: string) => string;
  className?: string;
}) {
  const readoutId = useId();

  const days = [...new Set(series.flatMap((s) => s.points.map((p) => p.day)))].sort();
  const first = dayNumber(from);
  const slots = Math.max(dayNumber(to) - first + 1, 1);
  const { lo, hi } = trendScale([
    ...series.flatMap((s) => s.points.flatMap((p) => [p.value, p.trend])),
    ...(target ? [target.value] : []),
  ]);
  const xOf = (day: string) => dayNumber(day) - first + 0.5;
  const yOf = (v: number) => ((hi - v) / (hi - lo)) * HEIGHT;
  const left = (day: string) => `${(xOf(day) / slots) * 100}%`;
  const top = (v: number) => `${(yOf(v) / HEIGHT) * 100}%`;
  // A day's place is its date, not its order, so the pointer reads the day nearest it.
  const cursor = useChartCursor({
    count: days.length,
    nearest: (fraction) => {
      const at = fraction * slots;
      let best = 0;
      days.forEach((d, i) => {
        if (Math.abs(xOf(d) - at) < Math.abs(xOf(days[best]) - at)) best = i;
      });
      return best;
    },
  });
  const hovered = cursor.at;
  const shownDay = hovered != null ? days[hovered] : null;
  const shown = shownDay ? readout(shownDay) : null;
  const dense = days.length > 120;
  const middle = Math.floor((first + first + slots - 1) / 2);
  const middleDay = new Date(middle * 86_400_000).toISOString().slice(0, 10);
  const banded = series.some((s) => s.band && (s.band.low != null || s.band.high != null));

  /** A band clipped to the axis: from its high end (or the top) down to its low end (or the bottom). */
  function bandBox(band: { low: number | null; high: number | null }) {
    const upper = Math.min(band.high ?? hi, hi);
    const lower = Math.max(band.low ?? lo, lo);
    if (upper <= lower) return null;
    return { top: top(upper), height: `${((upper - lower) / (hi - lo)) * 100}%` };
  }

  // Drawn back to front: the second series under the first.
  const drawn = series.map((s, i) => ({ s, i })).reverse();

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
          {...cursor.props}
          data-testid="trend-chart"
          className="relative h-48 min-w-0 flex-1 touch-pan-y rounded-sm border-b border-foreground/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <div className="absolute inset-x-0 top-0 border-t border-foreground/8" aria-hidden />
          {drawn.map(({ s, i }) => {
            const box = s.band ? bandBox(s.band) : null;
            return box ? (
              <div
                key={`band-${s.key}`}
                data-testid="trend-band"
                className={cn("pointer-events-none absolute inset-x-0 border-y", TONES[i % TONES.length].band)}
                style={box}
                aria-hidden
              />
            ) : null;
          })}
          {target ? (
            <div
              data-testid="trend-target"
              className="pointer-events-none absolute inset-x-0 border-t-2 border-dashed border-foreground/70"
              style={{ top: top(target.value) }}
              aria-hidden
            />
          ) : null}
          {shownDay ? (
            <div
              className="pointer-events-none absolute inset-y-0 border-l border-foreground/25"
              style={{ left: left(shownDay) }}
              aria-hidden
            />
          ) : null}
          {drawn.map(({ s, i }) =>
            s.points.map((p) => {
              const tone = TONES[i % TONES.length];
              const on = p.day === shownDay;
              return (
                <span
                  key={`${s.key}-${p.day}`}
                  data-testid="trend-dot"
                  data-flagged={p.flagged || undefined}
                  className={cn(
                    "pointer-events-none absolute -translate-x-1/2 -translate-y-1/2",
                    p.flagged ? tone.hollow : tone.dot,
                    p.flagged ? (dense ? "size-2" : "size-2.5") : dense ? "size-1.5" : "size-2",
                    on && "size-3 ring-2 ring-foreground/50",
                  )}
                  style={{ left: left(p.day), top: top(p.value) }}
                  aria-hidden
                />
              );
            }),
          )}
          <svg
            className="pointer-events-none absolute inset-0 size-full overflow-visible"
            viewBox={`0 0 ${slots} ${HEIGHT}`}
            preserveAspectRatio="none"
            aria-hidden
          >
            {drawn.flatMap(({ s }) =>
              trendRuns(s.points, xOf, yOf).map((run) => (
                <g key={`${s.key}-${run}`}>
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
              )),
            )}
          </svg>
          {shown && shownDay ? (
            <div
              className="pointer-events-none absolute top-1 z-10 w-max max-w-60 rounded-md bg-popover px-2 py-1 text-xs text-popover-foreground shadow-md ring-1 ring-foreground/10"
              style={{
                left: left(shownDay),
                transform: `translateX(${xOf(shownDay) < slots / 2 ? "-10%" : "-90%"})`,
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
        {series.map((s, i) => (
          <li key={s.key} className="flex items-center gap-1.5">
            <span className={cn("size-2", TONES[i % TONES.length].dot)} aria-hidden />
            {s.label}
          </li>
        ))}
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
        {banded ? (
          <li className="flex items-center gap-1.5">
            <span className={cn("h-2 w-3 border-y", TONES[0].band)} aria-hidden />
            {bandLabel}
          </li>
        ) : null}
        {series.some((s) => s.points.some((p) => p.flagged)) ? (
          <li className="flex items-center gap-1.5">
            <span className={cn("size-2.5", TONES[0].hollow)} aria-hidden />
            Outside the range
          </li>
        ) : null}
      </ul>
    </div>
  );
}
