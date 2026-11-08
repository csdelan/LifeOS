/**
 * The Measurements page's words and the API's shapes (MEASURE-1 to MEASURE-4, D20, measurements
 * plan §2–§4.9): the kernel's limits and starter list, how a value, a reading's time, the goal
 * date and the normal range read, the checks a typed value goes through (in the kernel's words),
 * how the page groups metrics (blood pressure's two are one), and the live API's numbers
 * (`number | string`) as numbers. The trend itself is `lib/metric-trend.ts`.
 */
import { formatDay } from "@/lib/dates";
import type {
  LabPanel,
  MetricBetter,
  MetricDayRow,
  MetricDetailRead,
  MetricFlag,
  MetricGoalRow,
  MetricGoalStatus,
  MetricReadingResult,
  MetricReadingRow,
  MetricReadingsResult,
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
  /** Which way is better; most lab tests have none. */
  better: MetricBetter | null;
  /** The normal range it comes with: blood pressure's (§4.8) and the lab tests' common adult ones (§4.9). */
  low?: number;
  high?: number;
  /** The lab panel a lab test is on (§4.9); a metric that isn't a lab test has none. */
  panel?: LabPanel;
}

/**
 * The starter list, as the kernel's `MetricCatalog`: Weight (M1), then the body metrics (M3).
 * Blood pressure is two metrics logged together, paired by their keys. Only it comes with a
 * normal range (the American Heart Association's "normal"); the others depend on age and sex.
 */
export const WEIGHT: MetricTemplate = { key: "weight", name: "Weight", unit: "lb", decimals: 1, better: "lower" };
export const BODY_FAT: MetricTemplate = { key: "body_fat", name: "Body fat", unit: "%", decimals: 1, better: "lower" };
export const WAIST: MetricTemplate = { key: "waist", name: "Waist", unit: "in", decimals: 1, better: "lower" };
export const SYSTOLIC: MetricTemplate = {
  key: "bp_systolic",
  name: "Systolic BP",
  unit: "mmHg",
  decimals: 0,
  better: "lower",
  low: 90,
  high: 119,
};
export const DIASTOLIC: MetricTemplate = {
  key: "bp_diastolic",
  name: "Diastolic BP",
  unit: "mmHg",
  decimals: 0,
  better: "lower",
  low: 60,
  high: 79,
};
export const VO2MAX_LAB: MetricTemplate = {
  key: "vo2max_lab",
  name: "VO2 max (lab)",
  unit: "mL/kg/min",
  decimals: 1,
  better: "higher",
};

/** A lab test on the starter list (M4, §4.9), with the common adult range a first visit starts from. */
function lab(
  key: string,
  name: string,
  unit: string,
  decimals: number,
  better: MetricBetter | null,
  low: number | null,
  high: number | null,
  panel: LabPanel,
): MetricTemplate {
  return { key, name, unit, decimals, better, panel, ...(low == null ? {} : { low }), ...(high == null ? {} : { high }) };
}

/** The comprehensive metabolic panel's 14 tests and eGFR, as the kernel's `MetricCatalog.Cmp`. */
export const CMP_TESTS: readonly MetricTemplate[] = [
  lab("glucose", "Glucose", "mg/dL", 0, null, 70, 100, "cmp"),
  lab("bun", "BUN", "mg/dL", 0, null, 6, 20, "cmp"),
  lab("creatinine", "Creatinine", "mg/dL", 2, null, 0.6, 1.3, "cmp"),
  lab("egfr", "eGFR", "mL/min/1.73m2", 0, "higher", 60, null, "cmp"),
  lab("sodium", "Sodium", "mmol/L", 0, null, 135, 145, "cmp"),
  lab("potassium", "Potassium", "mmol/L", 1, null, 3.7, 5.2, "cmp"),
  lab("chloride", "Chloride", "mmol/L", 0, null, 96, 106, "cmp"),
  lab("co2", "CO2", "mmol/L", 0, null, 23, 29, "cmp"),
  lab("calcium", "Calcium", "mg/dL", 1, null, 8.5, 10.2, "cmp"),
  lab("total_protein", "Total protein", "g/dL", 1, null, 6.0, 8.3, "cmp"),
  lab("albumin", "Albumin", "g/dL", 1, null, 3.4, 5.4, "cmp"),
  lab("bilirubin", "Total bilirubin", "mg/dL", 1, null, 0.1, 1.2, "cmp"),
  lab("alp", "Alkaline phosphatase", "U/L", 0, null, 20, 130, "cmp"),
  lab("ast", "AST", "U/L", 0, null, 8, 33, "cmp"),
  lab("alt", "ALT", "U/L", 0, null, 4, 36, "cmp"),
];

/** The lipid panel, as `MetricCatalog.Lipids`. HDL's range depends on sex, so it has none. */
export const LIPID_TESTS: readonly MetricTemplate[] = [
  lab("cholesterol", "Total cholesterol", "mg/dL", 0, "lower", null, 199, "lipids"),
  lab("hdl", "HDL", "mg/dL", 0, "higher", null, null, "lipids"),
  lab("triglycerides", "Triglycerides", "mg/dL", 0, "lower", null, 149, "lipids"),
  lab("ldl", "LDL", "mg/dL", 0, "lower", null, 99, "lipids"),
  lab("non_hdl", "Non-HDL cholesterol", "mg/dL", 0, "lower", null, 129, "lipids"),
];

/** Hemoglobin A1c, as `MetricCatalog.A1c`. */
export const A1C: MetricTemplate = lab("a1c", "A1c", "%", 1, "lower", null, 5.6, "a1c");

/** Every lab test on the starter list, in the order a report prints them. */
export const LAB_TESTS: readonly MetricTemplate[] = [...CMP_TESTS, ...LIPID_TESTS, A1C];

/** The whole starter list, as `MetricCatalog.All`: the body metrics, then the lab tests. */
export const CATALOG: readonly MetricTemplate[] = [WEIGHT, BODY_FAT, WAIST, SYSTOLIC, DIASTOLIC, VO2MAX_LAB, ...LAB_TESTS];

/** The starter-list template a metric named `name` is made from, in any case. */
export function findTemplate(name: string): MetricTemplate | undefined {
  const wanted = name.trim().toLowerCase();
  return CATALOG.find((t) => t.name.toLowerCase() === wanted);
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
    noun: metric.catalog === WEIGHT.key ? "weigh-in" : "reading",
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
function catalogRank(group: MetricGroup): number {
  const key = group.key === BLOOD_PRESSURE.key ? SYSTOLIC.key : group.metrics[0].catalog;
  const i = CATALOG.findIndex((t) => t.key === key);
  return i < 0 ? CATALOG.length : i;
}

/**
 * `metrics` as the page's rows (§4.8): blood pressure's two paired into one when both are there,
 * in starter-list order, then his own by name. Pass the live ones or the archived ones; a half of
 * blood pressure without its pair is a row of its own.
 */
export function groupMetrics(metrics: readonly MetricRow[]): MetricGroup[] {
  const systolic = metrics.find((m) => m.catalog === SYSTOLIC.key);
  const diastolic = metrics.find((m) => m.catalog === DIASTOLIC.key);
  const paired: MetricRow[] = systolic && diastolic ? [systolic, diastolic] : [];
  const groups: MetricGroup[] = metrics.filter((m) => !paired.includes(m)).map(singleGroup);
  if (paired.length === 2) groups.push({ ...BLOOD_PRESSURE, metrics: paired });
  return groups.sort((a, b) => catalogRank(a) - catalogRank(b) || a.name.localeCompare(b.name));
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
 * The starter metrics not tracked yet (§2.1, §4.8), in the list's order: blood pressure is one
 * choice (its two), and Weight has its own card at the top. Lab tests come from a lab visit (§4.9).
 */
export function starterChoices(metrics: readonly MetricRow[]): StarterChoice[] {
  const tracked = new Set(metrics.filter((m) => m.archivedAt == null).map((m) => m.catalog));
  const choices: StarterChoice[] = [];
  for (const t of CATALOG) {
    if (t.key === WEIGHT.key || t.key === DIASTOLIC.key || t.panel != null) continue;
    if (t.key === SYSTOLIC.key) {
      if (!tracked.has(SYSTOLIC.key) || !tracked.has(DIASTOLIC.key)) {
        choices.push({ key: BLOOD_PRESSURE.key, name: BLOOD_PRESSURE.name, templates: [SYSTOLIC, DIASTOLIC] });
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
    .map((x) => rangeText({ low: x.low ?? null, high: x.high ?? null, unit: "", decimals: x.decimals }))
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

function flag(value: unknown): MetricFlag | null {
  return value === "low" || value === "high" ? value : null;
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
    flag: flag(raw.flag),
    visitId: raw.visitId ?? null,
    low: numOrNull(raw.low),
    high: numOrNull(raw.high),
  };
}

function panel(value: unknown): LabPanel | null {
  return value === "cmp" || value === "lipids" || value === "a1c" || value === "other" ? value : null;
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
    low: numOrNull(raw.low),
    high: numOrNull(raw.high),
    panel: panel(raw.panel),
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

export function normalizeReadingsResult(raw: MetricReadingsResult): MetricReadingsResult {
  return { readings: (raw.readings ?? []).map(normalizeReading) };
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
