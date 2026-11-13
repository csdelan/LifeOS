/**
 * The mock store's measurements (MEASURE-1 to MEASURE-4, D20, measurements plan §4.7–§4.9):
 * Weight, with a 185 lb goal and six weeks of morning weigh-ins drifting down, a few days
 * skipped and one evening weigh-in on top of a morning one; blood pressure (its two starter
 * metrics, with their normal ranges) twice a week, the newest pair two days ago with its
 * systolic high; and Waist weekly, with no range or goal. All relative to today and the same on
 * every load; the newest weigh-in is yesterday's, so today's is Chris's to log. Body fat and
 * VO2 max (lab) are left for Add a metric. One lab visit four months ago (Quest Diagnostics,
 * fasting): the CMP, the lipid panel and A1c, 21 results with the ranges printed beside them, 3
 * outside them, and a report. It answers as `/api/measurements` does, with the kernel's refusals
 * word for word, flags a lab result against its printed range and any other reading against its
 * metric's range as it is now, has each test's range follow its newest report, and works out the
 * day values, trend and goal with `lib/metric-trend.ts`, the kernel's own formulas. The mock's
 * home zone is the browser's.
 */
import { dataUrlFor, objectUrlFor } from "@/lib/artifacts";
import { addDays, nowIso, todayIso } from "@/lib/dates";
import { LAB_MAX, REPORT_TYPES, lineProblem, reportOrder, reportProblem } from "@/lib/labs";
import {
  DECIMALS_MAX,
  MAX_DAYS,
  NAME_MAX,
  NOTE_MAX,
  SKEW_MS,
  UNIT_MAX,
  VALUE_MAX,
  flagOf,
  formatValue,
  type MetricTemplate,
} from "@/lib/measurements";
import {
  A1C,
  CMP_TESTS,
  DIASTOLIC,
  LIPID_TESTS,
  METRIC_CATALOG,
  SYSTOLIC,
  WAIST,
  WEIGHT,
  findTemplate,
} from "@/lib/mock/metric-catalog";
import { dayValues, goal, roundAway, trend } from "@/lib/metric-trend";
import type {
  LabPanel,
  LabReportRow,
  LabResultRequest,
  LabVisitRequest,
  LabVisitResult,
  LabVisitRow,
  LabVisitsRead,
  MetricBetter,
  MetricDetailRead,
  MetricReadingBatchRequest,
  MetricReadingEditRequest,
  MetricReadingRequest,
  MetricReadingResult,
  MetricReadingRow,
  MetricReadingsResult,
  MetricRequest,
  MetricRow,
} from "@/lib/production-ui-types";

/** The most readings logged together, as `MetricReadingService.MaxTogether`. */
const MAX_TOGETHER = 50;

interface MockMetric {
  id: string;
  urn: string;
  name: string;
  unit: string;
  decimals: number;
  target: number | null;
  better: MetricBetter | null;
  catalog: string | null;
  archivedAt: string | null;
  archivedOn: string | null;
  low: number | null;
  high: number | null;
  panel: LabPanel | null;
}

/**
 * A reading as `v_metric_reading` reads it: its newest version; a deleted one is gone. Its flag
 * is worked out on read: a lab result's from the range printed beside it, any other's from its
 * metric's range as it is then.
 */
type MockReading = Omit<MetricReadingRow, "flag">;

/** A lab visit as `v_lab_visit` reads it: its newest version; a deleted one is gone. */
type MockVisit = Omit<LabVisitRow, "results" | "outside">;

interface MeasurementsState {
  metrics: MockMetric[];
  readings: MockReading[];
  visits: MockVisit[];
  /** Each report's bytes, as a URL the page can open. */
  reportUrls: Map<string, string>;
  nextId: number;
}

let seeded: MeasurementsState | null = null;

/** Seeded on first use, so "today" is the day it's first read. */
function store(): MeasurementsState {
  return (seeded ??= seedMeasurements());
}

export function resetMeasurementsStore() {
  seeded = null;
}

/** Test seam: no metrics or readings, as before Chris starts tracking. */
export function clearMockMeasurements() {
  const state = store();
  state.metrics = [];
  state.readings = [];
  state.visits = [];
}

// ---------------------------------------------------------------------------
// The seed
// ---------------------------------------------------------------------------

function hex(n: number, width: number): string {
  return n.toString(16).padStart(width, "0");
}

/** A uuid-shaped id, the same on every load. */
function mockId(kind: number, n: number): string {
  return `00000000-0000-4000-9${hex(kind, 3)}-${hex(n, 12)}`;
}

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** A home-zone wall-clock time on `day` as the reading's instant, day and local time. */
function at(day: string, hour: number, minute: number): Pick<MockReading, "takenAt" | "takenOn" | "takenLocal"> {
  const [y, m, d] = day.split("-").map(Number);
  return {
    takenAt: new Date(y, m - 1, d, hour, minute).toISOString(),
    takenOn: day,
    takenLocal: `${day}T${pad(hour)}:${pad(minute)}:00`,
  };
}

/** The local day, time and instant of an ISO instant, as the kernel reads it in the home zone. */
function local(iso: string): Pick<MockReading, "takenAt" | "takenOn" | "takenLocal"> {
  const d = new Date(iso);
  const day = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  return {
    takenAt: d.toISOString(),
    takenOn: day,
    takenLocal: `${day}T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`,
  };
}

/** The seeded weeks: day 0 is 42 days ago, the last yesterday. */
const SEEDED_DAYS = 42;
/** A day-to-day swing, so the dots scatter around the trend as a scale's do. */
const SWING = [0.6, -0.4, 0.2, -0.8, 0.4, 0, -0.2, 0.7, -0.5, 0.3, -0.1, 0.5, -0.6];

/**
 * Blood pressure twice a week, as days ago and systolic/diastolic: mostly normal, the first
 * pair high on both, and the newest (two days ago) high on systolic alone.
 */
const PRESSURES: readonly [number, number, number][] = [
  [40, 124, 82],
  [36, 118, 77],
  [33, 121, 79],
  [29, 116, 75],
  [26, 119, 78],
  [22, 114, 74],
  [19, 117, 76],
  [15, 113, 73],
  [12, 118, 77],
  [8, 115, 74],
  [5, 112, 72],
  [2, 121, 78],
];

/** Waist once a week, as days ago and inches. */
const WAISTS: readonly [number, number][] = [
  [38, 38.6],
  [31, 38.4],
  [24, 38.3],
  [17, 38.0],
  [10, 37.9],
  [3, 37.7],
];

/** A seeded metric made from the starter list, as `bsk metric add` makes one by its name. */
function seededMetric(n: number, template: MetricTemplate, urn: string, target: number | null = null): MockMetric {
  return {
    id: mockId(1, n),
    urn: `urn:bsk:metric:${urn}`,
    name: template.name,
    unit: template.unit,
    decimals: template.decimals,
    target,
    better: template.better,
    catalog: template.key,
    archivedAt: null,
    archivedOn: null,
    low: template.low,
    high: template.high,
    panel: template.panel,
  };
}

/**
 * The seeded visit's results as the report prints them, each with the range beside it: the
 * report's own ranges, a little off the starter list's as a real lab's are. Glucose, LDL and
 * non-HDL cholesterol are high.
 */
const LAB_RESULTS: readonly [template: MetricTemplate, value: number, low: number | null, high: number | null][] = [
  [CMP_TESTS[0], 104, 65, 99],
  [CMP_TESTS[1], 15, 7, 25],
  [CMP_TESTS[2], 1.02, 0.6, 1.29],
  [CMP_TESTS[3], 92, 60, null],
  [CMP_TESTS[4], 140, 135, 146],
  [CMP_TESTS[5], 4.4, 3.5, 5.3],
  [CMP_TESTS[6], 103, 98, 110],
  [CMP_TESTS[7], 26, 20, 32],
  [CMP_TESTS[8], 9.6, 8.6, 10.3],
  [CMP_TESTS[9], 7.1, 6.1, 8.1],
  [CMP_TESTS[10], 4.6, 3.6, 5.1],
  [CMP_TESTS[11], 0.8, 0.2, 1.2],
  [CMP_TESTS[12], 68, 36, 130],
  [CMP_TESTS[13], 24, 10, 40],
  [CMP_TESTS[14], 31, 9, 46],
  [LIPID_TESTS[0], 196, null, 199],
  [LIPID_TESTS[1], 52, 40, null],
  [LIPID_TESTS[2], 118, null, 149],
  [LIPID_TESTS[3], 131, null, 99],
  [LIPID_TESTS[4], 144, null, 129],
  [A1C, 5.4, null, 5.6],
];

/** How long ago the seeded visit was. */
const LAB_DAYS_AGO = 120;

function seedMeasurements(): MeasurementsState {
  const today = todayIso();
  const weight = seededMetric(1, WEIGHT, "weight-3e1a90", 185);
  const systolic = seededMetric(2, SYSTOLIC, "systolic-bp-5b20c1");
  const diastolic = seededMetric(3, DIASTOLIC, "diastolic-bp-5b20c2");
  const waist = seededMetric(4, WAIST, "waist-71d4e3");
  const readings: MockReading[] = [];
  let n = 1;
  for (let i = 0; i < SEEDED_DAYS; i++) {
    // A few days skipped: travel, a late morning.
    if (i % 9 === 4 || i % 13 === 7) continue;
    const day = addDays(today, i - SEEDED_DAYS);
    const value = roundAway(199.8 - 0.14 * i + SWING[i % SWING.length], 1);
    const when = at(day, 6 + (i % 2), 40 + (i % 3) * 5);
    readings.push(reading(mockId(2, n++), weight, value, when, null));
    // One evening weigh-in after dinner: a reading, but the day's number is the morning's.
    if (i === 21) readings.push(reading(mockId(2, n++), weight, roundAway(value + 2.1, 1), at(day, 21, 15), "after dinner"));
  }
  // A pair is logged in one write, so its two share their instant.
  PRESSURES.forEach(([ago, sys, dia], i) => {
    const when = at(addDays(today, -ago), 7, 20 + (i % 3) * 5);
    const note = i === 0 ? "after coffee" : null;
    readings.push(reading(mockId(2, n++), systolic, sys, when, note));
    readings.push(reading(mockId(2, n++), diastolic, dia, when, note));
  });
  for (const [ago, inches] of WAISTS) readings.push(reading(mockId(2, n++), waist, inches, at(addDays(today, -ago), 7, 5), null));

  // One lab visit: each test made from the starter list, its range the report's (its newest).
  const tests: MockMetric[] = [];
  const drawn = at(addDays(today, -LAB_DAYS_AGO), 8, 15);
  const visitId = mockId(3, 1);
  LAB_RESULTS.forEach(([template, value, low, high], i) => {
    const test = { ...seededMetric(10 + i, template, `${template.key.replace(/_/g, "-")}-${hex(0xa10 + i, 6)}`), low, high };
    tests.push(test);
    readings.push({ ...reading(mockId(2, n++), test, value, drawn, null), visitId, low, high });
  });
  const reportText = "%PDF-1.4\nQuest Diagnostics: a made-up lab report for the mock.\n%%EOF\n";
  const report: LabReportRow = {
    id: mockId(4, 1),
    filename: "quest-results.pdf",
    contentType: "application/pdf",
    byteSize: reportText.length,
  };
  const visit: MockVisit = {
    id: visitId,
    lab: "Quest Diagnostics",
    note: "Fasting",
    collectedAt: drawn.takenAt,
    collectedOn: drawn.takenOn,
    collectedLocal: drawn.takenLocal,
    report,
    loggedAt: drawn.takenAt,
    versions: 2,
  };
  return {
    metrics: [weight, systolic, diastolic, waist, ...tests],
    readings,
    visits: [visit],
    reportUrls: new Map([[report.id, dataUrlFor(reportText, "application/pdf")]]),
    nextId: n,
  };
}

function reading(
  id: string,
  metric: MockMetric,
  value: number,
  when: Pick<MockReading, "takenAt" | "takenOn" | "takenLocal">,
  note: string | null,
): MockReading {
  return {
    id,
    metricId: metric.id,
    value,
    unit: metric.unit,
    note,
    ...when,
    loggedAt: when.takenAt,
    versions: 1,
    visitId: null,
    low: null,
    high: null,
  };
}

/** Whether a reading carries a range of its own, as a lab result does. */
function hasOwnRange(r: Pick<MockReading, "low" | "high">): boolean {
  return r.low != null || r.high != null;
}

// ---------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------

function readingsOf(metricId: string): MockReading[] {
  return store().readings.filter((r) => r.metricId === metricId);
}

/**
 * A reading as the API answers it: a lab result flagged against the range printed beside it, any
 * other against its metric's range as it is now (`Metric.Flag(MetricReading)`).
 */
function readingRow(r: MockReading, metric?: MockMetric): MetricReadingRow {
  const of = metric ?? store().metrics.find((m) => m.id === r.metricId);
  return { ...r, flag: hasOwnRange(r) ? flagOf(r.value, r) : of ? flagOf(r.value, of) : null };
}

/** Newest first by when it was taken, then id, as the kernel orders them. */
function newestFirst(a: MockReading, b: MockReading): number {
  return Date.parse(b.takenAt) - Date.parse(a.takenAt) || b.id.localeCompare(a.id);
}

function metricRow(metric: MockMetric): MetricRow {
  const own = readingsOf(metric.id).sort(newestFirst);
  return {
    id: metric.id,
    urn: metric.urn,
    name: metric.name,
    unit: metric.unit,
    decimals: metric.decimals,
    target: metric.target,
    better: metric.better,
    catalog: metric.catalog,
    archivedAt: metric.archivedAt,
    archivedOn: metric.archivedOn,
    low: metric.low,
    high: metric.high,
    panel: metric.panel,
    readings: own.length,
    lastReading: own[0] ? readingRow(own[0], metric) : null,
  };
}

/** Every metric, archived ones included, by name. */
export function mockMetrics(): MetricRow[] {
  return [...store().metrics].sort((a, b) => a.name.localeCompare(b.name)).map(metricRow);
}

function checkDays(days: number) {
  if (!Number.isInteger(days) || days < 1 || days > MAX_DAYS) throw new Error(`Ask for 1 to ${MAX_DAYS} days; got ${days}.`);
}

/**
 * One metric over the last `days` days (all history when null): its readings newest first, the
 * day values with the trend oldest first, and the goal over all history.
 */
export function mockMetricDetail(reference: string, days: number | null = null): MetricDetailRead {
  if (days != null) checkDays(days);
  const metric = resolveMetric(reference);
  const today = todayIso();
  const from = days == null ? null : addDays(today, 1 - days);
  const all = readingsOf(metric.id);
  const values = dayValues(all);
  const points = trend(values, metric.decimals);
  const g = goal(values, metric.target, metric.better, metric.decimals);
  return {
    today,
    from,
    metric: metricRow(metric),
    readings: all
      .filter((r) => from == null || r.takenOn >= from)
      .sort(newestFirst)
      .map((r) => readingRow(r, metric)),
    days: points
      .filter((p) => from == null || p.day >= from)
      .map((p) => ({ day: p.day, value: p.value, trend: p.trend, readingId: p.readingId })),
    goal: {
      status: g.status,
      target: g.target,
      direction: g.direction,
      now: g.now,
      ratePerWeek: g.ratePerWeek,
      reachedBy: g.reachedBy,
      basis: { days: g.basisDays, from: g.basisFrom, to: g.basisTo },
    },
  };
}

// ---------------------------------------------------------------------------
// Metrics, refused in the kernel's words (`MetricLimits`, `MetricService`)
// ---------------------------------------------------------------------------

function same(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase();
}

/** A control character or one of Unicode's own line breaks, as the kernel's `IsBreak`. */
function hasBreak(text: string): boolean {
  return [...text].some((ch) => {
    const code = ch.charCodeAt(0);
    return code < 0x20 || (code >= 0x7f && code <= 0x9f) || code === 0x2028 || code === 0x2029;
  });
}

/**
 * The metric `reference` names: its id, its urn, its short id (the urn's hex tail) or its name in
 * any case — one not archived first. As `MetricService.Resolve`.
 */
function resolveMetric(reference: string, preferArchived = false): MockMetric {
  const wanted = reference?.trim() ?? "";
  if (!wanted) throw new Error("Say which metric, by its name.");
  const all = store().metrics;
  const lower = wanted.toLowerCase();
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(wanted)) {
    const byId = all.find((m) => m.id === lower);
    if (!byId) throw new Error(`No subject matches '${reference}'.`);
    return byId;
  }
  const shortId = /^[0-9a-f]{4,12}$/i.test(wanted);
  const byUrn = all.find((m) => m.urn.toLowerCase() === lower || (shortId && m.urn.toLowerCase().endsWith(`-${lower}`)));
  if (byUrn) return byUrn;
  const named = all
    .filter((m) => same(m.name, wanted))
    .sort((a, b) => Number((a.archivedAt != null) !== preferArchived) - Number((b.archivedAt != null) !== preferArchived));
  if (named.length === 0) throw new Error(`No subject matches '${reference}'.`);
  return named[0];
}

function checkName(name: string | undefined): string {
  const text = name?.trim() ?? "";
  if (!text) throw new Error("Give the metric a name, e.g. Weight.");
  if (hasBreak(text)) throw new Error("A metric's name is one line: take out the line break.");
  if (text.length > NAME_MAX) throw new Error(`A metric's name is up to ${NAME_MAX} characters; that one is ${text.length}.`);
  return text;
}

function checkUnit(unit: string | undefined): string {
  const text = unit?.trim() ?? "";
  if (!text) throw new Error("Give the metric a unit, e.g. lb, in, % or mg/dL.");
  if (hasBreak(text)) throw new Error("A unit is one line: take out the line break.");
  if (text.length > UNIT_MAX) throw new Error(`A unit is up to ${UNIT_MAX} characters; that one is ${text.length}.`);
  return text;
}

function checkDecimals(decimals: number): number {
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > DECIMALS_MAX) {
    throw new Error(`A metric takes 0 to ${DECIMALS_MAX} decimal places; got ${decimals}.`);
  }
  return decimals;
}

function checkBetter(better: string): MetricBetter {
  const text = better.trim().toLowerCase();
  if (text !== "lower" && text !== "higher") throw new Error(`Which way is better is 'lower' or 'higher'; got '${better}'.`);
  return text;
}

/** A value within ±1,000,000 with at most `decimals` places, as `MetricLimits.CheckValue`. */
function checkValue(value: number, decimals: number, what: string): number {
  if (!Number.isFinite(value) || value < -VALUE_MAX || value > VALUE_MAX) {
    throw new Error(`${what} is between -1,000,000 and 1,000,000; got ${value}.`);
  }
  if (roundAway(value, decimals) !== value) {
    const places =
      decimals === 0 ? "a whole number" : decimals === 1 ? "at most 1 decimal place" : `at most ${decimals} decimal places`;
    throw new Error(`${what} takes ${places}; got ${value}.`);
  }
  return value;
}

/** A normal range as kept, as `MetricLimits.CheckRange`: each end checked, the low at or below the high. */
function checkRange(low: number | null, high: number | null, decimals: number): { low: number | null; high: number | null } {
  const keptLow = low == null ? null : checkValue(low, decimals, "The normal range's low end");
  const keptHigh = high == null ? null : checkValue(high, decimals, "The normal range's high end");
  if (keptLow != null && keptHigh != null && keptLow > keptHigh) {
    throw new Error(
      `The normal range's low end (${formatValue(keptLow, decimals)}) is above its high end (${formatValue(keptHigh, decimals)}).`,
    );
  }
  return { low: keptLow, high: keptHigh };
}

function checkFree(name: string, except: string | null) {
  const taken = store().metrics.find((m) => m.archivedAt == null && m.id !== except && same(m.name, name));
  if (taken) throw new Error(`You already have a metric called '${taken.name}'.`);
}

/**
 * Adds a metric; a starter-list name (Weight, Systolic BP, Glucose) fills in the unit, decimals,
 * which way is better, the normal range and a lab test's panel. `panel` makes it a lab test.
 */
export function mockAddMetric(req: MetricRequest & { name: string }, panel: LabPanel | null = null): MetricRow {
  const name = checkName(req.name);
  const template = findTemplate(name);
  const unit = checkUnit(req.unit ?? template?.unit);
  const decimals = checkDecimals(req.decimals ?? template?.decimals ?? 1);
  const better = req.better == null ? (template?.better ?? null) : checkBetter(req.better);
  const range = checkRange(req.low ?? template?.low ?? null, req.high ?? template?.high ?? null, decimals);
  const target = req.target == null ? null : checkValue(req.target, decimals, "The target");
  checkFree(name, null);
  const state = store();
  const n = state.nextId++;
  const metric: MockMetric = {
    id: mockId(1, n),
    urn: `urn:bsk:metric:${name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "")}-${hex(n, 6)}`,
    name,
    unit,
    decimals,
    target,
    better,
    catalog: template?.key ?? null,
    archivedAt: null,
    archivedOn: null,
    ...range,
    panel: panel ?? template?.panel ?? null,
  };
  state.metrics.push(metric);
  return metricRow(metric);
}

/**
 * Renames a metric or changes its unit, decimals, target, direction or normal range, as
 * `MetricService.SetAsync`.
 */
export function mockSetMetric(reference: string, req: MetricRequest): MetricRow {
  const empty =
    req.name == null &&
    req.unit == null &&
    req.decimals == null &&
    req.target == null &&
    !req.clearTarget &&
    req.better == null &&
    req.low == null &&
    !req.clearLow &&
    req.high == null &&
    !req.clearHigh;
  if (empty) {
    throw new Error("Say what to change: a name, a unit, decimals, a target, which way is better or the normal range.");
  }
  if (req.target != null && req.clearTarget) throw new Error("Set a target or clear it, not both.");
  if ((req.low != null && req.clearLow) || (req.high != null && req.clearHigh)) {
    throw new Error("Set an end of the normal range or clear it, not both.");
  }
  const metric = resolveMetric(reference);
  const name = req.name == null ? null : checkName(req.name);
  const decimals = req.decimals == null ? metric.decimals : checkDecimals(req.decimals);
  let unit = metric.unit;
  if (req.unit != null) {
    const next = checkUnit(req.unit);
    const count = readingsOf(metric.id).length;
    if (next !== metric.unit && count > 0) {
      throw new Error(
        `${metric.name} has ${count === 1 ? "1 reading" : `${count} readings`} in ${metric.unit}, so its unit stays: make a new metric for ${next}.`,
      );
    }
    unit = next;
  }
  let target = metric.target;
  if (req.target != null) target = checkValue(req.target, decimals, "The target");
  else if (req.clearTarget) target = null;
  else if (req.decimals != null && metric.target != null) checkValue(metric.target, decimals, "The target");
  const better = req.better == null ? metric.better : checkBetter(req.better);
  // The range as it will be, checked whole, as the kernel checks it.
  const range = checkRange(
    req.clearLow ? null : (req.low ?? metric.low),
    req.clearHigh ? null : (req.high ?? metric.high),
    decimals,
  );
  if (name != null && name !== metric.name) checkFree(name, metric.id);
  Object.assign(metric, { name: name ?? metric.name, unit, decimals, target, better, ...range });
  return metricRow(metric);
}

/**
 * Archives or restores a metric by its id or urn, as `ArchiveService` does for one: answers
 * false when `subject` isn't a metric, so the main store archives it instead.
 */
export function mockArchiveMetric(subject: string, archived: boolean): boolean {
  const wanted = subject.trim().toLowerCase();
  const metric = store().metrics.find((m) => m.id === wanted || m.urn.toLowerCase() === wanted);
  if (!metric) return false;
  if (archived && metric.archivedAt == null) {
    metric.archivedAt = nowIso();
    metric.archivedOn = todayIso();
  } else if (!archived && metric.archivedAt != null) {
    const taken = store().metrics.find((m) => m.archivedAt == null && same(m.name, metric.name));
    if (taken) throw new Error(`You already have a metric called '${metric.name}': rename one of them first.`);
    metric.archivedAt = null;
    metric.archivedOn = null;
  }
  return true;
}

// ---------------------------------------------------------------------------
// Readings (`MetricReadingService`)
// ---------------------------------------------------------------------------

function checkTime(iso: string): string {
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) throw new Error("Say when it was taken, as a date and time.");
  if (ms > Date.now() + SKEW_MS) throw new Error("A reading is taken now or earlier, not in the future.");
  if (ms < Date.UTC(1900, 0, 1)) throw new Error("A reading is taken in 1900 or later.");
  return iso;
}

function checkNote(note: string | undefined | null): string | null {
  const text = note?.trim() ?? "";
  if (!text) return null;
  if (hasBreak(text)) throw new Error("A reading's note is one line: take out the line break.");
  if (text.length > NOTE_MAX) throw new Error(`A reading's note is up to ${NOTE_MAX} characters; that one is ${text.length}.`);
  return text;
}

function resolveReading(id: string): MockReading {
  const found = store().readings.find((r) => r.id === id.trim().toLowerCase());
  if (!found) throw new Error(`No reading has the id ${id} (it may have been deleted).`);
  return found;
}

/** Logs a reading, now unless `takenAt` says when. */
export function mockLogReading(req: MetricReadingRequest): MetricReadingResult {
  const [logged] = mockLogReadings({
    readings: [{ metric: req.metric, value: req.value }],
    takenAt: req.takenAt,
    note: req.note,
  }).readings;
  return { reading: logged };
}

/**
 * Logs readings of several metrics at one time with one note, as `LogManyAsync`: every one is
 * checked before any is written, so a refusal writes none. Answers them in the order given.
 */
export function mockLogReadings(req: MetricReadingBatchRequest): MetricReadingsResult {
  const entries = req.readings ?? [];
  if (entries.length === 0) throw new Error("Give at least one reading: a metric and its value.");
  if (entries.length > MAX_TOGETHER) throw new Error(`Log up to ${MAX_TOGETHER} readings at once; that's ${entries.length}.`);
  const when = local(checkTime(req.takenAt ?? nowIso()));
  const note = checkNote(req.note);
  const seen = new Set<string>();
  const checked = entries.map((entry) => {
    const metric = resolveMetric(entry.metric ?? "");
    if (metric.archivedAt != null) throw new Error(`'${metric.name}' is archived: bring it back before logging to it.`);
    if (seen.has(metric.id)) throw new Error(`${metric.name} is given twice: log one value for each metric.`);
    seen.add(metric.id);
    return { metric, value: checkValue(entry.value, metric.decimals, metric.name) };
  });
  const state = store();
  const rows = checked.map(({ metric, value }) => {
    const row = reading(mockId(2, state.nextId++), metric, value, when, note);
    row.loggedAt = nowIso();
    state.readings.push(row);
    return readingRow(row, metric);
  });
  return { readings: rows };
}

/** Changes a reading's value, time or note; changing nothing writes nothing. It keeps its unit. */
export function mockEditReading(id: string, req: MetricReadingEditRequest): MetricReadingResult {
  const row = resolveReading(id);
  const metric = store().metrics.find((m) => m.id === row.metricId)!;
  const value = req.value == null ? row.value : checkValue(req.value, metric.decimals, metric.name);
  const when = req.takenAt == null ? null : local(checkTime(req.takenAt));
  const note = req.note == null ? row.note : checkNote(req.note);
  const moved = when != null && Date.parse(when.takenAt) !== Date.parse(row.takenAt);
  if (moved && row.visitId != null) {
    throw new Error(
      `That ${metric.name} result is part of a lab visit: change the visit's time instead, and its results move with it.`,
    );
  }
  if (value === row.value && !moved && note === row.note) return { reading: readingRow(row, metric) };
  Object.assign(row, { value, note, ...(moved ? when : {}), loggedAt: nowIso(), versions: row.versions + 1 });
  return { reading: readingRow(row, metric) };
}

/** Deletes a reading: the record keeps what it was (a voided version). */
export function mockDeleteReading(id: string): MetricReadingResult {
  const row = resolveReading(id);
  const state = store();
  state.readings = state.readings.filter((r) => r !== row);
  return { reading: null };
}

// ---------------------------------------------------------------------------
// Lab visits (MEASURE-4, `LabVisitService`)
// ---------------------------------------------------------------------------

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function resultsOf(visitId: string): MockReading[] {
  return store().readings.filter((r) => r.visitId === visitId);
}

/** A visit as the API answers it: its results in the report's order, and how many are outside their range. */
function visitRow(visit: MockVisit): LabVisitRow {
  const metrics = store().metrics;
  const results = resultsOf(visit.id)
    .map((r) => ({ r, m: metrics.find((m) => m.id === r.metricId) }))
    .filter((x): x is { r: MockReading; m: MockMetric } => x.m != null)
    .sort((a, b) => reportOrder(METRIC_CATALOG)(a.m, b.m))
    .map(({ r, m }) => readingRow(r, m));
  return { ...visit, results, outside: results.filter((r) => r.flag != null).length };
}

function newestVisitFirst(a: MockVisit, b: MockVisit): number {
  return Date.parse(b.collectedAt) - Date.parse(a.collectedAt) || b.id.localeCompare(a.id);
}

/** Every visit with its results, newest collected first (`GET /api/measurements/visits`). */
export function mockLabVisits(): LabVisitsRead {
  return { today: todayIso(), visits: [...store().visits].sort(newestVisitFirst).map(visitRow) };
}

/** A visit named by its id or the start of it (at least 4 hex digits), as `LabVisitReads.ResolveAsync`. */
function resolveVisit(reference: string): MockVisit {
  const wanted = reference?.trim().toLowerCase() ?? "";
  const visits = store().visits;
  if (UUID.test(wanted)) {
    const found = visits.find((v) => v.id === wanted);
    if (!found) throw new Error(`No lab visit has the id ${wanted} (it may have been deleted).`);
    return found;
  }
  if (wanted.length < 4 || !/^[0-9a-f-]+$/.test(wanted)) {
    throw new Error(`'${reference}' isn't a lab visit's id: give at least its first 4 hex digits.`);
  }
  const matches = visits.filter((v) => v.id.startsWith(wanted));
  if (matches.length === 0) throw new Error(`No lab visit's id starts with '${wanted}'.`);
  if (matches.length > 1) throw new Error(`'${wanted}' starts ${matches.length} lab visits' ids: give more of the id.`);
  return matches[0];
}

function checkCollected(iso: string): string {
  const ms = Date.parse(iso);
  if (Number.isNaN(ms)) throw new Error("Say when the sample was collected, as a date and time.");
  if (ms > Date.now() + SKEW_MS) throw new Error("A lab visit is collected now or earlier, not in the future.");
  if (ms < Date.UTC(1900, 0, 1)) throw new Error("A lab visit is collected in 1900 or later.");
  return iso;
}

/** A short optional line as kept: null when blank, else trimmed, as `MetricLimits.CheckLine`. */
function checkLine(text: string, max: number, what: string): string | null {
  const problem = lineProblem(text, max, what);
  if (problem) throw new Error(problem);
  return text.trim() || null;
}

/** The decimal places a number is written with: 1.02 has 2. */
function placesOf(value: number): number {
  const text = String(value);
  const dot = text.indexOf(".");
  return dot < 0 ? 0 : text.length - dot - 1;
}

/** One result as it will be written: its test (made first when it's new) and the result it keeps. */
interface ResultPlan {
  metric: MockMetric | null;
  template: MetricTemplate | undefined;
  kept: MockReading | null;
  name: string;
  unit: string;
  decimals: number;
  value: number;
  low: number | null;
  high: number | null;
  note: string | null;
}

/** The metric a result names, or null when it names none. */
function findTest(reference: string | undefined): MockMetric | null {
  if (!reference?.trim()) throw new Error("Say which test each result is for, by its name, e.g. Glucose.");
  try {
    return resolveMetric(reference);
  } catch {
    return null;
  }
}

/**
 * Each result checked and matched to its test and to the visit's result it keeps, before anything
 * is written, as the kernel's `Plan`: one named by id keeps that one; one without keeps the
 * visit's result of the same test.
 */
function planResults(entries: readonly LabResultRequest[], current: readonly MockReading[]): ResultPlan[] {
  const metrics = store().metrics;
  const claimed = new Set<string>();
  const named = new Map<LabResultRequest, MockReading>();
  for (const entry of entries) {
    const wanted = entry.reading?.trim().toLowerCase();
    if (!wanted) continue;
    const matches = current.filter((c) => (UUID.test(wanted) ? c.id === wanted : c.id.startsWith(wanted)));
    if (matches.length !== 1 || wanted.length < 4) throw new Error(`'${entry.reading!.trim()}' isn't one of this visit's results.`);
    if (claimed.has(matches[0].id)) throw new Error("A result is given twice: a visit has one result for each test.");
    claimed.add(matches[0].id);
    named.set(entry, matches[0]);
  }
  const seen = new Set<string>();
  return entries.map((entry) => {
    let kept = named.get(entry) ?? null;
    const metric = kept ? metrics.find((m) => m.id === kept!.metricId)! : findTest(entry.metric);
    let name: string;
    let unit: string;
    let decimals: number;
    let template: MetricTemplate | undefined;
    if (!metric) {
      const typedName = checkName(entry.metric);
      template = findTemplate(typedName);
      if (!template && !entry.unit?.trim()) {
        throw new Error(
          `'${typedName}' isn't a test you track yet: give its unit to add it, as the report prints it (e.g. mIU/L).`,
        );
      }
      name = template?.name ?? typedName;
      unit = template?.unit ?? checkUnit(entry.unit);
      decimals =
        template?.decimals ??
        Math.min(DECIMALS_MAX, Math.max(...[entry.value, entry.low ?? 0, entry.high ?? 0].map(placesOf)));
    } else {
      if (metric.archivedAt != null && !kept) {
        throw new Error(`'${metric.name}' is archived: bring it back before adding results to it.`);
      }
      name = metric.name;
      unit = metric.unit;
      decimals = metric.decimals;
      if (!kept) {
        kept = current.find((c) => c.metricId === metric.id && !claimed.has(c.id)) ?? null;
        if (kept) claimed.add(kept.id);
      }
    }
    const key = metric?.id ?? name.toLowerCase();
    if (seen.has(key)) throw new Error(`${name} is given twice: a visit has one result for each test.`);
    seen.add(key);
    const value = checkValue(entry.value, decimals, name);
    let range: { low: number | null; high: number | null };
    try {
      range = checkRange(entry.low ?? null, entry.high ?? null, decimals);
    } catch (e) {
      throw new Error(`${name}: ${(e as Error).message}`);
    }
    return { metric, template, kept, name, unit, decimals, value, ...range, note: checkNote(entry.note) };
  });
}

/**
 * Each test's range becomes its newest result's printed one, when that differs (§4.9). A range
 * the test can no longer show (its decimal places were cut since) is left as it is.
 */
function syncRanges(ids: Iterable<string>) {
  for (const id of new Set(ids)) {
    const metric = store().metrics.find((m) => m.id === id);
    const newest = readingsOf(id)
      .filter(hasOwnRange)
      .sort((a, b) => Date.parse(a.takenAt) - Date.parse(b.takenAt) || a.id.localeCompare(b.id))
      .at(-1);
    if (!metric || !newest || (metric.low === newest.low && metric.high === newest.high)) continue;
    try {
      Object.assign(metric, checkRange(newest.low, newest.high, metric.decimals));
    } catch {
      // Kept as it is, as the kernel keeps it.
    }
  }
}

/** Saves a visit and all its results as one write, every refusal checked first (`SaveCoreAsync`). */
function saveVisit(existing: MockVisit | null, req: LabVisitRequest): LabVisitRow {
  const entries = req.results ?? [];
  if (entries.length > MAX_TOGETHER) throw new Error(`A visit takes up to ${MAX_TOGETHER} results; that's ${entries.length}.`);
  const now = nowIso();
  const when = local(checkCollected(req.collectedAt ?? existing?.collectedAt ?? now));
  const lab = req.lab == null ? (existing?.lab ?? null) : checkLine(req.lab, LAB_MAX, "A lab's name");
  const note = req.note == null ? (existing?.note ?? null) : checkLine(req.note, NOTE_MAX, "A visit's note");
  const current = existing ? resultsOf(existing.id) : [];
  const plans = planResults(entries, current);

  // Everything is checked: make the tests that aren't tracked yet, then write it all.
  const state = store();
  for (const plan of plans.filter((p) => p.metric == null)) {
    const row = plan.template
      ? mockAddMetric({ name: plan.template.name })
      : mockAddMetric({ name: plan.name, unit: plan.unit, decimals: plan.decimals }, "other");
    plan.metric = state.metrics.find((m) => m.id === row.id)!;
  }

  let visit = existing;
  if (!visit) {
    visit = {
      id: mockId(3, state.nextId++),
      lab,
      note,
      collectedAt: when.takenAt,
      collectedOn: when.takenOn,
      collectedLocal: when.takenLocal,
      report: null,
      loggedAt: now,
      versions: 1,
    };
    state.visits.push(visit);
  } else if (Date.parse(visit.collectedAt) !== Date.parse(when.takenAt) || visit.lab !== lab || visit.note !== note) {
    Object.assign(visit, {
      lab,
      note,
      collectedAt: when.takenAt,
      collectedOn: when.takenOn,
      collectedLocal: when.takenLocal,
      loggedAt: now,
      versions: visit.versions + 1,
    });
  }

  for (const plan of plans) {
    const metric = plan.metric!;
    const kept = plan.kept;
    const result = { value: plan.value, low: plan.low, high: plan.high, note: plan.note };
    if (kept) {
      const same =
        kept.value === plan.value &&
        kept.low === plan.low &&
        kept.high === plan.high &&
        kept.note === plan.note &&
        Date.parse(kept.takenAt) === Date.parse(when.takenAt);
      if (!same) Object.assign(kept, result, when, { loggedAt: now, versions: kept.versions + 1 });
      continue;
    }
    state.readings.push({
      ...reading(mockId(2, state.nextId++), metric, plan.value, when, plan.note),
      ...result,
      visitId: visit.id,
      loggedAt: now,
    });
  }

  const keptIds = new Set(plans.map((p) => p.kept?.id).filter((id): id is string => id != null));
  const dropped = current.filter((c) => !keptIds.has(c.id));
  state.readings = state.readings.filter((r) => !dropped.includes(r));
  syncRanges([...plans.map((p) => p.metric!.id), ...dropped.map((d) => d.metricId)]);
  return visitRow(visit);
}

/** Logs a new visit with its results (`POST /api/measurements/visits`). */
export function mockLogLabVisit(req: LabVisitRequest): LabVisitResult {
  return { visit: saveVisit(null, req) };
}

/** Saves a visit as given: its results become the ones in `req` (`POST …/visits/{id}`). */
export function mockSaveLabVisit(reference: string, req: LabVisitRequest): LabVisitResult {
  return { visit: saveVisit(resolveVisit(reference), req) };
}

/** Deletes a visit and its results; each test's range goes back to its newest report left. */
export function mockDeleteLabVisit(reference: string): LabVisitResult {
  const visit = resolveVisit(reference);
  const results = resultsOf(visit.id);
  const state = store();
  state.visits = state.visits.filter((v) => v !== visit);
  state.readings = state.readings.filter((r) => r.visitId !== visit.id);
  syncRanges(results.map((r) => r.metricId));
  return { visit: null };
}

/** A report's type by what it claims or its name; the kernel reads its first bytes instead. */
function reportType(blob: Blob, filename: string | null): string | null {
  if ((REPORT_TYPES as readonly string[]).includes(blob.type)) return blob.type;
  const ext = filename?.toLowerCase().split(".").at(-1);
  const byExt: Record<string, string> = {
    pdf: "application/pdf",
    jpg: "image/jpeg",
    jpeg: "image/jpeg",
    png: "image/png",
    webp: "image/webp",
  };
  return (ext && byExt[ext]) || null;
}

/** Adds a visit's report, a PDF or a photo up to 10 MB, in place of any before it. */
export function mockAttachLabReport(reference: string, blob: Blob, filename?: string): LabVisitResult {
  const visit = resolveVisit(reference);
  const name = filename?.replace(/\\/g, "/").split("/").at(-1)?.trim() || null;
  const problem = reportProblem({ size: blob.size, type: "" });
  if (problem) throw new Error(problem);
  const contentType = reportType(blob, name);
  if (!contentType) throw new Error("That isn't a PDF or a photo: send the report as a PDF, or a JPEG, PNG or WebP photo of it.");
  const state = store();
  const report: LabReportRow = { id: mockId(4, state.nextId++), filename: name, contentType, byteSize: blob.size };
  state.reportUrls.set(report.id, objectUrlFor(blob));
  Object.assign(visit, { report, loggedAt: nowIso(), versions: visit.versions + 1 });
  return { visit: visitRow(visit) };
}

/** Takes a visit's report off; its bytes stay in the record. A visit with none stays as it is. */
export function mockRemoveLabReport(reference: string): LabVisitResult {
  const visit = resolveVisit(reference);
  if (visit.report) Object.assign(visit, { report: null, loggedAt: nowIso(), versions: visit.versions + 1 });
  return { visit: visitRow(visit) };
}

/** A report's bytes as a URL, or null when `id` isn't a report. */
export function mockLabReportUrl(id: string): string | null {
  return store().reportUrls.get(id) ?? null;
}
