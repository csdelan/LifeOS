import { useId, useState } from "react";
import { cn } from "@/lib/utils";

/** A clean axis top at or above `n`: 100, 125, 150, 200, 250, 300, 400, 500, 600, 800, 1,000… */
function niceTop(n: number): number {
  const steps = [1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];
  const scale = 10 ** Math.floor(Math.log10(Math.max(n, 1)));
  return (steps.find((s) => s * scale >= n) ?? 10) * scale;
}

/** One bar: a day, or a week in the year view. */
export interface MetricBar {
  key: string;
  /** Nothing recorded: no bar, and the readout says so. */
  value: number | null;
  /** The bar's tone, a Tailwind background class (`bg-steps-goal`). */
  tone: string;
  /** Still going (today, this week): drawn hatched, and the legend says what that means. */
  partial?: boolean;
  /** A marked day (an exception day): its column is hatched behind the bar. */
  marked?: boolean;
  /** The readout's first words: "Today", "Tue, Oct 21", "Week of Oct 6". */
  title: string;
  /** The readout: "6,240 steps, over the minimum, 1,260 short of the goal". */
  text: string;
  /** A second line under it: "7-day average 6,800". */
  detail?: string;
  /** Under the axis, for the first, middle and last bars: "Oct 21", "Today". */
  axis: string;
}

/** A threshold across the bars, labelled on the axis: dashed for the goal, dotted for a minimum. */
export interface MetricReference {
  value: number;
  label: string;
  pattern: "dashed" | "dotted";
}

export interface MetricLegendItem {
  label: string;
  kind: "bar" | "so-far" | "line" | "dashed" | "dotted" | "marked";
  /** For `bar` and `so-far`: the tone class. */
  tone?: string;
}

/** Hatching behind a marked day's column, as the Character page marks an exception day. */
const MARKED_HATCH =
  "repeating-linear-gradient(135deg, color-mix(in oklch, var(--muted-foreground) 22%, transparent) 0 2px, transparent 2px 6px)";

/** An SVG path for a threshold that moves by day: a flat step across each bar, joined where it moves. */
function stepPath(values: (number | null)[], top: number): string {
  let path = "";
  let last: number | null = null;
  values.forEach((v, i) => {
    if (v == null) {
      last = null;
      return;
    }
    path += last == null ? `M${i},${top - v}` : `V${top - v}`;
    path += `H${i + 1}`;
    last = v;
  });
  return path;
}

/** The SVG polyline points for each unbroken run of the average. */
function lineRuns(values: (number | null)[], top: number): string[] {
  const runs: string[] = [];
  let run: string[] = [];
  values.forEach((v, i) => {
    if (v == null) {
      if (run.length > 1) runs.push(run.join(" "));
      run = [];
      return;
    }
    run.push(`${i + 0.5},${top - v}`);
  });
  if (run.length > 1) runs.push(run.join(" "));
  return runs;
}

function LegendSwatch({ item }: { item: MetricLegendItem }) {
  switch (item.kind) {
    case "bar":
      return <span className={cn("size-2.5 rounded-[2px]", item.tone)} aria-hidden />;
    case "so-far":
      return <span className={cn("metric-so-far size-2.5 rounded-[2px]", item.tone)} aria-hidden />;
    case "line":
      return <span className="h-0.5 w-3 rounded-full bg-metric-average" aria-hidden />;
    case "dashed":
      return <span className="w-3 border-t-2 border-dashed border-foreground/70" aria-hidden />;
    case "dotted":
      return <span className="w-3 border-t-2 border-dotted border-foreground/55" aria-hidden />;
    case "marked":
      return <span className="size-2.5 rounded-[2px] ring-1 ring-foreground/20" style={{ backgroundImage: MARKED_HATCH }} aria-hidden />;
  }
}

/**
 * The daily-metric chart (activity plan §3.7), grown from `DomainDailyChart` for the Activity
 * page's steps, sleep and readiness: bars toned by band, a rolling-average line, labelled
 * threshold lines, and a hatched bar for a day or week still going. Hover or the arrow keys
 * read a bar in words; Enter (or a click) opens it when `onSelect` is given. The card around
 * it owns the range switch and the Table view ({@link MetricTable}).
 */
export function DailyMetricChart({
  label,
  bars,
  average,
  references = [],
  steps,
  legend,
  onSelect,
  top: fixedTop,
  formatValue = (n) => n.toLocaleString(),
  className,
}: {
  /** What the chart shows, for screen readers: "Steps by day, last 30 days". */
  label: string;
  bars: MetricBar[];
  /** The rolling average for each bar (null where there's none), drawn as a line. */
  average?: (number | null)[];
  references?: MetricReference[];
  /** A threshold that moves by day, one value per bar (null where there's none): a dashed step line. */
  steps?: (number | null)[];
  legend: MetricLegendItem[];
  onSelect?: (index: number) => void;
  /** A fixed axis top (a score's 100); otherwise a clean number above the tallest mark. */
  top?: number;
  /** How the axis says a value: "7,500", or "8 h". */
  formatValue?: (n: number) => string;
  className?: string;
}) {
  const [hovered, setHovered] = useState<number | null>(null);
  const readoutId = useId();

  const values = bars.map((b) => b.value ?? 0);
  const averages = [...(average ?? []), ...(steps ?? [])].filter((v): v is number => v != null);
  // A little headroom above the tallest mark, so it never touches the top line.
  const top = fixedTop ?? niceTop(Math.max(...references.map((r) => r.value), ...values, ...averages, 1) * 1.1);
  const dense = bars.length > 90;
  const shown = hovered != null ? bars[hovered] : null;
  const runs = average ? lineRuns(average, top) : [];
  const step = steps ? stepPath(steps, top) : "";

  function pick(clientX: number, el: HTMLElement) {
    const box = el.getBoundingClientRect();
    if (bars.length === 0 || box.width === 0) return null;
    const i = Math.floor(((clientX - box.left) / box.width) * bars.length);
    const at = Math.min(Math.max(i, 0), bars.length - 1);
    setHovered(at);
    return at;
  }

  function onKey(e: React.KeyboardEvent) {
    if (bars.length === 0) return;
    const last = bars.length - 1;
    const now = hovered ?? last;
    if (e.key === "Enter" && onSelect) {
      e.preventDefault();
      onSelect(now);
      return;
    }
    const next =
      e.key === "ArrowLeft" ? now - 1 : e.key === "ArrowRight" ? now + 1 : e.key === "Home" ? 0 : e.key === "End" ? last : null;
    if (next == null) return;
    e.preventDefault();
    setHovered(Math.min(Math.max(next, 0), last));
  }

  const middle = bars.length > 2 ? bars[Math.floor(bars.length / 2)] : null;

  return (
    <div className={className}>
      <div className="flex gap-2">
        <div className="relative h-48 w-10 shrink-0 text-right text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
          <span className="absolute right-0 top-0 -translate-y-1/2">{formatValue(top)}</span>
          {references.map((r) => (
            <span
              key={r.label}
              className="absolute right-0 translate-y-1/2 font-semibold text-foreground"
              style={{ bottom: `${(r.value / top) * 100}%` }}
            >
              {formatValue(r.value)}
            </span>
          ))}
          <span className="absolute bottom-0 right-0 translate-y-1/2">0</span>
        </div>
        <div
          role="group"
          aria-label={`${label}. Use the arrow keys to read a bar${onSelect ? ", and Enter to open it" : ""}.`}
          aria-describedby={readoutId}
          tabIndex={0}
          onKeyDown={onKey}
          onFocus={() => setHovered((h) => h ?? bars.length - 1)}
          onBlur={() => setHovered(null)}
          onPointerMove={(e) => pick(e.clientX, e.currentTarget)}
          onPointerLeave={() => setHovered(null)}
          onClick={
            onSelect
              ? (e) => {
                  const at = pick(e.clientX, e.currentTarget);
                  if (at != null) onSelect(at);
                }
              : undefined
          }
          className={cn(
            "relative h-48 min-w-0 flex-1 touch-pan-y rounded-sm border-b border-foreground/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
            onSelect && "cursor-pointer",
          )}
        >
          <div className="absolute inset-x-0 top-0 border-t border-foreground/8" aria-hidden />
          <div className={cn("absolute inset-0 flex items-end", dense ? "gap-0" : "gap-[2px]")} aria-hidden>
            {bars.map((b, i) => (
              <div
                key={b.key}
                data-marked={b.marked ? "" : undefined}
                className="flex h-full min-w-0 flex-1 items-end justify-center"
                style={b.marked ? { backgroundImage: MARKED_HATCH } : undefined}
              >
                {b.value != null && b.value > 0 ? (
                  <div
                    data-metric-bar=""
                    data-testid="metric-bar"
                    data-partial={b.partial ? "" : undefined}
                    className={cn(
                      "w-full max-w-6",
                      !dense && "rounded-t-[4px]",
                      b.tone,
                      b.partial && "metric-so-far",
                      hovered === i && "brightness-110 ring-2 ring-foreground/40",
                    )}
                    style={{ height: `max(2px, ${(b.value / top) * 100}%)` }}
                  />
                ) : null}
              </div>
            ))}
          </div>
          {references.map((r) => (
            <div
              key={r.label}
              className={cn(
                "pointer-events-none absolute inset-x-0 border-t-2",
                r.pattern === "dashed" ? "border-dashed border-foreground/70" : "border-dotted border-foreground/55",
              )}
              style={{ bottom: `${(r.value / top) * 100}%` }}
              aria-hidden
            />
          ))}
          {step ? (
            <svg
              className="pointer-events-none absolute inset-0 size-full overflow-visible"
              viewBox={`0 0 ${bars.length} ${top}`}
              preserveAspectRatio="none"
              aria-hidden
            >
              <path
                data-testid="metric-steps"
                d={step}
                fill="none"
                className="stroke-foreground/70"
                strokeWidth={2}
                strokeDasharray="5 4"
                vectorEffect="non-scaling-stroke"
              />
            </svg>
          ) : null}
          {runs.length > 0 ? (
            <svg
              className="pointer-events-none absolute inset-0 size-full overflow-visible"
              viewBox={`0 0 ${bars.length} ${top}`}
              preserveAspectRatio="none"
              aria-hidden
            >
              {runs.map((points) => (
                <g key={points}>
                  <polyline
                    points={points}
                    fill="none"
                    className="stroke-card"
                    strokeWidth={4}
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    vectorEffect="non-scaling-stroke"
                  />
                  <polyline
                    data-testid="metric-average"
                    points={points}
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
                left: `${((hovered! + 0.5) / bars.length) * 100}%`,
                transform: `translateX(${hovered! < bars.length / 2 ? "-10%" : "-90%"})`,
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
          <span>{bars[0]?.axis ?? ""}</span>
          <span>{middle?.axis ?? ""}</span>
          <span>{bars.length > 1 ? bars[bars.length - 1].axis : ""}</span>
        </div>
      </div>
      <p id={readoutId} className="sr-only" aria-live="polite">
        {shown ? `${shown.title}: ${shown.text}${shown.detail ? `; ${shown.detail}` : ""}` : ""}
      </p>
      {legend.length > 0 ? (
        <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
          {legend.map((item) => (
            <li key={item.label} className="flex items-center gap-1.5">
              <LegendSwatch item={item} />
              {item.label}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

export interface MetricColumn {
  key: string;
  label: string;
  /** Numbers sit right. */
  numeric?: boolean;
}

/**
 * The Table view beside a chart: every row, newest first as the caller orders them, the first
 * cell a row header. Focusable, so a keyboard can scroll a year of rows.
 */
export function MetricTable({
  label,
  caption,
  columns,
  rows,
}: {
  label: string;
  caption: string;
  columns: MetricColumn[];
  rows: { key: string; cells: React.ReactNode[] }[];
}) {
  return (
    <div
      tabIndex={0}
      role="region"
      aria-label={label}
      className="max-h-80 overflow-y-auto rounded-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
    >
      <table className="w-full text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="sticky top-0 bg-card">
          <tr className="text-left text-xs text-muted-foreground">
            {columns.map((c) => (
              <th key={c.key} scope="col" className={cn("py-1 font-medium", c.numeric && "text-right")}>
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y">
          {rows.map((r) => (
            <tr key={r.key} data-testid="metric-row">
              {r.cells.map((cell, i) =>
                i === 0 ? (
                  <th key={columns[i].key} scope="row" className="py-1.5 pr-2 text-left font-normal">
                    {cell}
                  </th>
                ) : (
                  <td
                    key={columns[i].key}
                    className={cn("py-1.5 pl-2", columns[i].numeric && "text-right tabular-nums")}
                  >
                    {cell}
                  </td>
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
