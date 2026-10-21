/**
 * The Food Log's Week and Month views (FOOD-4, food-log plan §3.7): which days a view covers,
 * how ‹ › step through them, what to call them, and their totals. Weeks run Monday to Sunday.
 */
import { addDays, addMonths, daysInMonth, formatDay, formatDayFull, startOfMonth, startOfWeekMonday } from "@/lib/dates";
import { DIET_CAP, formatDuration } from "@/lib/diet-score";
import type { FoodDaySummary } from "@/lib/production-ui-types";
import { monthLabel } from "@/lib/rewards-history";

export type FoodView = "day" | "week" | "month";

export interface FoodPeriod {
  view: FoodView;
  from: string;
  to: string;
}

/** The day, the Monday-to-Sunday week, or the month around `date`. */
export function foodPeriod(view: FoodView, date: string): FoodPeriod {
  if (view === "week") {
    const from = startOfWeekMonday(date);
    return { view, from, to: addDays(from, 6) };
  }
  if (view === "month") {
    const from = startOfMonth(date);
    return { view, from, to: addDays(from, daysInMonth(from) - 1) };
  }
  return { view, from: date, to: date };
}

/** The date ‹ or › lands on: a day, a week or a month away (a month from its first day). */
export function stepFoodDate(view: FoodView, date: string, direction: 1 | -1): string {
  if (view === "week") return addDays(startOfWeekMonday(date), 7 * direction);
  if (view === "month") return addMonths(startOfMonth(date), direction);
  return addDays(date, direction);
}

/** Whether the period holds today, so › has nowhere to go. */
export function holdsToday(period: FoodPeriod, today: string): boolean {
  return period.from <= today && today <= period.to;
}

/** "Today", "Tue, Oct 13, 2026", "Week of Oct 19", "October 2026". */
export function foodPeriodTitle(period: FoodPeriod, today: string): string {
  if (period.view === "week") return `Week of ${formatDay(period.from)}`;
  if (period.view === "month") return monthLabel(period.from);
  return period.from === today ? "Today" : formatDayFull(period.from);
}

/** What a day stands at: paid once it closes, on track while it's open, nothing ahead. */
export type DayStanding =
  | { kind: "future" }
  | { kind: "open"; base: number }
  | { kind: "paid"; base: number; paid: number; multiplier: number | null };

export function dayStanding(day: FoodDaySummary, today: string): DayStanding {
  if (day.day > today) return { kind: "future" };
  if (!day.closed) return { kind: "open", base: day.total };
  return {
    kind: "paid",
    base: day.paid.baseTotal,
    paid: day.paid.total,
    multiplier: day.paid.diet?.multiplier ?? day.paid.fast?.multiplier ?? null,
  };
}

/** "Tue, Oct 20, 2026 (today): on track for 35 of 40 · 2 entries, 1,200 cal, average score 5". */
export function dayInWords(day: FoodDaySummary, today: string): string {
  const standing = dayStanding(day, today);
  const when = `${formatDayFull(day.day)}${day.day === today ? " (today)" : ""}`;
  if (standing.kind === "future") return `${when}: ahead`;
  const score =
    standing.kind === "open"
      ? `on track for ${standing.base} of ${DIET_CAP}`
      : `${standing.paid} Health XP paid (${standing.base} base)`;
  if (day.meals === 0 && !day.longestFastSeconds) return `${when}: ${score} · nothing logged`;
  const facts = [
    `${day.meals} entr${day.meals === 1 ? "y" : "ies"}`,
    `${day.calories.toLocaleString()} cal`,
    day.averageScore !== null ? `average score ${roundTenth(day.averageScore)}` : null,
    day.longestFastSeconds ? `fasted ${formatDuration(day.longestFastSeconds)}` : null,
  ].filter(Boolean);
  return `${when}: ${score} · ${facts.join(", ")}`;
}

function roundTenth(n: number): string {
  const r = Math.round(n * 10) / 10;
  return Number.isInteger(r) ? String(r) : r.toFixed(1);
}

export interface FoodTotals {
  /** Health XP paid over the period, after multipliers. */
  paid: number;
  /** Days that closed (by hand or because they're over), and days up to today. */
  closedDays: number;
  daysSoFar: number;
  /** Paid per closed day, or null with none closed. */
  averagePaid: number | null;
  /** Days with an entry, and their average calories. */
  loggedDays: number;
  averageCalories: number | null;
}

export function foodTotals(days: readonly FoodDaySummary[], today: string): FoodTotals {
  const soFar = days.filter((d) => d.day <= today);
  const closed = soFar.filter((d) => d.closed);
  const logged = soFar.filter((d) => d.meals > 0);
  const paid = soFar.reduce((sum, d) => sum + d.paid.total, 0);
  return {
    paid,
    closedDays: closed.length,
    daysSoFar: soFar.length,
    averagePaid: closed.length > 0 ? Math.round(closed.reduce((s, d) => s + d.paid.total, 0) / closed.length) : null,
    loggedDays: logged.length,
    averageCalories:
      logged.length > 0 ? Math.round(logged.reduce((s, d) => s + d.calories, 0) / logged.length) : null,
  };
}

/** The calendar's shade for a day's base out of 40: none, then one step per 10. */
export function dietHeat(base: number): 0 | 1 | 2 | 3 | 4 {
  if (base <= 0) return 0;
  return Math.min(4, Math.ceil((base / DIET_CAP) * 4)) as 1 | 2 | 3 | 4;
}
