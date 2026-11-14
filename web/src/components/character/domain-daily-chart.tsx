import { useId, useState } from "react";
import { DailyMetricChart, MetricTable, type MetricBar, type MetricColumn, type MetricLegendItem } from "@/components/charts/daily-metric-chart";
import { ReadFailed } from "@/components/primitives/read-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { formatDay } from "@/lib/dates";
import { barPlus } from "@/lib/domains";
import { shownPoints } from "@/lib/home-score";
import type { DomainDayRow } from "@/lib/production-ui-types";
import { useDomainDays } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { usePersistedChoice } from "@/lib/view-state";

const RANGES = [
  { id: "30", label: "30 days" },
  { id: "90", label: "90 days" },
  { id: "365", label: "1 year" },
] as const;
type Range = (typeof RANGES)[number]["id"];

const VIEW = "character.chart";

const EARNED = "bg-chart-earned";
const NOT_EARNED = "bg-chart-earned/35";
const SLEEP = "bg-chart-3/55";

function verdict(d: DomainDayRow, home = false): string {
  if (d.exception != null) return ` · exception: ${d.exception}`;
  if (home) {
    if (d.homeScore == null) return " · not rated yet";
    if (d.active == null) return "";
    return d.active ? ` · kept up (${barPlus(d.bar)} needed)` : ` · not kept up (${barPlus(d.bar)} needed)`;
  }
  if (d.active == null) return "";
  if (d.active) return d.activeMinimum != null ? ` · active (${d.activeMinimum}+ needed)` : " · active";
  return d.activeMinimum != null ? ` · not active (${d.activeMinimum} needed)` : " · not active";
}

/** A day in words, after its date: "90 action XP (108 paid) · active (20+ needed)". */
function reading(d: DomainDayRow, home = false): string {
  if (home) {
    const score = d.homeScore == null ? "no Home Score" : `a Home Score of ${shownPoints(d.homeScore)}`;
    return `${score}${verdict(d, true)}`;
  }
  const sleep = d.passive > 0 ? ` + ${d.passive.toLocaleString()} sleep` : "";
  const paid = d.paid !== d.base ? ` (${d.paid.toLocaleString()} paid)` : "";
  return `${d.action.toLocaleString()} action XP${sleep}${paid}${verdict(d)}`;
}

/**
 * A domain's daily chart (xp-domains plan §7.4.9), drawn by the shared `DailyMetricChart` (code
 * review CR86): bars of action XP per day, the full tone on active days and a lighter one
 * otherwise, with sleep (passive XP) stacked on top in its own tone. The active minimum is a
 * dashed step line, since it moves with the level, and exception days are hatched. Hover or the
 * arrow keys read any day; the table view lists them all. For a domain held by the home
 * (environment plan §4.8), the bars are each day's Home Score out of 100 and the step line is
 * the bar the day had to reach to be kept up.
 */
export function DomainDailyChart({ domain, name }: { domain: string; name: string }) {
  const [range, setRange] = usePersistedChoice<Range>(VIEW, "range", RANGES, "30");
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const days = useDomainDays(domain, Number(range));
  const titleId = useId();

  const data = days.data;
  const rows = data?.days ?? [];
  const configured = data?.configured ?? false;
  const home = data?.kind === "home";
  const active = rows.filter((d) => d.active).length;
  const exceptions = rows.filter((d) => d.exception != null).length;
  const label = RANGES.find((r) => r.id === range)!.label;
  const hasSleep = rows.some((d) => d.passive > 0);
  const lastRow = rows.at(-1);
  const lastMinimum = configured && lastRow ? (home ? (lastRow.bar ?? null) : (lastRow.activeMinimum ?? null)) : null;
  const what = home ? "Home Score" : "XP";
  const word = home ? "kept up" : "active";

  // What each bar shows and the line it has to reach: action XP against the active minimum, or
  // the Home Score against the bar.
  const bars: MetricBar[] = rows.map((d, i) => ({
    key: d.day,
    value: home ? (d.homeScore ?? null) : d.action,
    tone: d.active || !configured ? EARNED : NOT_EARNED,
    stack: !home && d.passive > 0 ? { value: d.passive, tone: SLEEP } : undefined,
    marked: d.exception != null,
    title: formatDay(d.day),
    text: reading(d, home),
    axis: i === rows.length - 1 ? "Today" : formatDay(d.day),
  }));
  const steps = configured ? rows.map((d) => (home ? d.bar : d.activeMinimum) ?? null) : undefined;
  const legend: MetricLegendItem[] = [
    { label: home ? "Home Score, a kept-up day" : configured ? "Action XP, an active day" : "XP", kind: "bar", tone: EARNED },
    ...(configured
      ? [{ label: home ? "Home Score, not kept up" : "Action XP, not active", kind: "bar", tone: NOT_EARNED } as const]
      : []),
    ...(!home && hasSleep ? [{ label: "Sleep (passive)", kind: "bar", tone: SLEEP } as const] : []),
    ...(configured ? [{ label: home ? "The bar" : "Active minimum", kind: "dashed" } as const] : []),
    ...(exceptions > 0 ? [{ label: "Exception day", kind: "marked" } as const] : []),
  ];

  const columns: MetricColumn[] = [
    { key: "day", label: "Day" },
    ...(home
      ? [
          { key: "score", label: "Home Score", numeric: true },
          { key: "paid", label: "XP paid", numeric: true },
        ]
      : [
          { key: "action", label: "Action XP", numeric: true },
          { key: "sleep", label: "Sleep", numeric: true },
          { key: "paid", label: "Paid", numeric: true },
        ]),
    ...(configured
      ? [
          { key: "active", label: home ? "Kept up" : "Active", numeric: true },
          { key: "exception", label: "Exception" },
        ]
      : []),
  ];
  const tableRows = [...rows].reverse().map((d) => ({
    key: d.day,
    cells: [
      formatDay(d.day),
      ...(home
        ? [d.homeScore == null ? "—" : shownPoints(d.homeScore), d.paid.toLocaleString()]
        : [d.action.toLocaleString(), d.passive.toLocaleString(), d.paid.toLocaleString()]),
      ...(configured
        ? [
            d.exception != null || (home && d.homeScore == null)
              ? "—"
              : d.active
                ? "Yes"
                : home
                  ? `No (${barPlus(d.bar)} needed)`
                  : `No (${d.activeMinimum ?? 0} needed)`,
            d.exception ?? "",
          ]
        : []),
    ],
  }));

  return (
    <section aria-labelledby={titleId} className="space-y-3 border-t px-4 py-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id={titleId} className="type-scale-section text-muted-foreground">
          {name} {what} by day
        </h3>
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

      {days.isLoading ? <ListSkeleton rows={3} /> : null}
      <ReadFailed read={days} what="The daily chart" />

      {data ? (
        <div className={cn("space-y-2 transition-opacity", days.isPlaceholderData && "opacity-60")}>
          {configured ? (
            <p className="text-sm">
              <span className="font-medium">
                {active} of the last {rows.length} days {word}
              </span>
              {exceptions > 0 ? (
                <span className="text-muted-foreground">
                  {" "}
                  · {exceptions} exception {exceptions === 1 ? "day" : "days"}
                </span>
              ) : null}
            </p>
          ) : null}

          {mode === "chart" ? (
            <DailyMetricChart
              label={`${name} ${what} by day, last ${label}`}
              bars={bars}
              steps={steps}
              stepsLabel={lastMinimum != null ? (home ? barPlus(lastMinimum) : lastMinimum.toLocaleString()) : undefined}
              legend={legend}
              top={home ? 100 : undefined}
            />
          ) : (
            <MetricTable
              label={`${name} ${what} by day, as a table`}
              caption={`${name} ${what} by day, last ${label}, newest first`}
              columns={columns}
              rows={tableRows}
            />
          )}
        </div>
      ) : null}
    </section>
  );
}
