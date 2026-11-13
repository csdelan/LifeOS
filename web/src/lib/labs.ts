/**
 * Lab results (MEASURE-4, D20, measurements plan §4.9): the panels, how a visit and its results
 * read, the visit sheet's rows and its checks (in the kernel's words), and the live API's visits
 * with their numbers as numbers. The tests themselves are metrics, on the starter list the API
 * serves (`GET /api/measurements/catalog`, code review CR73); a test's range follows its newest
 * report.
 */
import { formatDay } from "@/lib/dates";
import {
  DECIMALS_MAX,
  NAME_MAX,
  NOTE_MAX,
  UNIT_MAX,
  normalizeReading,
  parseValue,
  rangeProblem,
  rangeText,
  singleGroup,
  type MetricGroup,
  type MetricTemplate,
} from "@/lib/measurements";
import type {
  LabPanel,
  LabReportRow,
  LabResultRequest,
  LabVisitResult,
  LabVisitRow,
  LabVisitsRead,
  MetricReadingRow,
  MetricRow,
} from "@/lib/production-ui-types";

/** The kernel's `LabLimits`: a lab's name, a report's size, and the results a visit takes. */
export const LAB_MAX = 60;
export const REPORT_MAX_BYTES = 10 * 1024 * 1024;
export const MAX_RESULTS = 50;

/** What a report may be: a PDF, or a photo of the paper. */
export const REPORT_TYPES = ["application/pdf", "image/jpeg", "image/png", "image/webp"] as const;

/** A panel: its key and its heading. Its tests on the starter list are `panelTests`'. */
export interface PanelInfo {
  key: LabPanel;
  name: string;
}

/** The panels in the order a report prints them, as the kernel's `LabPanels`. */
export const PANELS: readonly PanelInfo[] = [
  { key: "cmp", name: "CMP" },
  { key: "lipids", name: "Lipid panel" },
  { key: "a1c", name: "A1c" },
  { key: "other", name: "Other tests" },
];

/** The starter list's tests on `panel`, in the order its report prints them (none on Other tests). */
export function panelTests(catalog: readonly MetricTemplate[], panel: LabPanel): MetricTemplate[] {
  return catalog.filter((t) => t.panel === panel && panel !== "other");
}

/** The starter list's three panels, which a first visit starts with. */
export const STARTER_PANELS: readonly LabPanel[] = ["cmp", "lipids", "a1c"];

export function panelOf(key: LabPanel | null): PanelInfo {
  return PANELS.find((p) => p.key === key) ?? PANELS[PANELS.length - 1];
}

/** Where a panel sits; past the end for a metric on none. */
function panelOrder(key: LabPanel | null): number {
  const i = PANELS.findIndex((p) => p.key === key);
  return i < 0 ? PANELS.length : i;
}

export function isLabTest(metric: Pick<MetricRow, "panel">): boolean {
  return metric.panel != null;
}

type Ordered = Pick<MetricRow, "panel" | "catalog" | "name">;

/**
 * The report's order: by panel, then `catalog`'s order, then by name. As the kernel orders a
 * visit's results.
 */
export function reportOrder(catalog: readonly MetricTemplate[]): (a: Ordered, b: Ordered) => number {
  const index = new Map(catalog.map((t, i) => [t.key, i]));
  const at = (key: string | null) => (key == null ? undefined : index.get(key)) ?? catalog.length;
  return (a, b) => panelOrder(a.panel) - panelOrder(b.panel) || at(a.catalog) - at(b.catalog) || a.name.localeCompare(b.name);
}

/** The lab tests not archived, as rows under their panel's heading (§4.9). A panel with none is left out. */
export function testsByPanel(
  metrics: readonly MetricRow[],
  catalog: readonly MetricTemplate[],
): { panel: PanelInfo; groups: MetricGroup[] }[] {
  const tests = metrics.filter((m) => m.panel != null && m.archivedAt == null).sort(reportOrder(catalog));
  return PANELS.map((panel) => ({
    panel,
    groups: tests.filter((m) => m.panel === panel.key).map((m) => ({ ...singleGroup(m), noun: "result" })),
  })).filter((p) => p.groups.length > 0);
}

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------

/** "Nov 9, 2026". */
export function visitDay(day: string): string {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

/** "Nov 9, 2026 · Quest Diagnostics", or the day alone with no lab. */
export function visitTitle(visit: Pick<LabVisitRow, "collectedOn" | "lab">): string {
  const day = visitDay(visit.collectedOn);
  return visit.lab ? `${day} · ${visit.lab}` : day;
}

/** "21 results, 3 outside the range", "1 result, none outside the range", "No results yet". */
export function visitSummary(visit: Pick<LabVisitRow, "results" | "outside">): string {
  const n = visit.results.length;
  if (n === 0) return "No results yet";
  const count = n === 1 ? "1 result" : `${n} results`;
  return visit.outside === 0 ? `${count}, none outside the range` : `${count}, ${visit.outside} outside the range`;
}

/** The toast after a visit write: "Saved 21 results from Nov 9", "Saved the lab visit from Nov 9", "Deleted the lab visit". */
export function visitMessage(
  kind: "saved" | "deleted",
  visit: Pick<LabVisitRow, "collectedOn" | "results"> | null,
  today: string,
): string {
  if (kind === "deleted" || !visit) return "Deleted the lab visit";
  const day = visit.collectedOn.slice(0, 4) === today.slice(0, 4) ? formatDay(visit.collectedOn) : visitDay(visit.collectedOn);
  const n = visit.results.length;
  if (n === 0) return `Saved the lab visit from ${day}`;
  return `Saved ${n === 1 ? "1 result" : `${n} results`} from ${day}`;
}

/** The range printed beside a result, in words with its unit: "65–99 mg/dL", "up to 99 mg/dL"; null with none. */
export function resultRange(reading: Pick<MetricReadingRow, "low" | "high" | "unit">, decimals: number): string | null {
  return rangeText({ low: reading.low, high: reading.high, unit: reading.unit, decimals });
}

/** A visit's results under their panel's heading, each with its test, in the report's order. */
export function resultsByPanel(
  visit: Pick<LabVisitRow, "results">,
  metrics: readonly MetricRow[],
  catalog: readonly MetricTemplate[],
): { panel: PanelInfo; results: { reading: MetricReadingRow; metric: MetricRow }[] }[] {
  const byId = new Map(metrics.map((m) => [m.id, m]));
  const order = reportOrder(catalog);
  const rows = visit.results
    .map((reading) => ({ reading, metric: byId.get(reading.metricId) }))
    .filter((r): r is { reading: MetricReadingRow; metric: MetricRow } => r.metric != null)
    .sort((a, b) => order(a.metric, b.metric));
  return PANELS.map((panel) => ({ panel, results: rows.filter((r) => panelOf(r.metric.panel).key === panel.key) })).filter(
    (p) => p.results.length > 0,
  );
}

/** The labs visits have named, newest first, each once whatever its case: the sheet's suggestions. */
export function labsUsed(visits: readonly Pick<LabVisitRow, "lab">[]): string[] {
  const seen = new Set<string>();
  const labs: string[] = [];
  for (const v of visits) {
    if (!v.lab || seen.has(v.lab.toLowerCase())) continue;
    seen.add(v.lab.toLowerCase());
    labs.push(v.lab);
  }
  return labs;
}

// ---------------------------------------------------------------------------
// When the sample was collected
// ---------------------------------------------------------------------------

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** The local day of an instant, `yyyy-MM-dd`. */
function localDay(ms: number): string {
  const d = new Date(ms);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * The instant a visit's day and optional time (`HH:mm`) name: that time, else noon, as a reading
 * with no time is (§6) — or now, for today before noon. Null when the day or time is incomplete.
 */
export function collectedInstant(day: string, time: string, now: number): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return null;
  const [y, m, d] = day.split("-").map(Number);
  const clock = time.trim();
  if (clock) {
    if (!/^\d{2}:\d{2}$/.test(clock)) return null;
    const [h, min] = clock.split(":").map(Number);
    return new Date(y, m - 1, d, h, min).toISOString();
  }
  const noon = new Date(y, m - 1, d, 12, 0).getTime();
  return new Date(noon > now && day === localDay(now) ? now : noon).toISOString();
}

// ---------------------------------------------------------------------------
// The visit sheet's rows
// ---------------------------------------------------------------------------

/** A test in the visit sheet: its result and the range printed beside it, as typed. */
export interface VisitTestRow {
  /** The starter-list key, the metric's id, or `new-N` for a test added in the sheet. */
  key: string;
  panel: LabPanel;
  name: string;
  unit: string;
  /** The places its values take; a test added in the sheet takes up to 3, and the kernel keeps as many as it's given. */
  decimals: number;
  /** The tracked test; null for one the save makes, from the starter list or as typed. */
  metricId: string | null;
  /** The visit's result it changes, in an edit. */
  readingId: string | null;
  value: string;
  low: string;
  high: string;
  /** Typed in with Add a test: its name and unit are the user's. */
  added: boolean;
}

function typed(value: number | null | undefined, decimals: number): string {
  return value == null ? "" : value.toFixed(decimals);
}

/**
 * The sheet's rows (§4.9): each panel's starter tests on `catalog` (as tracked, when they are),
 * then his own tests on that panel, then Other tests; in an edit, every test the visit has a
 * result for. Each is filled in from the visit's result, else with the test's range now (its
 * newest report's), else with the starter list's. A test archived since is left out unless the
 * visit has it.
 */
export function visitRows(
  metrics: readonly MetricRow[],
  visit: Pick<LabVisitRow, "results"> | null,
  catalog: readonly MetricTemplate[],
): VisitTestRow[] {
  const results = new Map((visit?.results ?? []).map((r) => [r.metricId, r]));
  const rows: VisitTestRow[] = [];
  const used = new Set<string>();
  const row = (panel: LabPanel, metric: MetricRow | undefined, template?: MetricTemplate): void => {
    if (metric && (used.has(metric.id) || (metric.archivedAt != null && !results.has(metric.id)))) return;
    const result = metric ? results.get(metric.id) : undefined;
    const decimals = metric?.decimals ?? template!.decimals;
    const low = result ? result.low : (metric?.low ?? template?.low ?? null);
    const high = result ? result.high : (metric?.high ?? template?.high ?? null);
    if (metric) used.add(metric.id);
    rows.push({
      key: metric?.id ?? template!.key,
      panel,
      name: metric?.name ?? template!.name,
      unit: metric?.unit ?? template!.unit,
      decimals,
      metricId: metric?.id ?? null,
      readingId: result?.id ?? null,
      value: typed(result?.value, decimals),
      low: typed(low, decimals),
      high: typed(high, decimals),
      added: false,
    });
  };
  const tests = [...metrics].sort(reportOrder(catalog));
  // The live one first, as the kernel resolves a name.
  const tracked = (key: string) =>
    tests.find((m) => m.catalog === key && (m.archivedAt == null || results.has(m.id))) ?? undefined;
  for (const panel of PANELS) {
    const templates = panelTests(catalog, panel.key);
    for (const template of templates) {
      const metric = tracked(template.key);
      // Archived and not on this visit: he stopped tracking it, so the sheet doesn't offer it.
      if (!metric && tests.some((m) => m.catalog === template.key)) continue;
      row(panel.key, metric, template);
    }
    for (const m of tests.filter((m) => m.panel === panel.key && !templates.some((t) => t.key === m.catalog))) {
      row(panel.key, m);
    }
  }
  // A result whose test has left the panels (it was renamed or moved) still shows, under Other tests.
  for (const m of tests.filter((m) => results.has(m.id))) row("other", m);
  return rows;
}

/** A blank row for Add a test, under Other tests. */
export function addedRow(n: number): VisitTestRow {
  return {
    key: `new-${n}`,
    panel: "other",
    name: "",
    unit: "",
    decimals: DECIMALS_MAX,
    metricId: null,
    readingId: null,
    value: "",
    low: "",
    high: "",
    added: true,
  };
}

/**
 * The panels the sheet opens with: in an edit, the ones the visit has results on; for a new
 * visit, the ones the newest visit had; the starter list's three for the first. Other tests open
 * whenever there are any.
 */
export function initialPanels(
  rows: readonly VisitTestRow[],
  editing: boolean,
  newest: Pick<LabVisitRow, "results"> | null,
  metrics: readonly MetricRow[],
): Set<LabPanel> {
  if (editing) return new Set(rows.filter((r) => r.readingId != null).map((r) => r.panel));
  const panels = new Set<LabPanel>();
  for (const r of newest?.results ?? []) {
    const panel = metrics.find((m) => m.id === r.metricId)?.panel;
    if (panel) panels.add(panel);
  }
  return panels.size > 0 ? panels : new Set(STARTER_PANELS);
}

/** A control character or one of Unicode's own line breaks, as the kernel's `IsBreak`. */
function hasBreak(text: string): boolean {
  return [...text].some((ch) => {
    const code = ch.charCodeAt(0);
    return code < 0x20 || (code >= 0x7f && code <= 0x9f) || code === 0x2028 || code === 0x2029;
  });
}

/** A short line checked as `MetricLimits.CheckLine` checks one; null when it's fine. */
export function lineProblem(text: string, max: number, what: string): string | null {
  const t = text.trim();
  if (hasBreak(t)) return `${what} is one line: take out the line break.`;
  if (t.length > max) return `${what} is up to ${max} characters; that one is ${t.length}.`;
  return null;
}

function named(name: string, problem: string): string {
  return problem.startsWith(name) ? problem : `${name}: ${problem}`;
}

/**
 * The rows the sheet shows, checked as the kernel checks a visit (`LabVisitService`): the results
 * to send, in order, or the first problem in its words. A row with a blank result is skipped
 * (in an edit, that deletes the visit's result); the range's ends are optional.
 */
export function checkRows(rows: readonly VisitTestRow[]): { results: LabResultRequest[] } | { problem: string } {
  const results: LabResultRequest[] = [];
  const seen = new Set<string>();
  for (const row of rows) {
    const blank = row.value.trim() === "" && (!row.added || (row.name.trim() === "" && row.unit.trim() === ""));
    if (blank) continue;
    const name = row.name.trim();
    if (row.added) {
      if (!name) return { problem: "Give the test a name, e.g. TSH." };
      const nameProblem = lineProblem(name, NAME_MAX, "A test's name");
      if (nameProblem) return { problem: nameProblem };
      if (!row.unit.trim()) return { problem: `Give ${name}'s unit, as the report prints it (e.g. mIU/L).` };
      const unitProblem = lineProblem(row.unit, UNIT_MAX, "A unit");
      if (unitProblem) return { problem: unitProblem };
    }
    const value = parseValue(row.value, row.decimals, name, row.decimals === 0 ? "104" : "1.02");
    if ("problem" in value) return { problem: named(name, value.problem) };
    const ends: (number | null)[] = [];
    for (const [text, what] of [
      [row.low, "The normal range's low end"],
      [row.high, "The normal range's high end"],
    ] as const) {
      if (text.trim() === "") {
        ends.push(null);
        continue;
      }
      const end = parseValue(text, row.decimals, what);
      if ("problem" in end) return { problem: named(name, end.problem) };
      ends.push(end.value);
    }
    const [low, high] = ends;
    const upsideDown = rangeProblem(low, high, row.decimals);
    if (upsideDown) return { problem: named(name, upsideDown) };
    const key = name.toLowerCase();
    if (seen.has(key)) return { problem: `${name} is given twice: a visit has one result for each test.` };
    seen.add(key);
    results.push({
      metric: row.metricId ?? name,
      value: value.value,
      ...(low == null ? {} : { low }),
      ...(high == null ? {} : { high }),
      ...(row.added ? { unit: row.unit.trim() } : {}),
      ...(row.readingId ? { reading: row.readingId } : {}),
    });
  }
  if (results.length > MAX_RESULTS) return { problem: `A visit takes up to ${MAX_RESULTS} results; that's ${results.length}.` };
  return { results };
}

/** A visit's lab and note checked as the kernel checks them; null when both are fine. */
export function visitProblem(lab: string, note: string): string | null {
  return lineProblem(lab, LAB_MAX, "A lab's name") ?? lineProblem(note, NOTE_MAX, "A visit's note");
}

/** "2.4 MB". */
function megabytes(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** A report file checked before it's sent; the kernel checks its bytes again. Null when it's fine. */
export function reportProblem(file: Pick<File, "size" | "type">): string | null {
  if (file.size === 0) return "That report is empty.";
  if (file.size > REPORT_MAX_BYTES) {
    return `That report is ${megabytes(file.size)}; a report can be at most ${REPORT_MAX_BYTES / (1024 * 1024)} MB.`;
  }
  if (file.type && !(REPORT_TYPES as readonly string[]).includes(file.type)) {
    return "That isn't a PDF or a photo: send the report as a PDF, or a JPEG, PNG or WebP photo of it.";
  }
  return null;
}

// ---------------------------------------------------------------------------
// The live API's shapes: numbers may arrive as strings (`number | string`)
// ---------------------------------------------------------------------------

function report(raw: LabReportRow | null | undefined): LabReportRow | null {
  if (!raw) return null;
  return {
    id: raw.id,
    filename: raw.filename ?? null,
    contentType: raw.contentType ?? "application/octet-stream",
    byteSize: Number(raw.byteSize) || 0,
  };
}

export function normalizeLabVisit(raw: LabVisitRow): LabVisitRow {
  return {
    id: raw.id,
    lab: raw.lab ?? null,
    note: raw.note ?? null,
    collectedAt: raw.collectedAt,
    collectedOn: raw.collectedOn,
    collectedLocal: raw.collectedLocal,
    report: report(raw.report),
    results: (raw.results ?? []).map(normalizeReading),
    outside: Number(raw.outside) || 0,
    loggedAt: raw.loggedAt,
    versions: Number(raw.versions) || 0,
  };
}

export function normalizeLabVisits(raw: LabVisitsRead): LabVisitsRead {
  return { today: raw.today, visits: (raw.visits ?? []).map(normalizeLabVisit) };
}

export function normalizeLabVisitResult(raw: LabVisitResult): LabVisitResult {
  return { visit: raw.visit ? normalizeLabVisit(raw.visit) : null };
}
