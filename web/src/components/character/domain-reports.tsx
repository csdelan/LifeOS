import { useId, useState } from "react";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { formatDay } from "@/lib/dates";
import { domainSymbol, historyText, percent, periodLabel } from "@/lib/domains";
import type { DomainReportRow, DomainRow } from "@/lib/production-ui-types";
import { useDomainReport, useDomains } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { loadView, saveView } from "@/lib/view-state";

const PERIODS = [
  { id: "7", label: "7 days" },
  { id: "30", label: "30 days" },
  { id: "90", label: "3 months" },
  { id: "180", label: "6 months" },
  { id: "365", label: "1 year" },
] as const;
type Period = (typeof PERIODS)[number]["id"];

const VIEW = "character.reports";

/** Below this many configured domains, horizontal bars stand in for the spider charts (§3.13). */
const SPIDER_MIN = 3;

/** One configured domain's consistency over a period, as the charts plot it. */
export interface ConsistencyPoint {
  key: string;
  name: string;
  /** 0–1 over the days counted; null before the domain has any. */
  value: number | null;
  daysCounted: number;
  days: number;
  /** The comparison period's figures, when one is chosen. */
  compare?: { value: number | null; daysCounted: number; days: number; label: string };
}

/** One configured domain's lifetime level, with its rank, as the level charts plot it. */
export interface LevelPoint {
  key: string;
  name: string;
  level: number;
  rankTitle: string;
  /** The level each rank above Novice starts at (10, 20, … 100): the rings and ticks. */
  rankLevels: number[];
}

/**
 * The Character page's Reports (REWARD-6 / REWARD-7, xp-domains plan §3.13): each configured
 * domain's consistency over a period (7 days to a year), optionally against a second period,
 * and its level since the start. Each value says how much history backs it, and a domain
 * younger than the period is drawn hollow. With fewer than three configured domains these are
 * horizontal bars; from three, spider charts. The table under them holds every figure, for
 * every domain, and doubles as the charts' table view.
 */
export function DomainReports() {
  const [view, setViewState] = useState(() =>
    loadView<{ period: Period; compare: Period | "none" }>(VIEW, { period: "30", compare: "none" }),
  );
  const compare = view.compare !== view.period ? view.compare : "none";
  const report = useDomainReport(Number(view.period));
  const compared = useDomainReport(Number(compare === "none" ? view.period : compare), compare !== "none");
  const domains = useDomains();
  const consistencyId = useId();
  const levelsId = useId();
  const tableId = useId();

  const setView = (next: Partial<typeof view>) => {
    const merged = { ...view, ...next };
    setViewState(merged);
    try {
      saveView(VIEW, merged);
    } catch {
      // Storage can be full or blocked; the choice still applies to this view.
    }
  };

  // The period the data on screen covers: while a new one loads, the last one stays (dimmed),
  // labelled as what it is.
  const days = report.data?.days ?? Number(view.period);
  const rows = report.data?.domains ?? [];
  const configured = rows.filter((d) => d.configured);
  const compareData = compare !== "none" ? compared.data : undefined;
  const compareRows = compareData && compareData.days !== days ? compareData.domains : [];
  const compareLabel = compareData ? periodLabel(compareData.days) : "";
  const points: ConsistencyPoint[] = configured.map((d) => {
    const other = compareRows.find((c) => c.key === d.key);
    return {
      key: d.key,
      name: d.name,
      value: d.daysCounted ? d.consistency : null,
      daysCounted: d.daysCounted ?? 0,
      days,
      compare: other
        ? {
            value: other.daysCounted ? other.consistency : null,
            daysCounted: other.daysCounted ?? 0,
            days: compareData!.days,
            label: compareLabel,
          }
        : undefined,
    };
  });
  const levels: LevelPoint[] = (domains.data?.domains ?? [])
    .filter((d) => d.configured && d.lifetime)
    .map((d) => levelPoint(d));
  const label = periodLabel(days);

  return (
    <div className="space-y-8">
      <section aria-labelledby={consistencyId} className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 id={consistencyId} className="font-heading text-xl">
              Consistency
            </h2>
            <p className="max-w-prose text-xs text-muted-foreground">
              The share of successful days, over the days each domain has history for. Today counts once it
              succeeds.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <ToggleGroup
              label="Period"
              value={view.period}
              onChange={(period) => setView({ period })}
              options={[...PERIODS]}
            />
            <label className="flex items-center gap-2 text-xs text-muted-foreground">
              Compare with
              <select
                value={compare}
                onChange={(e) => setView({ compare: e.target.value as Period | "none" })}
                className="min-h-11 rounded-md border bg-background px-2 text-xs text-foreground md:min-h-8"
              >
                <option value="none">Nothing</option>
                {PERIODS.filter((p) => p.id !== view.period).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>

        {report.isLoading ? <ListSkeleton rows={2} /> : null}
        {report.isError ? (
          <p role="alert" className="text-sm text-destructive">
            The report failed to load.
          </p>
        ) : null}
        {report.data ? (
          <div
            className={cn(
              "rounded-xl bg-card p-4 ring-1 ring-foreground/10 transition-opacity",
              (report.isPlaceholderData || compared.isPlaceholderData) && "opacity-60",
            )}
          >
            {points.length === 0 ? (
              <p className="text-sm text-muted-foreground">No domain has rules yet, so there's nothing to plot.</p>
            ) : points.length >= SPIDER_MIN ? (
              <ConsistencySpider points={points} periodLabel={label} />
            ) : (
              <ConsistencyBars points={points} periodLabel={label} />
            )}
          </div>
        ) : null}
      </section>

      <section aria-labelledby={levelsId} className="space-y-3">
        <div>
          <h2 id={levelsId} className="font-heading text-xl">
            Since the start
          </h2>
          <p className="max-w-prose text-xs text-muted-foreground">
            Each domain's level, 1 to 100, with a mark where each rank begins. Levels are for life.
          </p>
        </div>
        {domains.isLoading ? <ListSkeleton rows={1} /> : null}
        {levels.length > 0 ? (
          <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            {levels.length >= SPIDER_MIN ? <LevelSpider points={levels} /> : <LevelBars points={levels} />}
          </div>
        ) : null}
      </section>

      {report.data ? (
        <section aria-labelledby={tableId} className="space-y-3">
          <h2 id={tableId} className="font-heading text-xl">
            By domain, last {label}
          </h2>
          <ReportTable rows={rows} days={days} periodLabel={label} />
        </section>
      ) : null}
    </div>
  );
}

function levelPoint(d: DomainRow): LevelPoint {
  return {
    key: d.key,
    name: d.name,
    level: d.lifetime!.level,
    rankTitle: d.lifetime!.rankTitle,
    rankLevels: (d.ranks ?? []).filter((r) => r.rank > 0).map((r) => r.level),
  };
}

const valueText = (value: number | null) => (value == null ? "no days yet" : percent(value));

/** "Health: 80% of days successful, over all 30 days." */
function inWords(p: { name: string; value: number | null; daysCounted: number; days: number }): string {
  return p.value == null
    ? `${p.name}: no days yet in the last ${periodLabel(p.days)}.`
    : `${p.name}: ${percent(p.value)} of days successful, over ${historyText(p.daysCounted, p.days)}.`;
}

/**
 * Consistency as horizontal bars, one per configured domain (fewer than three configured). A
 * domain with less history than the period is a hollow, dashed bar; a comparison period is a
 * tick across the bar, named under it.
 */
export function ConsistencyBars({ points, periodLabel: label }: { points: ConsistencyPoint[]; periodLabel: string }) {
  const partial = points.some((p) => p.daysCounted < p.days);
  const comparing = points.find((p) => p.compare)?.compare?.label;
  return (
    <div className="space-y-4">
      <ul className="space-y-4" aria-label={`Consistency, last ${label}`}>
        {points.map((p) => {
          const full = p.daysCounted === p.days;
          return (
            <li key={p.key} className="space-y-1.5">
              <p className="flex flex-wrap items-baseline justify-between gap-x-3 text-sm">
                <span className="font-medium">
                  <span aria-hidden>{domainSymbol(p.key)} </span>
                  {p.name}
                </span>
                <span className="tabular-nums">
                  <span className="font-semibold">{valueText(p.value)}</span>
                  <span className="text-muted-foreground"> · {historyText(p.daysCounted, p.days)}</span>
                </span>
              </p>
              <div className="relative h-3 rounded-sm bg-muted" aria-hidden>
                {p.value != null && p.value > 0 ? (
                  <div
                    data-testid="consistency-bar"
                    data-history={full ? "full" : "partial"}
                    className={cn(
                      "absolute inset-y-0 left-0 rounded-r-[4px]",
                      full ? "bg-chart-earned" : "border-2 border-dashed border-chart-earned bg-chart-earned/15",
                    )}
                    style={{ width: `${p.value * 100}%` }}
                  />
                ) : null}
                {p.compare?.value != null ? (
                  <div
                    data-testid="compare-tick"
                    className="absolute -inset-y-1 w-0.5 -translate-x-1/2 rounded-full bg-foreground"
                    style={{ left: `${p.compare.value * 100}%` }}
                  />
                ) : null}
              </div>
              {p.compare ? (
                <p className="text-xs text-muted-foreground tabular-nums">
                  {p.compare.label}: {valueText(p.compare.value)} · {historyText(p.compare.daysCounted, p.compare.days)}
                </p>
              ) : null}
            </li>
          );
        })}
      </ul>
      <ul className="flex flex-wrap gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
        <li className="flex items-center gap-1.5">
          <span className="h-2.5 w-4 rounded-[2px] bg-chart-earned" aria-hidden /> Last {label}
        </li>
        {partial ? (
          <li className="flex items-center gap-1.5">
            <span className="h-2.5 w-4 rounded-[2px] border-2 border-dashed border-chart-earned bg-chart-earned/15" aria-hidden />
            Less history than the period
          </li>
        ) : null}
        {comparing ? (
          <li className="flex items-center gap-1.5">
            <span className="h-3 w-0.5 rounded-full bg-foreground" aria-hidden /> Last {comparing}
          </li>
        ) : null}
      </ul>
    </div>
  );
}

/** Each configured domain's level as a bar from 1 to 100, ticked where each rank begins. */
export function LevelBars({ points }: { points: LevelPoint[] }) {
  return (
    <ul className="space-y-4" aria-label="Level since the start">
      {points.map((p) => (
        <li key={p.key} className="space-y-1.5">
          <p className="flex flex-wrap items-baseline justify-between gap-x-3 text-sm">
            <span className="font-medium">
              <span aria-hidden>{domainSymbol(p.key)} </span>
              {p.name}
            </span>
            <span className="tabular-nums">
              <span className="font-semibold">Level {p.level}</span>
              <span className="text-muted-foreground"> of 100 · {p.rankTitle}</span>
            </span>
          </p>
          <div className="relative h-3 rounded-sm bg-muted" aria-hidden>
            <div
              data-testid="level-bar"
              className="absolute inset-y-0 left-0 rounded-r-[4px] bg-chart-earned"
              style={{ width: `${p.level}%` }}
            />
            {p.rankLevels.map((level) => (
              <div
                key={level}
                className="absolute inset-y-0 w-px bg-background/90"
                style={{ left: `${level}%` }}
              />
            ))}
          </div>
          <div className="relative h-3 text-[0.625rem] text-muted-foreground tabular-nums" aria-hidden>
            {[1, 25, 50, 75, 100].map((level) => (
              <span
                key={level}
                className={cn("absolute", level === 1 ? "left-0" : level === 100 ? "right-0" : "-translate-x-1/2")}
                style={level === 1 || level === 100 ? undefined : { left: `${level}%` }}
              >
                {level}
              </span>
            ))}
          </div>
        </li>
      ))}
    </ul>
  );
}

// --- Spider charts (three or more configured domains) ----------------------------------

// Wider than tall, so the labels beside the side axes have room.
const WIDTH = 520;
const HEIGHT = 340;
const CX = WIDTH / 2;
const CY = HEIGHT / 2;
const RADIUS = 112;

function at(i: number, n: number, fraction: number): [number, number] {
  const angle = -Math.PI / 2 + (2 * Math.PI * i) / n;
  return [CX + Math.cos(angle) * RADIUS * fraction, CY + Math.sin(angle) * RADIUS * fraction];
}

const pointsOf = (values: number[]) => values.map((v, i) => at(i, values.length, v).join(",")).join(" ");

interface SpiderAxis {
  key: string;
  name: string;
  /** 0–1 along the axis; null draws no vertex. */
  value: number | null;
  /** The value's words under the axis name, e.g. "88% · 12 of 30 days". */
  detail: string;
  /** Less history than the period: a hollow vertex. */
  partial: boolean;
  /** The comparison period's value along the axis. */
  compare?: number | null;
}

/**
 * A spider chart: one axis per domain, rings at `rings` (fractions of the axis), the period's
 * polygon filled, and an optional comparison polygon dashed. Hovering a vertex reads it out;
 * the table under the chart holds every value.
 */
function Spider({
  axes,
  rings,
  ringLabel,
  title,
  description,
  compareLabel,
}: {
  axes: SpiderAxis[];
  rings: number[];
  ringLabel: (fraction: number) => string;
  title: string;
  description: string;
  compareLabel?: string;
}) {
  const [hovered, setHovered] = useState<number | null>(null);
  const n = axes.length;
  const values = axes.map((a) => a.value ?? 0);
  const comparing = axes.some((a) => a.compare != null);
  const shown = hovered != null ? axes[hovered] : null;
  return (
    <div className="space-y-2">
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className="mx-auto w-full max-w-xl"
        role="img"
        aria-label={`${title}. ${description}`}
        data-testid="spider"
      >
        {rings.map((r) => (
          <g key={r}>
            <polygon points={pointsOf(axes.map(() => r))} fill="none" className="stroke-foreground/12" strokeWidth={1} />
            <text
              x={CX - 4}
              y={CY - RADIUS * r - 2}
              textAnchor="end"
              className="fill-muted-foreground text-[9px] tabular-nums"
            >
              {ringLabel(r)}
            </text>
          </g>
        ))}
        {axes.map((a, i) => {
          const [x, y] = at(i, n, 1);
          return <line key={a.key} x1={CX} y1={CY} x2={x} y2={y} className="stroke-foreground/12" strokeWidth={1} />;
        })}
        <polygon
          points={pointsOf(values)}
          className="fill-chart-earned/20 stroke-chart-earned"
          strokeWidth={2}
          strokeLinejoin="round"
        />
        {comparing ? (
          <polygon
            data-testid="spider-compare"
            points={pointsOf(axes.map((a) => a.compare ?? 0))}
            fill="none"
            className="stroke-foreground/70"
            strokeWidth={2}
            strokeDasharray="5 4"
            strokeLinejoin="round"
          />
        ) : null}
        {axes.map((a, i) => {
          if (a.value == null) return null;
          const [x, y] = at(i, n, a.value);
          return (
            <g key={a.key} onPointerEnter={() => setHovered(i)} onPointerLeave={() => setHovered(null)}>
              <circle cx={x} cy={y} r={12} fill="transparent" />
              <circle
                data-testid="spider-vertex"
                data-history={a.partial ? "partial" : "full"}
                cx={x}
                cy={y}
                r={4.5}
                strokeWidth={2}
                className={cn("stroke-chart-earned", a.partial ? "fill-card" : "fill-chart-earned")}
                strokeDasharray={a.partial ? "2 2" : undefined}
              />
            </g>
          );
        })}
        {axes.map((a, i) => {
          const [x, y0] = at(i, n, 1.14);
          const anchor = Math.abs(x - CX) < 8 ? "middle" : x > CX ? "start" : "end";
          // The top label sits above its axis end; the bottom one below it.
          const y = Math.abs(x - CX) < 8 && y0 < CY ? y0 - 12 : y0;
          return (
            <text key={a.key} x={x} y={y} textAnchor={anchor} className="text-[11px]">
              <tspan className="fill-foreground font-medium">{a.name}</tspan>
              <tspan x={x} dy="1.2em" className="fill-muted-foreground text-[10px]">
                {a.detail}
              </tspan>
            </text>
          );
        })}
      </svg>
      <p className="min-h-5 text-center text-xs tabular-nums" aria-hidden>
        {shown ? `${shown.name}: ${shown.detail}` : ""}
      </p>
      <ul className="flex flex-wrap justify-center gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
        <li className="flex items-center gap-1.5">
          <span className="h-2.5 w-4 rounded-[2px] bg-chart-earned" aria-hidden /> {title}
        </li>
        {axes.some((a) => a.partial) ? (
          <li className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-full border-2 border-dashed border-chart-earned" aria-hidden /> Less
            history than the period
          </li>
        ) : null}
        {comparing && compareLabel ? (
          <li className="flex items-center gap-1.5">
            <span className="w-4 border-t-2 border-dashed border-foreground/70" aria-hidden /> Last {compareLabel}
          </li>
        ) : null}
      </ul>
    </div>
  );
}

/** Consistency per configured domain as a spider (three or more configured), 0–100%. */
export function ConsistencySpider({ points, periodLabel: label }: { points: ConsistencyPoint[]; periodLabel: string }) {
  const compareLabel = points.find((p) => p.compare)?.compare?.label;
  return (
    <Spider
      title={`Last ${label}`}
      description={points.map(inWords).join(" ")}
      rings={[0.25, 0.5, 0.75, 1]}
      ringLabel={(r) => `${Math.round(r * 100)}%`}
      compareLabel={compareLabel}
      axes={points.map((p) => ({
        key: p.key,
        name: p.name,
        value: p.value,
        detail: `${valueText(p.value)} · ${historyText(p.daysCounted, p.days)}`,
        partial: p.daysCounted < p.days,
        compare: p.compare?.value ?? null,
      }))}
    />
  );
}

/** Level per configured domain as a spider (three or more configured), with a ring at each rank. */
export function LevelSpider({ points }: { points: LevelPoint[] }) {
  const ranks = [...new Set(points.flatMap((p) => p.rankLevels))].sort((a, b) => a - b);
  return (
    <Spider
      title="Level"
      description={points.map((p) => `${p.name}: level ${p.level}, ${p.rankTitle}.`).join(" ")}
      rings={ranks.map((level) => level / 100)}
      ringLabel={(r) => (Math.round(r * 100) % 20 === 0 ? `${Math.round(r * 100)}` : "")}
      axes={points.map((p) => ({
        key: p.key,
        name: p.name,
        value: p.level / 100,
        detail: `Level ${p.level} · ${p.rankTitle}`,
        partial: false,
      }))}
    />
  );
}

/** Every domain's figures for the period; the charts' table view. */
function ReportTable({ rows, days, periodLabel: label }: { rows: DomainReportRow[]; days: number; periodLabel: string }) {
  const dash = <span className="text-muted-foreground">—</span>;
  return (
    <div
      tabIndex={0}
      role="region"
      aria-label={`Every life domain over the last ${label}, as a table`}
      className="overflow-x-auto rounded-xl bg-card ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
    >
      <table className="w-full text-sm whitespace-nowrap">
        <caption className="sr-only">Every life domain over the last {label}</caption>
        <thead>
          <tr className="border-b text-left text-xs text-muted-foreground">
            <th scope="col" className="px-3 py-2 font-medium">
              Domain
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Days counted
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Successful
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Consistency
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              XP
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Base XP a day
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Level
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Ranks reached
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {rows.map((d) => (
            <tr key={d.key}>
              <th scope="row" className="px-3 py-2 text-left font-medium">
                <span aria-hidden>{domainSymbol(d.key)} </span>
                {d.name}
                {d.configured ? null : <span className="block text-xs font-normal text-muted-foreground">XP only</span>}
              </th>
              <td className="px-3 py-2 text-right tabular-nums">
                {d.daysCounted != null ? historyText(d.daysCounted, days) : dash}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">{d.successfulDays ?? dash}</td>
              <td className="px-3 py-2 text-right tabular-nums">
                {d.consistency != null && d.daysCounted ? percent(d.consistency) : dash}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {d.paid.toLocaleString()}
                {d.base !== d.paid ? (
                  <span className="block text-xs text-muted-foreground">{d.base.toLocaleString()} base</span>
                ) : null}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {d.averageBase != null && d.daysCounted ? d.averageBase.toLocaleString() : dash}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {d.levelTo == null ? dash : d.levelFrom === d.levelTo ? d.levelTo : `${d.levelFrom} → ${d.levelTo}`}
              </td>
              <td className="px-3 py-2">
                {d.ranksReached && d.ranksReached.length > 0
                  ? d.ranksReached.map((r) => `${r.title} (${formatDay(r.qualifiedOn)})`).join(", ")
                  : dash}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
