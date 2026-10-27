import { useId, useState } from "react";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { formatDay } from "@/lib/dates";
import { shownPoints } from "@/lib/home-score";
import { domainSymbol, newBestsText, percent, periodLabel, rankSummary, type RankSeries } from "@/lib/domains";
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

export type { RankSeries };

const VIEW = "character.reports";

/** From this many configured domains, spider charts join the lines (§7.4.10). */
const SPIDER_MIN = 3;

/** Hatching for an exception day's band. */
const HATCH =
  "repeating-linear-gradient(135deg, color-mix(in oklch, var(--muted-foreground) 22%, transparent) 0 2px, transparent 2px 6px)";

/** Each configured domain's line, in order: Health first. */
const LINE_TONES = [
  { stroke: "stroke-chart-earned", swatch: "bg-chart-earned" },
  { stroke: "stroke-chart-3", swatch: "bg-chart-3" },
  { stroke: "stroke-chart-2", swatch: "bg-chart-2" },
  { stroke: "stroke-chart-4", swatch: "bg-chart-4" },
  { stroke: "stroke-chart-5", swatch: "bg-chart-5" },
] as const;

/** One configured domain's shown level and best, as the level charts plot them. */
export interface LevelPoint {
  key: string;
  name: string;
  level: number;
  rankTitle: string;
  best: number;
  bestRankTitle: string;
  /** The level each rank above Novice starts at (10, 20, … 100): the ticks and rings. */
  rankLevels: number[];
}

/** One configured domain's rank at a period's start and end, as the rank spider plots it. */
export interface RankPoint {
  key: string;
  name: string;
  rankFrom: number;
  rankTo: number;
  titleFrom: string;
  titleTo: string;
  /** The top rank's index (10 for Health). */
  topRank: number;
}

/**
 * The Character page's Reports (REWARD-6 / REWARD-7, xp-domains plan §7.4.10): each configured
 * domain's rank over a period (7 days to a year), its best rank dotted and exception days
 * hatched; each domain's level since the start with its best; and the period's figures for every
 * domain in a table. From three configured domains, spider charts compare ranks and levels too.
 */
export function DomainReports() {
  const [view, setViewState] = useState(() => loadView<{ period: Period }>(VIEW, { period: "30" }));
  const period = PERIODS.some((p) => p.id === view.period) ? view.period : "30";
  const report = useDomainReport(Number(period));
  const domains = useDomains();
  const ranksId = useId();
  const levelsId = useId();
  const tableId = useId();

  const setPeriod = (next: Period) => {
    setViewState({ period: next });
    try {
      saveView(VIEW, { period: next });
    } catch {
      // Storage can be full or blocked; the choice still applies to this view.
    }
  };

  // The period the data on screen covers: while a new one loads, the last one stays (dimmed),
  // labelled as what it is.
  const days = report.data?.days ?? Number(period);
  const rows = report.data?.domains ?? [];
  const configured = rows.filter((d) => d.configured && d.ranks);
  const ladders = new Map((domains.data?.domains ?? []).map((d) => [d.key, (d.ranks ?? []).map((r) => r.title)]));
  const series: RankSeries[] = configured.map((d) => ({
    key: d.key,
    name: d.name,
    days: d.ranks ?? [],
    titles: ladders.get(d.key) ?? [],
  }));
  const rankPoints: RankPoint[] = configured.map((d) => {
    const titles = ladders.get(d.key) ?? [];
    return {
      key: d.key,
      name: d.name,
      rankFrom: d.rankFrom ?? 0,
      rankTo: d.rankTo ?? 0,
      titleFrom: titles[d.rankFrom ?? 0] ?? "",
      titleTo: d.rankTitle ?? "",
      topRank: Math.max(titles.length - 1, 1),
    };
  });
  const levels: LevelPoint[] = (domains.data?.domains ?? [])
    .filter((d) => d.configured && d.shown && d.best)
    .map((d) => levelPoint(d));
  const label = periodLabel(days);

  return (
    <div className="space-y-8">
      <section aria-labelledby={ranksId} className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 id={ranksId} className="font-heading text-xl">
              Rank over time
            </h2>
            <p className="max-w-prose text-xs text-muted-foreground">
              Each domain's rank at the end of each settled day. The dotted line is the best rank; hatched days are
              exception days.
            </p>
          </div>
          <ToggleGroup label="Period" value={period} onChange={setPeriod} options={[...PERIODS]} />
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
              "space-y-6 rounded-xl bg-card p-4 ring-1 ring-foreground/10 transition-opacity",
              report.isPlaceholderData && "opacity-60",
            )}
          >
            {series.length === 0 ? (
              <p className="text-sm text-muted-foreground">No domain has rules yet, so there's nothing to plot.</p>
            ) : (
              <RankChart series={series} periodLabel={label} />
            )}
            {rankPoints.length >= SPIDER_MIN ? <RankSpider points={rankPoints} periodLabel={label} /> : null}
          </div>
        ) : null}
      </section>

      <section aria-labelledby={levelsId} className="space-y-3">
        <div>
          <h2 id={levelsId} className="font-heading text-xl">
            Since the start
          </h2>
          <p className="max-w-prose text-xs text-muted-foreground">
            Each domain's level, 1 to 100, with a mark where each rank begins and a tick at your best.
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
          <ReportTable rows={rows} ladders={ladders} periodLabel={label} />
        </section>
      ) : null}
    </div>
  );
}

function levelPoint(d: DomainRow): LevelPoint {
  return {
    key: d.key,
    name: d.name,
    level: d.shown!.level,
    rankTitle: d.shown!.rankTitle,
    best: d.best!.level,
    bestRankTitle: d.best!.rankTitle,
    rankLevels: (d.ranks ?? []).filter((r) => r.rank > 0).map((r) => r.level),
  };
}

const CHART_HEIGHT = 176;

/**
 * Rank over time (§7.4.10): one step line per configured domain across the period, the best rank
 * dotted, exception days as hatched bands, rank titles on the axis (from Novice to one rank above
 * the highest best, so a young domain's line isn't flat on the floor). Hover or arrow keys read a
 * day; the table view lists every day.
 */
export function RankChart({ series, periodLabel: label }: { series: RankSeries[]; periodLabel: string }) {
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const [hovered, setHovered] = useState<number | null>(null);
  const readoutId = useId();
  const length = Math.max(...series.map((s) => s.days.length), 0);
  const titles = series.reduce<string[]>((a, s) => (s.titles.length > a.length ? s.titles : a), []);
  const topRank = Math.max(titles.length - 1, 1);
  const ceiling = Math.min(
    topRank,
    Math.max(1, ...series.flatMap((s) => s.days.map((d) => Math.max(d.rank, d.bestRank) + 1))),
  );
  const axis = Array.from({ length: ceiling + 1 }, (_, i) => i);
  const y = (rank: number) => 100 - (rank / ceiling) * 100;
  const exceptionDays = series[0]?.days.map((d) => d.exception) ?? [];
  const summary = series.map(rankSummary).join(" ");

  const stepPath = (values: number[]) =>
    values.map((v, i) => `${i === 0 ? "M" : "L"}${i},${y(v)} L${i + 1},${y(v)}`).join(" ");

  function pick(clientX: number, el: HTMLElement) {
    const box = el.getBoundingClientRect();
    if (length === 0 || box.width === 0) return;
    const i = Math.floor(((clientX - box.left) / box.width) * length);
    setHovered(Math.min(Math.max(i, 0), length - 1));
  }

  function onKey(e: React.KeyboardEvent) {
    if (length === 0) return;
    const last = length - 1;
    const now = hovered ?? last;
    const next =
      e.key === "ArrowLeft" ? now - 1 : e.key === "ArrowRight" ? now + 1 : e.key === "Home" ? 0 : e.key === "End" ? last : null;
    if (next == null) return;
    e.preventDefault();
    setHovered(Math.min(Math.max(next, 0), last));
  }

  const readout = (i: number) =>
    series
      .map((s) => {
        const d = s.days[i];
        if (!d) return "";
        const exception = d.exception ? " · exception day" : "";
        return `${s.name} ${formatDay(d.day)}: ${s.titles[d.rank] ?? d.rank} (level ${d.level}), best ${s.titles[d.bestRank] ?? d.bestRank}${exception}`;
      })
      .filter(Boolean)
      .join(" · ");
  const days = series[0]?.days ?? [];

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm">{summary}</p>
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
      {mode === "chart" ? (
        <div>
          <div className="flex gap-2">
            <div
              className="relative w-20 shrink-0 text-right text-[0.625rem] text-muted-foreground"
              style={{ height: CHART_HEIGHT }}
              aria-hidden
            >
              {axis.map((rank) => (
                <span
                  key={rank}
                  className="absolute right-0 translate-y-1/2 truncate"
                  style={{ bottom: `${(rank / ceiling) * 100}%` }}
                >
                  {titles[rank] ?? rank}
                </span>
              ))}
            </div>
            <div
              role="group"
              aria-label={`Rank over the last ${label}. ${summary} Use the arrow keys to read a day.`}
              aria-describedby={readoutId}
              tabIndex={0}
              onKeyDown={onKey}
              onFocus={() => setHovered((h) => h ?? length - 1)}
              onBlur={() => setHovered(null)}
              onPointerMove={(e) => pick(e.clientX, e.currentTarget)}
              onPointerLeave={() => setHovered(null)}
              data-testid="rank-chart"
              className="relative min-w-0 flex-1 touch-none rounded-sm border-b border-foreground/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
              style={{ height: CHART_HEIGHT }}
            >
              <div className="absolute inset-0 flex" aria-hidden>
                {exceptionDays.map((exception, i) => (
                  <div
                    key={i}
                    data-testid={exception ? "exception-band" : undefined}
                    className="h-full min-w-0 flex-1"
                    style={exception ? { backgroundImage: HATCH } : undefined}
                  />
                ))}
              </div>
              {axis.map((rank) => (
                <div
                  key={rank}
                  className="pointer-events-none absolute inset-x-0 border-t border-foreground/8"
                  style={{ bottom: `${(rank / ceiling) * 100}%` }}
                  aria-hidden
                />
              ))}
              {length > 0 ? (
                <svg
                  className="pointer-events-none absolute inset-0 h-full w-full overflow-visible"
                  viewBox={`0 0 ${length} 100`}
                  preserveAspectRatio="none"
                  aria-hidden
                >
                  {series.map((s, i) => {
                    const tone = LINE_TONES[i % LINE_TONES.length];
                    return (
                      <g key={s.key}>
                        <path
                          data-testid="best-line"
                          d={stepPath(s.days.map((d) => d.bestRank))}
                          fill="none"
                          className={cn(tone.stroke, "opacity-70")}
                          strokeWidth={2}
                          strokeDasharray="1 4"
                          strokeLinecap="round"
                          vectorEffect="non-scaling-stroke"
                        />
                        <path
                          data-testid="rank-line"
                          d={stepPath(s.days.map((d) => d.rank))}
                          fill="none"
                          className={tone.stroke}
                          strokeWidth={2.5}
                          strokeLinejoin="round"
                          vectorEffect="non-scaling-stroke"
                        />
                      </g>
                    );
                  })}
                </svg>
              ) : null}
              {hovered != null ? (
                <>
                  <div
                    className="pointer-events-none absolute inset-y-0 w-px bg-foreground/40"
                    style={{ left: `${((hovered + 0.5) / length) * 100}%` }}
                    aria-hidden
                  />
                  <div
                    className="pointer-events-none absolute top-1 z-10 max-w-[80%] rounded-md bg-popover px-2 py-1 text-xs text-popover-foreground shadow-md ring-1 ring-foreground/10"
                    style={{
                      left: `${((hovered + 0.5) / length) * 100}%`,
                      transform: `translateX(${hovered < length / 2 ? "-10%" : "-90%"})`,
                    }}
                    aria-hidden
                  >
                    {readout(hovered)}
                  </div>
                </>
              ) : null}
            </div>
          </div>
          <div className="flex gap-2 pt-1 text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
            <div className="w-20 shrink-0" />
            <div className="flex min-w-0 flex-1 justify-between">
              <span>{days[0] ? formatDay(days[0].day) : ""}</span>
              <span>{days.length > 2 ? formatDay(days[Math.floor(days.length / 2)].day) : ""}</span>
              <span>{days.at(-1) ? formatDay(days.at(-1)!.day) : ""}</span>
            </div>
          </div>
          <p id={readoutId} className="sr-only" aria-live="polite">
            {hovered != null ? readout(hovered) : ""}
          </p>
          <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
            {series.map((s, i) => {
              const tone = LINE_TONES[i % LINE_TONES.length];
              return (
                <li key={s.key} className="flex items-center gap-1.5">
                  <span className={cn("h-0.5 w-4 rounded-full", tone.swatch)} aria-hidden /> {s.name} rank
                </li>
              );
            })}
            <li className="flex items-center gap-1.5">
              <span className="w-4 border-t-2 border-dotted border-foreground/60" aria-hidden /> Best rank
            </li>
            {exceptionDays.some(Boolean) ? (
              <li className="flex items-center gap-1.5">
                <span className="size-2.5 rounded-[2px] ring-1 ring-foreground/20" style={{ backgroundImage: HATCH }} aria-hidden />
                Exception day
              </li>
            ) : null}
          </ul>
        </div>
      ) : (
        <div
          tabIndex={0}
          role="region"
          aria-label={`Rank over the last ${label}, as a table`}
          className="max-h-80 overflow-y-auto rounded-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <table className="w-full text-sm">
            <caption className="sr-only">Rank by day over the last {label}, newest first</caption>
            <thead>
              <tr className="text-left text-xs text-muted-foreground">
                <th scope="col" className="py-1 font-medium">
                  Day
                </th>
                {series.length > 1 ? (
                  <th scope="col" className="py-1 font-medium">
                    Domain
                  </th>
                ) : null}
                <th scope="col" className="py-1 text-right font-medium">
                  Level
                </th>
                <th scope="col" className="py-1 pl-3 font-medium">
                  Rank
                </th>
                <th scope="col" className="py-1 pl-3 font-medium">
                  Best rank
                </th>
                <th scope="col" className="py-1 pl-3 font-medium">
                  Exception
                </th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {[...Array(length).keys()].reverse().flatMap((i) =>
                series.map((s) => {
                  const d = s.days[i];
                  if (!d) return null;
                  return (
                    <tr key={`${s.key}-${d.day}`}>
                      <th scope="row" className="py-1.5 text-left font-normal">
                        {formatDay(d.day)}
                      </th>
                      {series.length > 1 ? <td className="py-1.5">{s.name}</td> : null}
                      <td className="py-1.5 text-right tabular-nums">{d.level}</td>
                      <td className="py-1.5 pl-3">{s.titles[d.rank] ?? d.rank}</td>
                      <td className="py-1.5 pl-3">{s.titles[d.bestRank] ?? d.bestRank}</td>
                      <td className="py-1.5 pl-3 text-muted-foreground">{d.exception ? "Yes" : ""}</td>
                    </tr>
                  );
                }),
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** Each configured domain's shown level as a bar from 1 to 100, ticked where each rank begins and at its best. */
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
              <span className="text-muted-foreground">
                {" "}
                of 100 · {p.rankTitle}
                {p.best > p.level ? ` · best ${p.best}, ${p.bestRankTitle}` : ""}
              </span>
            </span>
          </p>
          <div className="relative h-3 rounded-sm bg-muted" aria-hidden>
            <div
              data-testid="level-bar"
              className="absolute inset-y-0 left-0 rounded-r-[4px] bg-chart-earned"
              style={{ width: `${p.level}%` }}
            />
            {p.rankLevels.map((level) => (
              <div key={level} className="absolute inset-y-0 w-px bg-background/90" style={{ left: `${level}%` }} />
            ))}
            {p.best > p.level ? (
              <div
                data-testid="best-tick"
                className="absolute -inset-y-1 w-0.5 -translate-x-1/2 rounded-full bg-foreground"
                style={{ left: `${p.best}%` }}
              />
            ) : null}
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
      {points.some((p) => p.best > p.level) ? (
        <li className="flex items-center gap-1.5 text-[0.6875rem] text-muted-foreground" aria-hidden>
          <span className="h-3 w-0.5 rounded-full bg-foreground" /> Best level
        </li>
      ) : null}
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
  /** 0–1 along the axis. */
  value: number;
  /** The value's words under the axis name, e.g. "Expert · from Adept". */
  detail: string;
  /** A second value along the axis, dashed (the period's start). */
  compare?: number;
}

/**
 * A spider chart: one axis per domain, rings at `rings` (fractions of the axis), the value's
 * polygon filled, and an optional second polygon dashed. Hovering a vertex reads it out; the
 * table under the charts holds every value.
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
            <text x={CX - 4} y={CY - RADIUS * r - 2} textAnchor="end" className="fill-muted-foreground text-[9px] tabular-nums">
              {ringLabel(r)}
            </text>
          </g>
        ))}
        {axes.map((a, i) => {
          const [x, y] = at(i, n, 1);
          return <line key={a.key} x1={CX} y1={CY} x2={x} y2={y} className="stroke-foreground/12" strokeWidth={1} />;
        })}
        <polygon
          points={pointsOf(axes.map((a) => a.value))}
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
          const [x, y] = at(i, n, a.value);
          return (
            <g key={a.key} onPointerEnter={() => setHovered(i)} onPointerLeave={() => setHovered(null)}>
              <circle cx={x} cy={y} r={12} fill="transparent" />
              <circle data-testid="spider-vertex" cx={x} cy={y} r={4.5} strokeWidth={2} className="fill-chart-earned stroke-chart-earned" />
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
        {comparing && compareLabel ? (
          <li className="flex items-center gap-1.5">
            <span className="w-4 border-t-2 border-dashed border-foreground/70" aria-hidden /> {compareLabel}
          </li>
        ) : null}
      </ul>
    </div>
  );
}

/** Rank per configured domain as a spider (three or more configured): now filled, the period's start dashed. */
export function RankSpider({ points, periodLabel: label }: { points: RankPoint[]; periodLabel: string }) {
  const top = Math.max(...points.map((p) => p.topRank), 1);
  return (
    <Spider
      title="Rank now"
      description={points
        .map((p) => (p.rankFrom === p.rankTo ? `${p.name}: ${p.titleTo}.` : `${p.name}: ${p.titleTo}, from ${p.titleFrom}.`))
        .join(" ")}
      rings={Array.from({ length: top }, (_, i) => (i + 1) / top)}
      ringLabel={(r) => (Math.round(r * top) % 2 === 0 ? `${Math.round(r * top)}` : "")}
      compareLabel={`${label} ago`}
      axes={points.map((p) => ({
        key: p.key,
        name: p.name,
        value: p.rankTo / top,
        detail: p.rankFrom === p.rankTo ? p.titleTo : `${p.titleTo} · from ${p.titleFrom}`,
        compare: p.rankFrom / top,
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
      description={points.map((p) => `${p.name}: level ${p.level}, ${p.rankTitle}; best ${p.best}.`).join(" ")}
      rings={ranks.map((level) => level / 100)}
      ringLabel={(r) => (Math.round(r * 100) % 20 === 0 ? `${Math.round(r * 100)}` : "")}
      compareLabel="Best level"
      axes={points.map((p) => ({
        key: p.key,
        name: p.name,
        value: p.level / 100,
        detail: `Level ${p.level} · ${p.rankTitle}`,
        compare: p.best / 100,
      }))}
    />
  );
}

/** Every domain's figures for the period. */
function ReportTable({
  rows,
  ladders,
  periodLabel: label,
}: {
  rows: DomainReportRow[];
  ladders: Map<string, string[]>;
  periodLabel: string;
}) {
  const dash = <span className="text-muted-foreground">—</span>;
  // Environment counts kept-up days and the average Home Score where Health counts active days
  // and base XP (environment plan §4.8), so the shared columns say which is which.
  const hasHome = rows.some((d) => d.kind === "home");
  const head = (text: string, right = true) => (
    <th scope="col" className={cn("px-3 py-2 font-medium", right && "text-right")}>
      {text}
    </th>
  );
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
            {head("Domain", false)}
            {head("Days counted")}
            {head("Exception days")}
            {head(hasHome ? "Active or kept-up days" : "Active days")}
            {head(hasHome ? "A day's average" : "Base XP a day")}
            {head("XP")}
            {head("Level")}
            {head("Rank", false)}
            {head("Best rank", false)}
            {head("New bests", false)}
          </tr>
        </thead>
        <tbody className="divide-y">
          {rows.map((d) => {
            const titles = ladders.get(d.key) ?? [];
            const bests = newBestsText(d);
            const rankFrom = d.rankFrom != null ? (titles[d.rankFrom] ?? `${d.rankFrom}`) : null;
            return (
              <tr key={d.key}>
                <th scope="row" className="px-3 py-2 text-left font-medium">
                  <span aria-hidden>{domainSymbol(d.key)} </span>
                  {d.name}
                  {d.configured ? null : <span className="block text-xs font-normal text-muted-foreground">XP only</span>}
                </th>
                <td className="px-3 py-2 text-right tabular-nums">{d.daysCounted ?? dash}</td>
                <td className="px-3 py-2 text-right tabular-nums">{d.exceptionDays ?? dash}</td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {d.activeDays != null && d.daysCounted
                    ? `${d.activeDays}${hasHome ? (d.kind === "home" ? " kept up" : " active") : ""} (${percent(d.activeShare ?? 0)})`
                    : dash}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {!d.daysCounted
                    ? dash
                    : d.kind === "home"
                      ? `Home Score ${shownPoints(d.averageHomeScore ?? 0)}`
                      : d.averageBase != null
                        ? `${d.averageBase.toLocaleString()}${hasHome ? " base XP" : ""}`
                        : dash}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {d.paid.toLocaleString()}
                  {d.base !== d.paid ? (
                    <span className="block text-xs text-muted-foreground">{d.base.toLocaleString()} base</span>
                  ) : null}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  {d.levelTo == null ? dash : d.levelFrom === d.levelTo ? d.levelTo : `${d.levelFrom} → ${d.levelTo}`}
                </td>
                <td className="px-3 py-2">
                  {d.rankTitle == null ? dash : rankFrom && rankFrom !== d.rankTitle ? `${rankFrom} → ${d.rankTitle}` : d.rankTitle}
                </td>
                <td className="px-3 py-2">{d.bestRankTitle ?? dash}</td>
                <td className="px-3 py-2">{bests ?? (d.configured ? <span className="text-muted-foreground">None</span> : dash)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
