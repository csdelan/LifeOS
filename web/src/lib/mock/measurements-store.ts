/**
 * The mock store's measurements (MEASURE-1 to MEASURE-3, D20, measurements plan §4.7–§4.8):
 * Weight, with a 185 lb goal and six weeks of morning weigh-ins drifting down, a few days
 * skipped and one evening weigh-in on top of a morning one; blood pressure (its two starter
 * metrics, with their normal ranges) twice a week, the newest pair two days ago with its
 * systolic high; and Waist weekly, with no range or goal. All relative to today and the same on
 * every load; the newest weigh-in is yesterday's, so today's is Chris's to log. Body fat and
 * VO2 max (lab) are left for Add a metric. It answers as `/api/measurements` does, with the
 * kernel's refusals word for word, flags readings against each metric's range as it is now, and
 * works out the day values, trend and goal with `lib/metric-trend.ts`, the kernel's own
 * formulas. The mock's home zone is the browser's.
 */
import { addDays, nowIso, todayIso } from "@/lib/dates";
import {
  DECIMALS_MAX,
  DIASTOLIC,
  MAX_DAYS,
  NAME_MAX,
  NOTE_MAX,
  SKEW_MS,
  SYSTOLIC,
  UNIT_MAX,
  VALUE_MAX,
  WAIST,
  WEIGHT,
  findTemplate,
  flagOf,
  formatValue,
  type MetricTemplate,
} from "@/lib/measurements";
import { dayValues, goal, roundAway, trend } from "@/lib/metric-trend";
import type {
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
}

/**
 * A reading as `v_metric_reading` reads it: its newest version; a deleted one is gone. Its flag
 * is worked out on read, from its metric's range as it is then.
 */
type MockReading = Omit<MetricReadingRow, "flag">;

interface MeasurementsState {
  metrics: MockMetric[];
  readings: MockReading[];
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
    low: template.low ?? null,
    high: template.high ?? null,
  };
}

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
  return { metrics: [weight, systolic, diastolic, waist], readings, nextId: n };
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
  };
}

// ---------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------

function readingsOf(metricId: string): MockReading[] {
  return store().readings.filter((r) => r.metricId === metricId);
}

/** A reading as the API answers it: flagged against its metric's range as it is now. */
function readingRow(r: MockReading, metric?: MockMetric): MetricReadingRow {
  const of = metric ?? store().metrics.find((m) => m.id === r.metricId);
  return { ...r, flag: of ? flagOf(r.value, of) : null };
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
 * Adds a metric; a starter-list name (Weight, Systolic BP) fills in the unit, decimals, which way
 * is better and the normal range.
 */
export function mockAddMetric(req: MetricRequest & { name: string }): MetricRow {
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
