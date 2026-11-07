/**
 * The Measurements page's words and the API's shapes (MEASURE-1, MEASURE-2, D20, measurements
 * plan §2–§4.7): the kernel's limits, how a value, a reading's time and the goal date read, the
 * checks a typed value goes through (in the kernel's words), and the live API's numbers
 * (`number | string`) as numbers. The trend itself is `lib/metric-trend.ts`.
 */
import { formatDay } from "@/lib/dates";
import type {
  MetricBetter,
  MetricDayRow,
  MetricDetailRead,
  MetricGoalRow,
  MetricGoalStatus,
  MetricReadingResult,
  MetricReadingRow,
  MetricRow,
} from "@/lib/production-ui-types";

/** The kernel's `MetricLimits`. */
export const NAME_MAX = 60;
export const UNIT_MAX = 16;
export const DECIMALS_MAX = 3;
export const NOTE_MAX = 140;
export const VALUE_MAX = 1_000_000;
/** The longest range asked for by days (ten years); a range left out is all history. */
export const MAX_DAYS = 3660;
/** How far ahead of now a reading's time may be, for clock skew. */
export const SKEW_MS = 60_000;

/** A metric on the starter list (§2.1): what making one by that name fills in. */
export interface MetricTemplate {
  key: string;
  name: string;
  unit: string;
  decimals: number;
  better: MetricBetter;
}

/** The starter list. M2 needs Weight; M3 adds the body metrics. */
export const WEIGHT: MetricTemplate = { key: "weight", name: "Weight", unit: "lb", decimals: 1, better: "lower" };
export const CATALOG: readonly MetricTemplate[] = [WEIGHT];

/** The chart's ranges: 30 days, 90 days, a year, and all of history. */
export const RANGES = [
  { id: "30", label: "30 days" },
  { id: "90", label: "90 days" },
  { id: "365", label: "1 year" },
  { id: "all", label: "All" },
] as const;
export type MetricRange = (typeof RANGES)[number]["id"];

/** The days a range asks the read for; null is all history. */
export function rangeDays(range: MetricRange): number | null {
  return range === "all" ? null : Number(range);
}

/** The metric made from the starter list's `key` (Weight), the live one first. */
export function findCatalog(metrics: readonly MetricRow[], key: string): MetricRow | undefined {
  const made = metrics.filter((m) => m.catalog === key);
  return made.find((m) => m.archivedAt == null) ?? made[0];
}

// ---------------------------------------------------------------------------
// Numbers
// ---------------------------------------------------------------------------

/** A value with exactly `decimals` places: "192.4", "6". */
export function formatValue(value: number, decimals: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
}

/** A value as the metric shows it: "192.4 lb". */
export function withUnit(value: number, metric: Pick<MetricRow, "unit" | "decimals">): string {
  const text = formatValue(value, metric.decimals);
  return metric.unit ? `${text} ${metric.unit}` : text;
}

/** A rate as it reads: "1.4", "1.63": up to one more place than the metric's. */
export function formatRate(rate: number, decimals: number): string {
  return Math.abs(rate).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: decimals + 1 });
}

/** The decimal places a typed number has: "192.40" has 2. */
function placesOf(text: string): number {
  const dot = text.indexOf(".");
  return dot < 0 ? 0 : text.length - dot - 1;
}

/** What the kernel calls too many places: "at most 1 decimal place", "a whole number". */
function placesWords(decimals: number): string {
  return decimals === 0 ? "a whole number" : decimals === 1 ? "at most 1 decimal place" : `at most ${decimals} decimal places`;
}

/**
 * A typed value checked as the kernel checks one (`MetricLimits.CheckValue`): the number, or
 * what's wrong in the kernel's words. Trailing zeros past the places are fine ("192.40").
 */
export function parseValue(raw: string, decimals: number, what: string): { value: number } | { problem: string } {
  const text = raw.trim();
  if (text === "") return { problem: `Type the ${what.toLowerCase()}, e.g. ${decimals === 0 ? "70" : "192.4"}.` };
  if (!/^-?(\d+\.?\d*|\.\d+)$/.test(text)) return { problem: `'${text}' isn't a number.` };
  const value = Number(text);
  if (Math.abs(value) > VALUE_MAX) return { problem: `${what} is between -1,000,000 and 1,000,000; got ${text}.` };
  const significant = text.replace(/0+$/, "").replace(/\.$/, "");
  if (text.includes(".") && placesOf(significant) > decimals) {
    return { problem: `${what} takes ${placesWords(decimals)}; got ${text}.` };
  }
  return { value };
}

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------

/** "7:10 AM": the home-zone time a reading was taken. */
export function clockOf(reading: Pick<MetricReadingRow, "takenLocal">): string {
  return new Date(reading.takenLocal).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

/** "Today, 7:10 AM", "Yesterday, 7:00 AM", "Tue, Nov 4, 7:10 AM". */
export function readingWhen(reading: Pick<MetricReadingRow, "takenOn" | "takenLocal">, today: string): string {
  const [y, m, d] = reading.takenOn.split("-").map(Number);
  const [ty, tm, td] = today.split("-").map(Number);
  const gap = Math.round((Date.UTC(ty, tm - 1, td) - Date.UTC(y, m - 1, d)) / 86_400_000);
  const day =
    gap === 0
      ? "Today"
      : gap === 1
        ? "Yesterday"
        : new Date(y, m - 1, d).toLocaleDateString(undefined, {
            weekday: "short",
            month: "short",
            day: "numeric",
            ...(y === ty ? {} : { year: "numeric" }),
          });
  return `${day}, ${clockOf(reading)}`;
}

/** "Mar 14", or "Mar 14, 2028" outside this year. */
function goalDay(day: string, today: string): string {
  if (day.slice(0, 4) === today.slice(0, 4)) return formatDay(day);
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

/** Under this many days in the basis, the goal date says it's an early estimate. */
export const EARLY_BASIS = 7;

/**
 * The goal line under the chart (§2.3): "185 lb by Mar 14 at 1.2 lb a week, from 23
 * weigh-ins". `noun` names a day with a reading ("weigh-in"). Null when there's no target.
 */
export function goalText(
  goal: MetricGoalRow,
  metric: Pick<MetricRow, "unit" | "decimals">,
  today: string,
  noun = "day",
): string | null {
  if (goal.status === "no_target" || goal.target == null) return null;
  const target = withUnit(goal.target, metric);
  const unit = metric.unit ? ` ${metric.unit}` : "";
  const rate = goal.ratePerWeek ?? 0;
  const rateText = `${formatRate(rate, metric.decimals)}${unit} a week`;
  const n = goal.basis.days;
  const from = `${n < EARLY_BASIS ? "an early estimate from" : "from"} ${n} ${noun}${n === 1 ? "" : "s"}`;
  const lower = goal.direction !== "higher";
  switch (goal.status as MetricGoalStatus) {
    case "too_few":
      return `Log another day to see when you'll reach ${target}.`;
    case "reached":
      return `At or ${lower ? "under" : "over"} ${target}.`;
    case "heading":
      return `${target} by ${goalDay(goal.reachedBy!, today)} at ${rateText}, ${from}.`;
    case "far":
      return `${target} is more than 2 years away at ${rateText}, ${from}.`;
    case "not_heading":
      return rate === 0
        ? `Not heading toward ${target}: flat, ${from}.`
        : `Not heading toward ${target}: ${rate > 0 ? "up" : "down"} ${rateText}, ${from}.`;
    default:
      return null;
  }
}

// ---------------------------------------------------------------------------
// The live API's shapes: numbers may arrive as strings (`number | string`)
// ---------------------------------------------------------------------------

function num(value: unknown): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

function numOrNull(value: unknown): number | null {
  return value === null || value === undefined || value === "" ? null : num(value);
}

function better(value: unknown): MetricBetter | null {
  return value === "lower" || value === "higher" ? value : null;
}

export function normalizeReading(raw: MetricReadingRow): MetricReadingRow {
  return {
    id: raw.id,
    metricId: raw.metricId,
    value: num(raw.value),
    unit: raw.unit ?? "",
    note: raw.note ?? null,
    takenAt: raw.takenAt,
    takenOn: raw.takenOn,
    takenLocal: raw.takenLocal,
    loggedAt: raw.loggedAt,
    versions: num(raw.versions),
  };
}

export function normalizeMetric(raw: MetricRow): MetricRow {
  return {
    id: raw.id,
    urn: raw.urn ?? "",
    name: raw.name ?? "",
    unit: raw.unit ?? "",
    decimals: num(raw.decimals),
    target: numOrNull(raw.target),
    better: better(raw.better),
    catalog: raw.catalog ?? null,
    archivedAt: raw.archivedAt ?? null,
    archivedOn: raw.archivedOn ?? null,
    readings: num(raw.readings),
    lastReading: raw.lastReading ? normalizeReading(raw.lastReading) : null,
  };
}

function normalizeDay(raw: MetricDayRow): MetricDayRow {
  return { day: raw.day, value: num(raw.value), trend: num(raw.trend), readingId: raw.readingId };
}

function normalizeGoal(raw: MetricGoalRow): MetricGoalRow {
  return {
    status: (raw.status ?? "no_target") as MetricGoalStatus,
    target: numOrNull(raw.target),
    direction: better(raw.direction),
    now: numOrNull(raw.now),
    ratePerWeek: numOrNull(raw.ratePerWeek),
    reachedBy: raw.reachedBy ?? null,
    basis: {
      days: num(raw.basis?.days),
      from: raw.basis?.from ?? null,
      to: raw.basis?.to ?? null,
    },
  };
}

export function normalizeMetricDetail(raw: MetricDetailRead): MetricDetailRead {
  return {
    today: raw.today,
    from: raw.from ?? null,
    metric: normalizeMetric(raw.metric),
    readings: (raw.readings ?? []).map(normalizeReading),
    days: (raw.days ?? []).map(normalizeDay),
    goal: normalizeGoal(raw.goal),
  };
}

export function normalizeReadingResult(raw: MetricReadingResult): MetricReadingResult {
  return { reading: raw.reading ? normalizeReading(raw.reading) : null };
}

/** The toast after a reading write: "Logged 192.4 lb", "Saved 192.2 lb", "Deleted the reading". */
export function readingMessage(
  kind: "logged" | "saved" | "deleted",
  reading: Pick<MetricReadingRow, "value" | "unit"> | null,
  decimals: number,
): string {
  if (kind === "deleted" || !reading) return "Deleted the reading";
  const value = withUnit(reading.value, { unit: reading.unit, decimals });
  return kind === "logged" ? `Logged ${value}` : `Saved ${value}`;
}
