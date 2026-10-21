import { useMemo, useState } from "react";
import { CalendarDaysIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { addDays, formatDay } from "@/lib/dates";
import { DIET_CAP, DIET_RULES, formatDuration, formatPart } from "@/lib/diet-score";
import { multiplierLabel } from "@/lib/domains";
import { dayInWords, dayStanding, dietHeat, foodTotals, type FoodPeriod } from "@/lib/food-period";
import type { FoodDaySummary } from "@/lib/production-ui-types";
import { useFoodDays } from "@/lib/queries";
import { monthCells } from "@/lib/rewards-history";
import { cn } from "@/lib/utils";

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/** The calendar's shade by base out of 40: one hue, four steps (level 0 is no fill). */
const HEAT = ["", "bg-chart-earned/10", "bg-chart-earned/20", "bg-chart-earned/32", "bg-chart-earned/45"];

/** A day's three parts, stacked in this order (the colours are validated in `index.css`). */
const PARTS = [
  { key: "quality", label: "Quality", max: DIET_RULES.qualityMax, fill: "bg-diet-quality" },
  { key: "caloriesPart", label: "Calories", max: DIET_RULES.caloriesMax, fill: "bg-diet-calories" },
  { key: "fasting", label: "Fasting", max: DIET_RULES.fastingMax, fill: "bg-diet-fasting" },
] as const;

/** "Mon 19". */
function shortDay(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  return `${new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: "short" })} ${d}`;
}

/** What a day shows as its number: paid once closed, on track while open, a dash ahead. */
function dayFigure(day: FoodDaySummary, today: string): { value: string; note: string } {
  const standing = dayStanding(day, today);
  if (standing.kind === "future") return { value: "—", note: "" };
  if (standing.kind === "open") return { value: String(standing.base), note: "on track" };
  return { value: String(standing.paid), note: standing.multiplier ? multiplierLabel(standing.multiplier) : "paid" };
}

function dayFacts(day: FoodDaySummary): string {
  if (day.meals === 0 && !day.longestFastSeconds) return "Nothing logged";
  return [
    `${day.calories.toLocaleString()} cal`,
    day.averageScore !== null ? `score ${formatPart(day.averageScore)}` : null,
    day.longestFastSeconds ? `fast ${formatDuration(day.longestFastSeconds)}` : null,
  ]
    .filter(Boolean)
    .join(" · ");
}

/**
 * The Food Log's Week and Month views (FOOD-4, food-log plan §3.7): the period's totals, then
 * the week as a row per day with its diet XP stacked by part out of 40, or the month as a
 * Monday-first calendar shaded by base; either as a table. Picking a day opens it.
 */
export function FoodPeriodView({
  period,
  today,
  onOpenDay,
}: {
  period: FoodPeriod;
  today: string;
  onOpenDay: (date: string) => void;
}) {
  const range = useFoodDays(period.from, period.to);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const days = useMemo(() => range.data?.days ?? [], [range.data]);
  const stale = range.isFetching && range.isPlaceholderData;
  const unit = period.view === "month" ? "month" : "week";

  if (range.isLoading) {
    return (
      <div className="space-y-4">
        <CardSkeleton />
        <CardSkeleton />
      </div>
    );
  }
  if (range.isError) {
    return (
      <EmptyState
        tone="error"
        icon={CalendarDaysIcon}
        title={`This ${unit} failed to load`}
        description="This is not an empty period — the reader never returned."
      />
    );
  }

  return (
    <div className={cn("space-y-6 transition-opacity", stale && "opacity-60")} aria-busy={stale || undefined}>
      <PeriodTotals days={days} today={today} unit={unit} />

      <section aria-labelledby="period-title" className="min-w-0 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 id="period-title" className="type-scale-section text-muted-foreground">
            Diet XP by day
          </h2>
          <ToggleGroup
            label="Show as"
            value={mode}
            onChange={setMode}
            options={[
              { id: "chart", label: period.view === "month" ? "Calendar" : "Chart" },
              { id: "table", label: "Table" },
            ]}
          />
        </div>
        {mode === "table" ? (
          <PeriodTable days={days} today={today} onOpenDay={onOpenDay} />
        ) : period.view === "month" ? (
          <MonthCalendar from={period.from} days={days} today={today} onOpenDay={onOpenDay} />
        ) : (
          <WeekChart days={days} today={today} onOpenDay={onOpenDay} />
        )}
      </section>
    </div>
  );
}

function PeriodTotals({ days, today, unit }: { days: FoodDaySummary[]; today: string; unit: string }) {
  const t = foodTotals(days, today);
  const stats = [
    { label: "Diet XP paid", value: `${t.paid.toLocaleString()} Health XP` },
    { label: "A closed day", value: t.averagePaid !== null ? `${t.averagePaid} on average` : "None closed yet" },
    {
      label: "Calories",
      value: t.averageCalories !== null ? `${t.averageCalories.toLocaleString()} a logged day` : "Nothing logged",
    },
    { label: "Days closed", value: `${t.closedDays} of ${t.daysSoFar}` },
  ];
  return (
    <dl
      aria-label={`Totals for this ${unit}`}
      className="grid grid-cols-2 gap-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10 md:grid-cols-4"
    >
      {stats.map((s) => (
        <div key={s.label}>
          <dt className="text-xs text-muted-foreground">{s.label}</dt>
          <dd className="font-heading text-lg tabular-nums">{s.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function Legend() {
  return (
    <ul aria-label="Legend" className="mb-3 flex flex-wrap gap-x-3 gap-y-1 text-[0.6875rem] text-muted-foreground">
      {PARTS.map((p) => (
        <li key={p.key} className="flex items-center gap-1.5">
          <span className={cn("size-2.5 rounded-[2px]", p.fill)} aria-hidden /> {p.label} (up to {p.max})
        </li>
      ))}
    </ul>
  );
}

/** A row per day: its parts stacked along a track out of 40, then what it paid. */
function WeekChart({
  days,
  today,
  onOpenDay,
}: {
  days: FoodDaySummary[];
  today: string;
  onOpenDay: (date: string) => void;
}) {
  return (
    <div>
      <Legend />
      <ul aria-label="Diet XP each day, out of 40" className="space-y-1">
        {days.map((d) => {
          const standing = dayStanding(d, today);
          const figure = dayFigure(d, today);
          const filled = PARTS.map((p) => ({ ...p, value: d[p.key] })).filter((p) => p.value > 0);
          const used = filled.reduce((s, p) => s + p.value, 0);
          const isToday = d.day === today;
          return (
            <li key={d.day}>
              <Tooltip>
                <TooltipTrigger asChild>
                  <button
                    type="button"
                    aria-label={dayInWords(d, today)}
                    disabled={standing.kind === "future"}
                    onClick={() => onOpenDay(d.day)}
                    className="grid w-full grid-cols-[3.5rem_minmax(0,1fr)_3.5rem] items-center gap-x-3 rounded-lg px-2 py-2 text-left transition-colors hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50 disabled:hover:bg-transparent"
                  >
                    <span className={cn("text-sm", isToday && "font-semibold")}>{shortDay(d.day)}</span>
                    <span className="flex h-2.5 w-full min-w-0 gap-0.5" aria-hidden>
                      {filled.map((p, i) => (
                        <span
                          key={p.key}
                          className={cn(
                            "h-full",
                            p.fill,
                            standing.kind === "open" && "opacity-60",
                            i === filled.length - 1 && "rounded-r-[4px]",
                          )}
                          style={{ width: `${(p.value / DIET_CAP) * 100}%` }}
                        />
                      ))}
                      {used < DIET_CAP ? <span className="h-full flex-1 rounded-r-[4px] bg-muted" /> : null}
                    </span>
                    <span className="text-right leading-tight">
                      <span className="block text-sm font-medium tabular-nums">{figure.value}</span>
                      {figure.note ? (
                        <span className="block text-[0.625rem] text-muted-foreground">{figure.note}</span>
                      ) : null}
                    </span>
                    <span className="col-start-2 col-end-4 text-[0.6875rem] text-muted-foreground">
                      {standing.kind === "future" ? "" : dayFacts(d)}
                    </span>
                  </button>
                </TooltipTrigger>
                {standing.kind === "future" ? null : (
                  <TooltipContent side="top">
                    <span className="font-medium">{formatDay(d.day)}</span>
                    <span>
                      {PARTS.map((p) => `${p.label} ${formatPart(d[p.key])}`).join(" · ")}
                    </span>
                    <span>
                      {standing.kind === "open"
                        ? `On track for ${standing.base} of ${DIET_CAP}`
                        : `${standing.base} base → ${standing.paid} Health XP`}
                    </span>
                  </TooltipContent>
                )}
              </Tooltip>
            </li>
          );
        })}
      </ul>
      <p className="mt-2 text-[0.6875rem] text-muted-foreground">
        Each bar is the day's base diet XP out of {DIET_CAP}; the number is what it paid, after the multiplier.
        Today is on track until it closes. Pick a day to open it.
      </p>
    </div>
  );
}

/** A Monday-first calendar, each day shaded by its base out of 40 and showing what it paid. */
function MonthCalendar({
  from,
  days,
  today,
  onOpenDay,
}: {
  from: string;
  days: FoodDaySummary[];
  today: string;
  onOpenDay: (date: string) => void;
}) {
  const byDate = useMemo(() => new Map(days.map((d) => [d.day, d])), [days]);
  return (
    <div>
      <div className="mb-2 flex items-center justify-end gap-1.5 text-[0.6875rem] text-muted-foreground" aria-hidden>
        0
        {HEAT.map((heat, i) => (
          <span key={i} className={cn("size-3 rounded-[3px] ring-1 ring-foreground/10", heat)} />
        ))}
        {DIET_CAP} base
      </div>
      <div className="grid grid-cols-7 gap-1" role="group" aria-label="Diet XP each day">
        {WEEKDAYS.map((w) => (
          <div key={w} className="text-center text-[0.625rem] text-muted-foreground" aria-hidden>
            {w}
          </div>
        ))}
        {monthCells(from).map((date, i) => {
          if (!date) return <div key={`blank-${i}`} aria-hidden />;
          const d = byDate.get(date);
          if (!d) return <div key={date} aria-hidden />;
          const standing = dayStanding(d, today);
          const figure = dayFigure(d, today);
          const base = standing.kind === "future" ? 0 : standing.base;
          const isToday = date === today;
          return (
            <button
              key={date}
              type="button"
              aria-label={dayInWords(d, today)}
              disabled={standing.kind === "future"}
              onClick={() => onOpenDay(date)}
              className={cn(
                "flex min-h-14 flex-col justify-between rounded-md p-1 text-left text-foreground ring-1 ring-foreground/8 transition-colors md:min-h-16 md:p-1.5",
                "hover:ring-foreground/30 focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-40",
                HEAT[dietHeat(base)],
              )}
            >
              <span className={cn("text-[0.625rem] leading-none", isToday && "font-bold underline underline-offset-2")}>
                {Number(date.slice(8))}
              </span>
              {standing.kind !== "future" && (d.meals > 0 || d.paid.total > 0 || base > 0) ? (
                <span className="flex flex-col items-end leading-tight tabular-nums" aria-hidden>
                  <span className="text-xs font-medium">{figure.value}</span>
                  {standing.kind === "open" ? <span className="text-[0.625rem]">on track</span> : null}
                </span>
              ) : null}
            </button>
          );
        })}
      </div>
      <p className="mt-2 text-[0.6875rem] text-muted-foreground">
        Each day is shaded by its base diet XP out of {DIET_CAP} and shows what it paid. Pick a day to open it.
      </p>
    </div>
  );
}

function PeriodTable({
  days,
  today,
  onOpenDay,
}: {
  days: FoodDaySummary[];
  today: string;
  onOpenDay: (date: string) => void;
}) {
  const shown = days.filter((d) => d.day <= today);
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[32rem] text-sm">
        <caption className="sr-only">Diet XP each day, oldest first</caption>
        <thead>
          <tr className="text-left text-xs text-muted-foreground">
            <th scope="col" className="py-1 font-medium">
              Day
            </th>
            <th scope="col" className="py-1 text-right font-medium">
              Base
            </th>
            <th scope="col" className="py-1 text-right font-medium">
              Paid
            </th>
            <th scope="col" className="py-1 text-right font-medium">
              Calories
            </th>
            <th scope="col" className="py-1 text-right font-medium">
              Score
            </th>
            <th scope="col" className="py-1 text-right font-medium">
              Longest fast
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {shown.map((d) => {
            const standing = dayStanding(d, today);
            return (
              <tr key={d.day}>
                <th scope="row" className="py-1.5 text-left font-normal">
                  <button
                    type="button"
                    className="underline-offset-2 hover:underline focus-visible:underline"
                    onClick={() => onOpenDay(d.day)}
                  >
                    {formatDay(d.day)}
                    {d.day === today ? " (today)" : ""}
                  </button>
                </th>
                <td className="py-1.5 text-right tabular-nums">{d.total}</td>
                <td className="py-1.5 text-right tabular-nums">
                  {standing.kind === "paid" ? standing.paid : "on track"}
                </td>
                <td className="py-1.5 text-right tabular-nums">{d.meals > 0 ? d.calories.toLocaleString() : "—"}</td>
                <td className="py-1.5 text-right tabular-nums">
                  {d.averageScore !== null ? formatPart(d.averageScore) : "—"}
                </td>
                <td className="py-1.5 text-right tabular-nums">
                  {d.longestFastSeconds ? formatDuration(d.longestFastSeconds) : "—"}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {shown.length < days.length ? (
        <p className="mt-2 text-[0.6875rem] text-muted-foreground">
          Days after today aren't listed; the {days.length > 7 ? "month" : "week"} ends {formatDay(addDays(days[0].day, days.length - 1))}.
        </p>
      ) : null}
    </div>
  );
}
