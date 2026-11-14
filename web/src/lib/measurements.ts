/**
 * The Measurements page's words (MEASURE-1 to MEASURE-4, D20, measurements
 * plan §2–§4.9): the kernel's limits, the starter list's keys (the list is the API's, CR73), how a value, a reading's time, the goal
 * date and the normal range read, the checks a typed value goes through (in the kernel's words),
 * and how the page groups metrics (blood pressure's two are one). The trend itself is
 * `lib/metric-trend.ts`.
 */
import { formatDay } from "@/lib/dates";
import type {
  MetricFlag,
  MetricGoalRow,
  MetricGoalStatus,
  MetricReadingRow,
  MetricRow,
  MetricTemplate,
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

export type { MetricTemplate } from "@/lib/production-ui-types";

/**
 * The starter-list keys the page names: Weight has a card of its own, and blood pressure's two
 * are paired by theirs. The list itself is the kernel's `MetricCatalog`, read from
 * `GET /api/measurements/catalog` (code review CR73), so a retuned range reaches Add a metric and
 * the lab visit sheet with no web change; `tests/rules/catalogs.json` checks these keys are on it.
 */
export const WEIGHT_KEY = "weight";
export const SYSTOLIC_KEY = "bp_systolic";
export const DIASTOLIC_KEY = "bp_diastolic";

/** The starter list's template with `key`, when the list has it. */
export function templateOf(catalog: readonly MetricTemplate[], key: string): MetricTemplate | undefined {
  return catalog.find((t) => t.key === key);
}

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
// Groups: what the page shows as one row and one card (§4.8)
// ---------------------------------------------------------------------------

/** Blood pressure's group key, the name its row and card go by, and its two halves' labels. */
export const BLOOD_PRESSURE = {
  key: "bp",
  name: "Blood pressure",
  noun: "reading",
  labels: ["Systolic", "Diastolic"],
} as const;

/**
 * One row and one card on the page: a metric on its own, or blood pressure's two (systolic
 * first). `key` is the starter-list key, `bp`, or a metric's id; `noun` names a reading
 * ("weigh-in" for Weight); `labels` name each metric within it (its name, or "Systolic").
 */
export interface MetricGroup {
  key: string;
  name: string;
  noun: string;
  labels: readonly string[];
  metrics: MetricRow[];
}

/** A metric as a group of its own. */
export function singleGroup(metric: MetricRow): MetricGroup {
  return {
    key: metric.catalog ?? metric.id,
    name: metric.name,
    noun: metric.catalog === WEIGHT_KEY ? "weigh-in" : "reading",
    labels: [metric.name],
    metrics: [metric],
  };
}

/** A name inside a sentence: "weight", "body fat", but "VO2 max (lab)" and "LDL" as they are. */
export function nameInText(name: string): string {
  const second = name.charAt(1);
  if (/[A-Z0-9]/.test(second)) return name;
  return name.charAt(0).toLowerCase() + name.slice(1);
}

/** A group's normal range in words: "Normal: 90–119 mmHg", "Normal: systolic 90–119, diastolic 60–79 mmHg". */
export function groupRangeText(group: Pick<MetricGroup, "metrics" | "labels">): string | null {
  if (group.metrics.length === 1) {
    const text = rangeText(group.metrics[0]);
    return text ? `Normal: ${text}` : null;
  }
  const parts = group.metrics
    .map((m, i) => {
      const text = rangeText({ ...m, unit: "" });
      return text ? `${nameInText(group.labels[i])} ${text}` : null;
    })
    .filter((p): p is string => p != null);
  if (parts.length === 0) return null;
  const unit = group.metrics[0].unit;
  return `Normal: ${parts.join(", ")}${unit ? ` ${unit}` : ""}`;
}

/** Where a group sits: the starter list's order (blood pressure at systolic's place), then his own. */
function catalogRank(group: MetricGroup, catalog: readonly MetricTemplate[]): number {
  const key = group.key === BLOOD_PRESSURE.key ? SYSTOLIC_KEY : group.metrics[0].catalog;
  const i = catalog.findIndex((t) => t.key === key);
  return i < 0 ? catalog.length : i;
}

/**
 * `metrics` as the page's rows (§4.8): blood pressure's two paired into one when both are there,
 * in `catalog`'s order, then his own by name. Pass the live ones or the archived ones; a half of
 * blood pressure without its pair is a row of its own.
 */
export function groupMetrics(metrics: readonly MetricRow[], catalog: readonly MetricTemplate[]): MetricGroup[] {
  const systolic = metrics.find((m) => m.catalog === SYSTOLIC_KEY);
  const diastolic = metrics.find((m) => m.catalog === DIASTOLIC_KEY);
  const paired: MetricRow[] = systolic && diastolic ? [systolic, diastolic] : [];
  const groups: MetricGroup[] = metrics.filter((m) => !paired.includes(m)).map(singleGroup);
  if (paired.length === 2) groups.push({ ...BLOOD_PRESSURE, metrics: paired });
  return groups.sort((a, b) => catalogRank(a, catalog) - catalogRank(b, catalog) || a.name.localeCompare(b.name));
}

/** A group's values as they read: "192.4 lb", "122/78 mmHg"; a missing half is "—". */
export function groupValue(group: Pick<MetricGroup, "metrics">, values: readonly (number | null)[]): string {
  const [first] = group.metrics;
  if (group.metrics.length === 1) return values[0] == null ? "—" : withUnit(values[0], first);
  const text = group.metrics.map((m, i) => (values[i] == null ? "—" : formatValue(values[i]!, m.decimals))).join("/");
  return first.unit ? `${text} ${first.unit}` : text;
}

/**
 * A group's readings taken together, newest first. Blood pressure's two share an instant (they're
 * logged in one write), so each pair is one set; a metric on its own has a set for each reading.
 * `readings` is each metric's readings, in the group's order.
 */
export function readingSets(readings: readonly (readonly MetricReadingRow[])[]): (MetricReadingRow | null)[][] {
  const width = readings.length;
  const sets: (MetricReadingRow | null)[][] = [];
  // The open set at each instant; a second reading of one metric at that instant starts another.
  const open = new Map<number, (MetricReadingRow | null)[]>();
  readings.forEach((list, i) => {
    for (const r of list) {
      const at = Date.parse(r.takenAt);
      let set = open.get(at);
      if (!set || set[i] != null) {
        set = Array<MetricReadingRow | null>(width).fill(null);
        open.set(at, set);
        sets.push(set);
      }
      set[i] = r;
    }
  });
  const first = (set: (MetricReadingRow | null)[]) => set.find((r) => r != null)!;
  return sets.sort(
    (a, b) => Date.parse(first(b).takenAt) - Date.parse(first(a).takenAt) || first(b).id.localeCompare(first(a).id),
  );
}

/** A set of readings taken together: one for a metric on its own, blood pressure's pair. */
export type ReadingSet = (MetricReadingRow | null)[];

/** The newest set among a group's metrics' newest readings. */
export function lastSet(group: Pick<MetricGroup, "metrics">): ReadingSet | null {
  return readingSets(group.metrics.map((m) => (m.lastReading ? [m.lastReading] : [])))[0] ?? null;
}

/** "122/78 mmHg · Today, 7:10 AM": a set as its row, sheet and delete ask name it. */
export function setText(group: Pick<MetricGroup, "metrics">, set: ReadingSet, today: string): string {
  const taken = set.find((r) => r != null)!;
  return `${groupValue(group, set.map((r) => r?.value ?? null))} · ${readingWhen(taken, today || taken.takenOn)}`;
}

/** A starter-list choice in Add a metric: one metric, or blood pressure's two. */
export interface StarterChoice {
  key: string;
  name: string;
  templates: MetricTemplate[];
}

/**
 * The starter metrics on `catalog` not tracked yet (§2.1, §4.8), in the list's order: blood
 * pressure is one choice (its two), and Weight has its own card at the top. Lab tests come from a
 * lab visit (§4.9).
 */
export function starterChoices(metrics: readonly MetricRow[], catalog: readonly MetricTemplate[]): StarterChoice[] {
  const tracked = new Set(metrics.filter((m) => m.archivedAt == null).map((m) => m.catalog));
  const diastolic = templateOf(catalog, DIASTOLIC_KEY);
  const choices: StarterChoice[] = [];
  for (const t of catalog) {
    if (t.key === WEIGHT_KEY || t.key === DIASTOLIC_KEY || t.panel != null) continue;
    if (t.key === SYSTOLIC_KEY && diastolic) {
      if (!tracked.has(SYSTOLIC_KEY) || !tracked.has(DIASTOLIC_KEY)) {
        choices.push({ key: BLOOD_PRESSURE.key, name: BLOOD_PRESSURE.name, templates: [t, diastolic] });
      }
      continue;
    }
    if (!tracked.has(t.key)) choices.push({ key: t.key, name: t.name, templates: [t] });
  }
  return choices;
}

/** What a starter choice comes with, in words: "mmHg · lower is better · normal 90–119 / 60–79". */
export function starterText(choice: StarterChoice): string {
  const [t] = choice.templates;
  const ranges = choice.templates
    .map((x) => rangeText({ low: x.low, high: x.high, unit: "", decimals: x.decimals }))
    .filter((r): r is string => r != null);
  return [t.unit, t.better ? `${t.better} is better` : null, ranges.length > 0 ? `normal ${ranges.join(" / ")}` : null]
    .filter((p): p is string => p != null)
    .join(" · ");
}

// ---------------------------------------------------------------------------
// The normal range (MEASURE-3, §4.8)
// ---------------------------------------------------------------------------

/** `low` below the range and `high` above it; the ends themselves are normal. As `Metric.Flag`. */
export function flagOf(value: number, metric: Pick<MetricRow, "low" | "high">): MetricFlag | null {
  if (metric.low != null && value < metric.low) return "low";
  if (metric.high != null && value > metric.high) return "high";
  return null;
}

/** "High", "Low". */
export function flagWord(flag: MetricFlag): string {
  return flag === "high" ? "High" : "Low";
}

/** A set's flags in words, each once: "High", "Low", "High, low"; null when none is flagged. */
export function flagsText(readings: readonly (Pick<MetricReadingRow, "flag"> | null)[]): string | null {
  const flags = [...new Set(readings.map((r) => r?.flag).filter((f): f is MetricFlag => f != null))];
  if (flags.length === 0) return null;
  return flags.map((f, i) => (i === 0 ? flagWord(f) : flagWord(f).toLowerCase())).join(", ");
}

/** The normal range in words, as `Metric.RangeText`: "90–119 mmHg", "up to 40.0 in", "at least 30.0". */
export function rangeText(metric: Pick<MetricRow, "low" | "high" | "unit" | "decimals">): string | null {
  const unit = metric.unit ? ` ${metric.unit}` : "";
  const { low, high, decimals } = metric;
  if (low != null && high != null) return `${formatValue(low, decimals)}–${formatValue(high, decimals)}${unit}`;
  if (high != null) return `up to ${formatValue(high, decimals)}${unit}`;
  if (low != null) return `at least ${formatValue(low, decimals)}${unit}`;
  return null;
}

/** A normal range checked as the kernel's `MetricLimits.CheckRange` checks one: the low end at or below the high. */
export function rangeProblem(low: number | null, high: number | null, decimals: number): string | null {
  if (low != null && high != null && low > high) {
    return `The normal range's low end (${formatValue(low, decimals)}) is above its high end (${formatValue(high, decimals)}).`;
  }
  return null;
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
export function parseValue(
  raw: string,
  decimals: number,
  what: string,
  example = decimals === 0 ? "70" : "192.4",
): { value: number } | { problem: string } {
  const text = raw.trim();
  if (text === "") return { problem: `Type the ${nameInText(what)}, e.g. ${example}.` };
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

/** The toast after a group's write: "Logged 122/78 mmHg", "Saved 192.2 lb". */
export function readingsMessage(
  kind: "logged" | "saved",
  group: Pick<MetricGroup, "metrics">,
  values: readonly (number | null)[],
): string {
  return `${kind === "logged" ? "Logged" : "Saved"} ${groupValue(group, values)}`;
}
