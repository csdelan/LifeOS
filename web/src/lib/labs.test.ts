import { describe, expect, it } from "vitest";
import {
  addedRow,
  checkRows,
  collectedInstant,
  initialPanels,
  labsUsed,
  lineProblem,
  normalizeLabVisit,
  reportOrder,
  reportProblem,
  resultRange,
  resultsByPanel,
  testsByPanel,
  visitMessage,
  visitProblem,
  visitRows,
  visitSummary,
  visitTitle,
  type VisitTestRow,
} from "@/lib/labs";
import { METRIC_CATALOG } from "@/lib/mock/metric-catalog";
import type { LabPanel, LabVisitRow, MetricReadingRow, MetricRow } from "@/lib/production-ui-types";

function test(name: string, panel: LabPanel | null, catalog: string | null, more: Partial<MetricRow> = {}): MetricRow {
  return {
    id: `id-${name}`,
    urn: `urn:bsk:metric:${name}`,
    name,
    unit: "mg/dL",
    decimals: 0,
    target: null,
    better: null,
    catalog,
    archivedAt: null,
    archivedOn: null,
    low: null,
    high: null,
    panel,
    readings: 0,
    lastReading: null,
    ...more,
  };
}

function result(metric: MetricRow, value: number, low: number | null, high: number | null): MetricReadingRow {
  return {
    id: `r-${metric.name}`,
    metricId: metric.id,
    value,
    unit: metric.unit,
    note: null,
    takenAt: "2026-07-11T15:15:00Z",
    takenOn: "2026-07-11",
    takenLocal: "2026-07-11T08:15:00",
    loggedAt: "2026-07-11T15:15:00Z",
    versions: 1,
    flag: null,
    visitId: "v1",
    low,
    high,
  };
}

/** A sheet row as the user left it. */
function row(name: string, value: string, more: Partial<VisitTestRow> = {}): VisitTestRow {
  return {
    key: name.toLowerCase(),
    panel: "cmp",
    name,
    unit: "mg/dL",
    decimals: 0,
    metricId: null,
    readingId: null,
    value,
    low: "",
    high: "",
    added: false,
    ...more,
  };
}

const glucose = test("Glucose", "cmp", "glucose", { low: 65, high: 99 });
const bun = test("BUN", "cmp", "bun");
const ldl = test("LDL", "lipids", "ldl");
const tsh = test("TSH", "other", null, { unit: "mIU/L", decimals: 2 });
const ferritin = test("Ferritin", "other", null, { unit: "ng/mL" });
const weight = test("Weight", null, "weight", { unit: "lb", decimals: 1 });

describe("lab visits in words (measurements plan §4.9)", () => {
  it("names a visit by its day and lab, and counts its results and those outside their range", () => {
    expect(visitTitle({ collectedOn: "2026-11-09", lab: "Quest Diagnostics" })).toBe("Nov 9, 2026 · Quest Diagnostics");
    expect(visitTitle({ collectedOn: "2026-11-09", lab: null })).toBe("Nov 9, 2026");

    const results = (n: number) => Array.from({ length: n }, () => result(glucose, 90, 65, 99));
    expect(visitSummary({ results: results(21), outside: 3 })).toBe("21 results, 3 outside the range");
    expect(visitSummary({ results: results(1), outside: 0 })).toBe("1 result, none outside the range");
    expect(visitSummary({ results: [], outside: 0 })).toBe("No results yet");

    expect(visitMessage("saved", { collectedOn: "2026-11-09", results: results(21) }, "2026-11-10")).toBe(
      "Saved 21 results from Nov 9",
    );
    expect(visitMessage("saved", { collectedOn: "2026-11-09", results: [] }, "2026-11-10")).toBe(
      "Saved the lab visit from Nov 9",
    );
    // A visit from another year says which.
    expect(visitMessage("saved", { collectedOn: "2025-12-30", results: results(1) }, "2026-01-02")).toBe(
      "Saved 1 result from Dec 30, 2025",
    );
    expect(visitMessage("deleted", null, "2026-11-10")).toBe("Deleted the lab visit");
  });

  it("gives a result's printed range in words, and suggests each lab once, newest first", () => {
    expect(resultRange({ low: 65, high: 99, unit: "mg/dL" }, 0)).toBe("65–99 mg/dL");
    expect(resultRange({ low: null, high: 99, unit: "mg/dL" }, 0)).toBe("up to 99 mg/dL");
    expect(resultRange({ low: 0.6, high: 1.29, unit: "mg/dL" }, 2)).toBe("0.60–1.29 mg/dL");
    expect(resultRange({ low: null, high: null, unit: "mg/dL" }, 0)).toBeNull();

    expect(labsUsed([{ lab: "LabCorp" }, { lab: null }, { lab: "Quest Diagnostics" }, { lab: "labcorp" }])).toEqual([
      "LabCorp",
      "Quest Diagnostics",
    ]);
  });
});

describe("when a visit's sample was collected", () => {
  // Nov 9, 2026, 9:30 AM local: today, before noon.
  const morning = new Date(2026, 10, 9, 9, 30).getTime();

  it("is the time given, else noon, or now for today before noon", () => {
    expect(collectedInstant("2026-11-08", "08:15", morning)).toBe(new Date(2026, 10, 8, 8, 15).toISOString());
    expect(collectedInstant("2026-11-08", "", morning)).toBe(new Date(2026, 10, 8, 12, 0).toISOString());
    expect(collectedInstant("2026-11-09", "", morning)).toBe(new Date(morning).toISOString());
    expect(collectedInstant("2026-11-09", "", new Date(2026, 10, 9, 15, 0).getTime())).toBe(
      new Date(2026, 10, 9, 12, 0).toISOString(),
    );
  });

  it("is nothing while the day or time is incomplete", () => {
    expect(collectedInstant("", "", morning)).toBeNull();
    expect(collectedInstant("2026-11-08", "8", morning)).toBeNull();
  });
});

describe("lab tests by panel (measurements plan §4.9)", () => {
  it("lists the tests under their panels in the report's order, his own by name, archived ones and metrics left out", () => {
    const archived = test("Sodium", "cmp", "sodium", { archivedAt: "2026-10-01T00:00:00Z", archivedOn: "2026-10-01" });
    const panels = testsByPanel([tsh, ldl, weight, bun, ferritin, archived, glucose], METRIC_CATALOG);
    expect(panels.map((p) => [p.panel.name, p.groups.map((g) => g.name)])).toEqual([
      ["CMP", ["Glucose", "BUN"]],
      ["Lipid panel", ["LDL"]],
      ["Other tests", ["Ferritin", "TSH"]],
    ]);
    expect(panels[0].groups[0].noun).toBe("result");
    expect([tsh, glucose, ldl].sort(reportOrder(METRIC_CATALOG)).map((m) => m.name)).toEqual(["Glucose", "LDL", "TSH"]);
  });

  it("lays out a visit's results as its report does, by panel", () => {
    const visit = { results: [result(tsh, 2.15, 0.45, 4.5), result(ldl, 131, null, 99), result(glucose, 104, 65, 99)] };
    const panels = resultsByPanel(visit, [glucose, ldl, tsh], METRIC_CATALOG);
    expect(panels.map((p) => [p.panel.name, p.results.map((r) => r.metric.name)])).toEqual([
      ["CMP", ["Glucose"]],
      ["Lipid panel", ["LDL"]],
      ["Other tests", ["TSH"]],
    ]);
  });
});

describe("the lab visit sheet's rows (measurements plan §4.9)", () => {
  it("starts a first visit with the starter list's three panels and their common adult ranges", () => {
    const rows = visitRows([], null, METRIC_CATALOG);
    expect(rows).toHaveLength(21);
    expect(rows[0]).toMatchObject({ key: "glucose", name: "Glucose", unit: "mg/dL", metricId: null, low: "70", high: "100" });
    expect(rows.find((r) => r.name === "Creatinine")).toMatchObject({ low: "0.60", high: "1.30", decimals: 2 });
    expect(rows.at(-1)).toMatchObject({ name: "A1c", panel: "a1c", low: "", high: "5.6" });
    expect([...initialPanels(rows, false, null, [])]).toEqual(["cmp", "lipids", "a1c"]);
  });

  it("fills a tracked test with its newest report's range, adds his own, and drops one he stopped tracking", () => {
    const stopped = test("BUN", "cmp", "bun", { archivedAt: "2026-10-01T00:00:00Z", archivedOn: "2026-10-01" });
    const rows = visitRows([glucose, stopped, tsh], null, METRIC_CATALOG);
    expect(rows[0]).toMatchObject({ key: glucose.id, metricId: glucose.id, low: "65", high: "99", value: "" });
    expect(rows.some((r) => r.name === "BUN")).toBe(false);
    expect(rows.at(-1)).toMatchObject({ name: "TSH", panel: "other", metricId: tsh.id, unit: "mIU/L" });
  });

  it("fills an edit from the visit's own results and ranges, opening the panels it has results on", () => {
    // The test's range has moved on since; the visit keeps the one printed beside its result.
    const now = { ...glucose, low: 70, high: 110 };
    const visit = { results: [result(now, 104, 65, 99), result(tsh, 2.15, 0.45, 4.5)] };
    const rows = visitRows([now, tsh], visit, METRIC_CATALOG);
    expect(rows[0]).toMatchObject({ metricId: now.id, readingId: "r-Glucose", value: "104", low: "65", high: "99" });
    expect(rows.find((r) => r.name === "TSH")).toMatchObject({ value: "2.15", low: "0.45", high: "4.50" });
    expect([...initialPanels(rows, true, null, [now, tsh])].sort()).toEqual(["cmp", "other"]);

    // A new visit opens the panels the newest one had.
    expect([...initialPanels(visitRows([now, tsh], null, METRIC_CATALOG), false, visit, [now, tsh])].sort()).toEqual(["cmp", "other"]);
  });
});

describe("checking the visit sheet as the kernel does", () => {
  it("sends the results typed, by test, and leaves out a blank one", () => {
    const checked = checkRows([
      row("Glucose", "101", { low: "70", high: "100" }),
      row("BUN", ""),
      row("LDL", "131", { metricId: ldl.id, readingId: "r-LDL", high: "99", panel: "lipids" }),
      { ...addedRow(1), name: "TSH", unit: " mIU/L ", value: "2.15", low: "0.45", high: "4.5" },
      addedRow(2),
    ]);
    expect(checked).toEqual({
      results: [
        { metric: "Glucose", value: 101, low: 70, high: 100 },
        { metric: ldl.id, value: 131, high: 99, reading: "r-LDL" },
        { metric: "TSH", value: 2.15, low: 0.45, high: 4.5, unit: "mIU/L" },
      ],
    });
    expect(checkRows([row("Glucose", ""), addedRow(1)])).toEqual({ results: [] });
  });

  it("refuses a bad result in the kernel's words, naming the test", () => {
    const problem = (rows: VisitTestRow[]) => {
      const checked = checkRows(rows);
      return "problem" in checked ? checked.problem : null;
    };
    expect(problem([row("Glucose", "abc")])).toBe("Glucose: 'abc' isn't a number.");
    expect(problem([row("Glucose", "101.5")])).toBe("Glucose takes a whole number; got 101.5.");
    expect(problem([row("Glucose", "101", { low: "100", high: "70" })])).toBe(
      "Glucose: The normal range's low end (100) is above its high end (70).",
    );
    expect(problem([{ ...addedRow(1), value: "2.15" }])).toBe("Give the test a name, e.g. TSH.");
    expect(problem([{ ...addedRow(1), name: "TSH", value: "2.15" }])).toBe(
      "Give TSH's unit, as the report prints it (e.g. mIU/L).",
    );
    expect(problem([row("Glucose", "101"), { ...addedRow(1), name: "glucose", unit: "mg/dL", value: "99" }])).toBe(
      "glucose is given twice: a visit has one result for each test.",
    );
  });

  it("checks a visit's lab, note and report before anything is sent", () => {
    expect(visitProblem("Quest Diagnostics", "Fasting")).toBeNull();
    expect(visitProblem("Quest\nDiagnostics", "")).toBe("A lab's name is one line: take out the line break.");
    expect(visitProblem("x".repeat(61), "")).toBe("A lab's name is up to 60 characters; that one is 61.");
    expect(lineProblem("  fine  ", 4, "A note")).toBeNull();

    expect(reportProblem({ size: 2048, type: "application/pdf" })).toBeNull();
    expect(reportProblem({ size: 2048, type: "" })).toBeNull();
    expect(reportProblem({ size: 0, type: "application/pdf" })).toBe("That report is empty.");
    expect(reportProblem({ size: 11 * 1024 * 1024, type: "application/pdf" })).toBe(
      "That report is 11.0 MB; a report can be at most 10 MB.",
    );
    expect(reportProblem({ size: 2048, type: "text/plain" })).toBe(
      "That isn't a PDF or a photo: send the report as a PDF, or a JPEG, PNG or WebP photo of it.",
    );
  });
});

describe("the live API's lab visits", () => {
  it("reads numbers sent as strings as numbers", () => {
    const raw = {
      id: "v1",
      lab: "Quest Diagnostics",
      note: null,
      collectedAt: "2026-07-11T15:15:00Z",
      collectedOn: "2026-07-11",
      collectedLocal: "2026-07-11T08:15:00",
      report: { id: "a1", filename: "results.pdf", contentType: "application/pdf", byteSize: "2048" },
      results: [{ ...result(glucose, 104, 65, 99), value: "104", low: "65", high: "99", flag: "high" }],
      outside: "1",
      loggedAt: "2026-07-12T00:00:00Z",
      versions: "2",
    } as unknown as LabVisitRow;
    const visit = normalizeLabVisit(raw);
    expect(visit).toMatchObject({ outside: 1, versions: 2, report: { byteSize: 2048 } });
    expect(visit.results[0]).toMatchObject({ value: 104, low: 65, high: 99, flag: "high", visitId: "v1" });
  });
});
