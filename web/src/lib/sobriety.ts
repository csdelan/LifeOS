/**
 * The sobriety streak (FOOD-6, food-log plan §7.3), ported from `Sobriety` in the kernel's
 * Domain so the mock answers as the API does, and its words on the page.
 *
 * - A drink-free day is a day with no alcoholic entry; a day with nothing logged counts.
 * - The streak counts completed days: a day counts once it's over, so today never does.
 * - The streak on a day is the drink-free days in a row from the day after the last drink, or
 *   from the Food Log's first day when there's been none, to the day itself once it's over or
 *   to yesterday while it's today. A day with a drink has a streak of 0 at once.
 * - The best is the longest completed run from the first day to that day. Before the first
 *   day there's no streak.
 */
import { addDays, formatDay } from "@/lib/dates";
import type { SobrietyRow } from "@/lib/production-ui-types";

/** Days since 1970-01-01 for a `yyyy-MM-dd` day. */
function dayNumber(day: string): number {
  const [y, m, d] = day.split("-").map(Number);
  return Math.round(Date.UTC(y, m - 1, d) / 86_400_000);
}

function dayAfter(day: string): string {
  return new Date((dayNumber(day) + 1) * 86_400_000).toISOString().slice(0, 10);
}

/** The streak as of `asOf` seen on `today`, counted from `firstDay` over the days with a drink; null before the first day. */
export function sobrietyOn(
  asOf: string,
  today: string,
  firstDay: string,
  drinkDays: Iterable<string>,
): SobrietyRow | null {
  if (asOf < firstDay) return null;
  const drinks = [...new Set(drinkDays)].filter((d) => d >= firstDay && d <= asOf).sort();
  let best = 0;
  let runStart = firstDay;
  for (const day of drinks) {
    best = Math.max(best, dayNumber(day) - dayNumber(runStart));
    runStart = dayAfter(day);
  }
  // The last day that's over: the day itself once it's past, else yesterday. A drink on the
  // day puts the run's start after it, so the streak is 0.
  const lastDone = asOf < today ? asOf : addDays(today, -1);
  const days = Math.max(0, dayNumber(lastDone) - dayNumber(runStart) + 1);
  return {
    asOf,
    days,
    bestDays: Math.max(best, days),
    since: runStart,
    lastDrinkOn: drinks.length > 0 ? drinks[drinks.length - 1] : null,
    firstDay,
  };
}

/** Whether the day itself has a drink, which is what makes its streak 0 at once. */
export function drinkThatDay(streak: SobrietyRow): boolean {
  return streak.lastDrinkOn === streak.asOf;
}

/** "1 day", "12 days". */
export function daysText(days: number): string {
  return `${days.toLocaleString()} day${days === 1 ? "" : "s"}`;
}

/** The streak's headline: "12 days sober" ("0 days sober" the day after a drink), or "A drink today" / "A drink that day". */
export function sobrietyHeadline(streak: SobrietyRow, today: string): string {
  if (drinkThatDay(streak)) return streak.asOf === today ? "A drink today" : "A drink that day";
  return `${daysText(streak.days)} sober`;
}

/** Under the headline: "Drink-free since Oct 14 · best 30 days", or "Best 30 days" on a day with a drink. */
export function sobrietyDetail(streak: SobrietyRow): string {
  const best = `best ${daysText(streak.bestDays)}`;
  if (drinkThatDay(streak)) return `${best[0].toUpperCase()}${best.slice(1)}`;
  return `Drink-free since ${formatDay(streak.since)} · ${best}`;
}

/** In a sentence: "Sober 12 days (best 30 days)", or "A drink today (best 30 days)". */
export function sobrietyText(streak: SobrietyRow, today: string): string {
  const best = `best ${daysText(streak.bestDays)}`;
  if (drinkThatDay(streak)) return `${sobrietyHeadline(streak, today)} (${best})`;
  return `Sober ${daysText(streak.days)} (${best})`;
}
