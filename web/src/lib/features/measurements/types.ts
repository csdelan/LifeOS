/**
 * Measurements (MEASURE-1 to MEASURE-4, D20, measurements plan §4.6, §4.8, §4.9): `/api/measurements`.
 * A metric is something Chris measures (Weight); a reading is one measurement of it, taken at an
 * instant. A lab visit (M4) is one blood draw: its results are readings of lab tests (metrics on a
 * panel), each with the range printed beside it. Days are home-zone days (`yyyy-MM-dd`). Readings
 * are information only: no write earns or moves a level.
 *
 * A type that needs no narrowing is the generated one (`ApiSchema`).
 */
import type { ApiSchema } from "@/lib/production-ui-types";

/** Which way is good for a metric: it sets the goal date's direction. */
export type MetricBetter = "lower" | "higher";

/** Where a reading sits against its metric's normal range (MEASURE-3): below it or above it. */
export type MetricFlag = "low" | "high";

/** The lab panel a test is on (MEASURE-4): the starter list's three, and `other` for a test added from a visit. */
export type LabPanel = "cmp" | "lipids" | "a1c" | "other";

/** How a metric stands against its target (measurements plan §2.3). */
export type MetricGoalStatus = "no_target" | "too_few" | "reached" | "heading" | "far" | "not_heading";

/** One reading (MEASURE-1): its value in `unit`, when it was taken, and how many versions it has. */
export interface MetricReadingRow {
  id: string;
  metricId: string;
  value: number;
  /** The metric's unit when the reading was written. */
  unit: string;
  note: string | null;
  /** ISO instant it was taken. */
  takenAt: string;
  /** The home-zone day it was taken. */
  takenOn: string;
  /** The home-zone wall-clock time it was taken, `yyyy-MM-ddTHH:mm:ss`. */
  takenLocal: string;
  /** ISO instant it was last written. */
  loggedAt: string;
  versions: number;
  /**
   * `low` or `high` outside its range (the ends themselves are normal); null inside it or with no
   * range. A lab result's range is the one printed beside it (`low`, `high`), for good; any other
   * reading's is its metric's as it is now.
   */
  flag: MetricFlag | null;
  /** The lab visit a result belongs to (MEASURE-4); null for any other reading. */
  visitId: string | null;
  /** The low end of the range printed beside a lab result; null for any other reading. */
  low: number | null;
  /** The high end of the range printed beside a lab result. */
  high: number | null;
}

/** One metric (MEASURE-1), with its normal range (MEASURE-3), how many readings it has and its newest one. */
/**
 * A metric on the starter list (measurements plan §2.1, `GET /api/measurements/catalog`): what
 * making one by that name fills in. `better` is which way is good (most lab tests have none);
 * `low` and `high` the normal range it starts with (blood pressure's, §4.8, and the lab tests'
 * common adult ones, §4.9); `panel` the lab panel a test is on, null for a body metric.
 */
export interface MetricTemplate {
  key: string;
  name: string;
  unit: string;
  decimals: number;
  better: MetricBetter | null;
  low: number | null;
  high: number | null;
  panel: LabPanel | null;
}

export interface MetricRow {
  id: string;
  urn: string;
  name: string;
  unit: string;
  /** 0–3: the decimal places a reading takes and shows. */
  decimals: number;
  target: number | null;
  better: MetricBetter | null;
  /** The starter-list key it was made from (`weight`), kept through a rename. */
  catalog: string | null;
  archivedAt: string | null;
  archivedOn: string | null;
  /** The normal range's low end, or null; a reading below it is flagged `low`. */
  low: number | null;
  /** The normal range's high end, or null; a reading above it is flagged `high`. */
  high: number | null;
  /** The lab panel a test is on (MEASURE-4); null for a metric that isn't a lab test. A test's range follows its newest report. */
  panel: LabPanel | null;
  readings: number;
  lastReading: MetricReadingRow | null;
}

/** One day's value (its first reading) and the trend on that day (measurements plan §2.2). */
export type MetricDayRow = ApiSchema<"MetricDayRow">;

/**
 * Where a metric stands against its target (§2.3): `now` is the trend on the newest day,
 * `ratePerWeek` the rate over the basis, and `reachedBy` the day the rate reaches the target
 * (`heading` only). The basis is the days the rate was measured over.
 */
export interface MetricGoalRow {
  status: MetricGoalStatus;
  target: number | null;
  direction: MetricBetter | null;
  now: number | null;
  ratePerWeek: number | null;
  reachedBy: string | null;
  basis: { days: number; from: string | null; to: string | null };
}

/**
 * `GET /api/measurements/metrics/{id}?days=N`: the readings from `from` (all of them when null)
 * to `today`, newest first; the day values with the trend, oldest first; and the goal, which
 * reads the newest 28 days of all history.
 */
export interface MetricDetailRead {
  today: string;
  from: string | null;
  metric: MetricRow;
  readings: MetricReadingRow[];
  days: MetricDayRow[];
  goal: MetricGoalRow;
}

/** A reading write's answer: the reading as it now reads, or null after a delete. */
export interface MetricReadingResult {
  reading: MetricReadingRow | null;
}

/** Readings logged together: each as it now reads, in the order given. */
export interface MetricReadingsResult {
  readings: MetricReadingRow[];
}

/**
 * Adding or changing a metric. A starter-list name (Weight, Systolic BP) fills in the unit,
 * decimals, which way is better and the normal range when a new one leaves them out;
 * `clearTarget`, `clearLow` and `clearHigh` remove the target or an end of the range.
 */
export interface MetricRequest {
  name?: string;
  unit?: string;
  decimals?: number;
  target?: number;
  clearTarget?: boolean;
  better?: MetricBetter;
  low?: number;
  clearLow?: boolean;
  high?: number;
  clearHigh?: boolean;
}

/** Logging a reading: the metric (id or name), the value, when it was taken (now when left out), a note. */
export interface MetricReadingRequest {
  metric: string;
  value: number;
  /** ISO instant; now when left out. */
  takenAt?: string;
  note?: string;
}

/**
 * Logging readings of several metrics taken together (blood pressure's two, MEASURE-3): one
 * time (now when left out) and one note. All or nothing.
 */
export interface MetricReadingBatchRequest {
  readings: { metric: string; value: number }[];
  takenAt?: string;
  note?: string;
}

/** Changing a reading: any of the value, the time and the note; a blank note removes it. */
export interface MetricReadingEditRequest {
  value?: number;
  takenAt?: string;
  note?: string;
}

/** A lab visit's report (MEASURE-4): a PDF, or a photo of the paper. Its bytes come from `artifactBytesUrl(id)`. */
export interface LabReportRow {
  id: string;
  filename: string | null;
  contentType: string;
  byteSize: number;
}

/**
 * One lab visit (MEASURE-4, measurements plan §4.9): the lab, a note, when the sample was
 * collected (an instant, and in the home zone its day and wall-clock time), the report, its
 * results in the order its report prints them (by panel), and how many are outside the range
 * printed beside them.
 */
export interface LabVisitRow {
  id: string;
  lab: string | null;
  note: string | null;
  collectedAt: string;
  collectedOn: string;
  collectedLocal: string;
  report: LabReportRow | null;
  results: MetricReadingRow[];
  outside: number;
  loggedAt: string;
  versions: number;
}

/** `GET /api/measurements/visits`: today in the home zone and every visit, newest collected first. */
export interface LabVisitsRead {
  today: string;
  visits: LabVisitRow[];
}

/** A visit write's answer: the visit as it now reads, or null after a delete. */
export interface LabVisitResult {
  visit: LabVisitRow | null;
}

/**
 * One result in a visit: the test (a metric's id or name), its value, the range printed beside
 * it, a note, the unit for a test that isn't tracked or on the starter list, and in an edit the
 * result it keeps (`reading`, its id).
 */
export interface LabResultRequest {
  metric: string;
  value: number;
  low?: number;
  high?: number;
  note?: string;
  unit?: string;
  reading?: string;
}

/**
 * Logging or saving a lab visit: when the sample was collected (now for a new visit when left
 * out; kept by an edit), the lab and a note (an edit keeps each when left out and clears it when
 * blank), and all its results: an edit's results become these, so one left out is deleted.
 */
export interface LabVisitRequest {
  collectedAt?: string;
  lab?: string;
  note?: string;
  results: LabResultRequest[];
}
