/**
 * The rewards history (REWARD-5): what was earned and spent by day, week and month.
 *
 * The kernel totals each local day (`GET /api/ledger/daily`, in the viewer's time zone);
 * weeks and months are roll-ups of those days here, so every figure on the page agrees
 * with the days it is made of. "Earned" on the calendar and the chart is net of what was
 * given back: a task completed and reopened on the same day earned nothing that day, and a
 * correction made later shows on the day it was made, so a past day never silently changes.
 */
import {
  addDays,
  addMonths,
  daysInMonth,
  formatDay,
  formatDayFull,
  localDateFromTimestamp,
  startOfMonth,
  startOfWeekMonday,
  weekdayIndex,
} from "@/lib/dates";
import type { Amounts, LedgerDay, LedgerEntryKind, LedgerRow } from "@/lib/production-ui-types";
import { currencyLabel, orderedAmounts, triggerLabel } from "@/lib/rewards";

export type HistoryBucket = "week" | "month";

/** How many weeks or months the earned-vs-spent chart shows. */
export const BUCKET_COUNT: Record<HistoryBucket, number> = { week: 8, month: 6 };

/** A stretch of local days [start, end), `YYYY-MM-DD`. */
export interface Period {
  kind: "day" | "week" | "month";
  start: string;
  end: string;
}

/** A period's totals: positive earned / reversed / spent, and the signed `net`. */
export interface PeriodTotals extends Period {
  earned: Amounts;
  reversed: Amounts;
  spent: Amounts;
  net: Amounts;
}

/** The viewer's IANA time zone, which the kernel files days in; UTC when unknown. */
export function viewerTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

/** Local midnight at the start of `date`, as an ISO instant: a ledger range end. */
export function localMidnightIso(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(y, m - 1, d).toISOString();
}

export function dayPeriod(date: string): Period {
  return { kind: "day", start: date, end: addDays(date, 1) };
}

/**
 * The Monday-to-Sunday week holding `date`. (The weekly review, REVIEW-2, keeps its own
 * Sunday-start week: Sunday is review day, but the week itself runs Monday to Sunday.)
 */
export function weekPeriod(date: string): Period {
  const start = startOfWeekMonday(date);
  return { kind: "week", start, end: addDays(start, 7) };
}

export function monthPeriod(date: string): Period {
  const start = startOfMonth(date);
  return { kind: "month", start, end: addMonths(start, 1) };
}

export function samePeriod(a: Period, b: Period): boolean {
  return a.kind === b.kind && a.start === b.start;
}

export function inPeriod(period: Period, date: string): boolean {
  return date >= period.start && date < period.end;
}

function monthName(date: string, month: "long" | "short", withYear: boolean): string {
  const [y, m] = date.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(undefined, withYear ? { month, year: "numeric" } : { month });
}

/** "October 2026". */
export function monthLabel(date: string): string {
  return monthName(date, "long", true);
}

/** "Tue, Oct 14, 2026" · "Week of Oct 5" · "October 2026". */
export function periodLabel(period: Period): string {
  if (period.kind === "day") return formatDayFull(period.start);
  if (period.kind === "week") return `Week of ${formatDay(period.start)}`;
  return monthLabel(period.start);
}

/** The chart's axis label: "10/5" for a week, "Oct" for a month. */
export function shortPeriodLabel(period: Period): string {
  if (period.kind === "month") return monthName(period.start, "short", false);
  const [y, m, d] = period.start.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: "numeric", day: "numeric" });
}

/**
 * The chart's periods: `count` weeks or months, oldest first, the last holding `anchor`.
 */
export function periodsEnding(bucket: HistoryBucket, anchor: string, count = BUCKET_COUNT[bucket]): Period[] {
  const last = bucket === "week" ? weekPeriod(anchor) : monthPeriod(anchor);
  return Array.from({ length: count }, (_, i) => {
    const back = count - 1 - i;
    return bucket === "week"
      ? weekPeriod(addDays(last.start, -7 * back))
      : monthPeriod(addMonths(last.start, -back));
  });
}

/** The chart ends on today while viewing this month, otherwise on the month's last day. */
export function chartAnchor(month: string, today: string): string {
  const viewed = monthPeriod(month);
  return inPeriod(viewed, today) ? today : addDays(viewed.end, -1);
}

/**
 * The days to read so the month's calendar and both charts (weeks and months) are covered:
 * one read, so switching between weeks and months never waits.
 */
export function historyRange(month: string, today: string): { from: string; to: string } {
  const anchor = chartAnchor(month, today);
  const viewed = monthPeriod(month);
  const weeks = periodsEnding("week", anchor);
  const months = periodsEnding("month", anchor);
  const starts = [viewed.start, weeks[0].start, months[0].start].sort();
  const ends = [viewed.end, weeks.at(-1)!.end, months.at(-1)!.end].sort();
  return { from: starts[0], to: ends.at(-1)! };
}

/**
 * The month's calendar, Monday first so each row is one of the chart's weeks: blanks before
 * the 1st and after the last day.
 */
export function monthCells(month: string): (string | null)[] {
  const first = startOfMonth(month);
  const cells: (string | null)[] = [
    ...Array.from({ length: (weekdayIndex(first) + 6) % 7 }, () => null),
    ...Array.from({ length: daysInMonth(first) }, (_, i) => addDays(first, i)),
  ];
  while (cells.length % 7 !== 0) cells.push(null);
  return cells;
}

/** a + b + …, currency by currency, zeros dropped, in display order. */
export function sumAmounts(...list: Amounts[]): Amounts {
  const out: Amounts = {};
  for (const amounts of list) {
    for (const [key, n] of Object.entries(amounts)) out[key] = (out[key] ?? 0) + n;
  }
  return Object.fromEntries(orderedAmounts(out));
}

export function negate(amounts: Amounts): Amounts {
  return Object.fromEntries(Object.entries(amounts).map(([k, n]) => [k, -n]));
}

/** The totals of the days inside `period`. */
export function totalsFor(days: readonly LedgerDay[], period: Period): PeriodTotals {
  const inside = days.filter((d) => inPeriod(period, d.date));
  return {
    ...period,
    earned: sumAmounts(...inside.map((d) => d.earned)),
    reversed: sumAmounts(...inside.map((d) => d.reversed)),
    spent: sumAmounts(...inside.map((d) => d.spent)),
    net: sumAmounts(...inside.map((d) => d.net)),
  };
}

/** What was earned in one currency, net of what was given back. */
export function netEarned(totals: Pick<LedgerDay, "earned" | "reversed">, currency: string): number {
  return (totals.earned[currency] ?? 0) - (totals.reversed[currency] ?? 0);
}

/** 0 (nothing) to 4: where `value` sits against the highest value shown. */
export function heatLevel(value: number, max: number): 0 | 1 | 2 | 3 | 4 {
  if (value <= 0 || max <= 0) return 0;
  return Math.min(4, Math.max(1, Math.ceil((value / max) * 4))) as 1 | 2 | 3 | 4;
}

/** A clean axis top for `value`: 1, 2 or 5 times a power of ten, never below it. */
export function niceMax(value: number): number {
  if (value <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 5, 10]) {
    if (step * power >= value) return step * power;
  }
  return 10 * power;
}

/** "35", "1,250", "12K": short enough for a calendar cell. */
export function compact(n: number): string {
  return Math.abs(n) >= 10_000
    ? n.toLocaleString(undefined, { notation: "compact", maximumFractionDigits: 1 })
    : n.toLocaleString();
}

function wordsFor(amounts: Amounts): string {
  return orderedAmounts(amounts)
    .map(([k, n]) => `${Math.abs(n).toLocaleString()} ${currencyLabel(k)}`)
    .join(", ");
}

/**
 * A day, week or month in words, for screen readers and tooltips:
 * "earned 35 XP, 7 Coins; gave back 3 XP; spent 45 Freedom", or "nothing earned or spent".
 */
export function totalsInWords(totals: Pick<LedgerDay, "earned" | "reversed" | "spent">): string {
  const parts: string[] = [];
  if (Object.keys(totals.earned).length) parts.push(`earned ${wordsFor(totals.earned)}`);
  if (Object.keys(totals.reversed).length) parts.push(`gave back ${wordsFor(totals.reversed)}`);
  if (Object.keys(totals.spent).length) parts.push(`spent ${wordsFor(totals.spent)}`);
  return parts.length ? parts.join("; ") : "nothing earned or spent";
}

// ---------------------------------------------------------------------------
// The ledger behind a day, week or month.
// ---------------------------------------------------------------------------

export type EntryFilter = "all" | Exclude<LedgerEntryKind, "open">;

/**
 * The entries a filter keeps: one kind (earned / given back / spent) and one currency,
 * or all. The wallet's opening carries no amounts, so it shows only unfiltered.
 */
export function filterLedger(rows: readonly LedgerRow[], entry: EntryFilter, currency: string): LedgerRow[] {
  return rows.filter((row) => {
    if (entry !== "all" && row.entry !== entry) return false;
    if (currency !== "all" && !row.amounts[currency]) return false;
    return true;
  });
}

/** Entries grouped by the local day they happened, newest day first; rows keep their order. */
export function groupByDay(rows: readonly LedgerRow[]): { date: string; rows: LedgerRow[] }[] {
  const groups = new Map<string, LedgerRow[]>();
  for (const row of rows) {
    const date = localDateFromTimestamp(row.occurredAt);
    const group = groups.get(date);
    if (group) group.push(row);
    else groups.set(date, [row]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([date, list]) => ({ date, rows: list }));
}

/** The dollars that moved with this range's redemptions (Coins are real money). */
export function dollarsMoved(rows: readonly LedgerRow[]): number {
  const cents = rows.reduce(
    (sum, row) => sum + (row.entry === "redeem" && row.dollars ? Math.round(row.dollars * 100) : 0),
    0,
  );
  return cents / 100;
}

/** The second line of a ledger entry: what it was for. */
export function entryDetail(row: LedgerRow): string {
  const why = row.trigger ? triggerLabel(row.trigger) : "";
  switch (row.entry) {
    case "open":
      return "nothing done before this pays";
    case "earn":
      return why || "earned";
    case "reversal":
      return why ? `given back · ${why} undone` : "given back";
    case "redeem":
      if (!row.subject) return "custom redemption";
      return row.note ? `redeemed — ${row.note}` : "redeemed";
    default:
      return "";
  }
}

/** The first line of an entry without an item: the wallet opening, or a custom redemption's note. */
export function entryTitle(row: LedgerRow): string {
  if (row.entry === "open") return "Wallet opened";
  if (row.entry === "redeem") return row.note || "Custom redemption";
  return row.entry === "reversal" ? "Given back" : "Earned";
}
