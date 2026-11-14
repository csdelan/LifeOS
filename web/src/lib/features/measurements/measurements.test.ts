import { describe, expect, it } from "vitest";
import {
  flagOf,
  flagsText,
  groupMetrics,
  groupRangeText,
  groupValue,
  nameInText,
  parseValue,
  rangeText,
  readingSets,
  starterChoices,
} from "@/lib/features/measurements/measurements";
import { METRIC_CATALOG } from "@/lib/mock/metric-catalog";
import type { MetricReadingRow, MetricRow } from "@/lib/features/measurements/types";

function metric(name: string, catalog: string | null, more: Partial<MetricRow> = {}): MetricRow {
  return {
    id: `id-${name}`,
    urn: `urn:bsk:metric:${name}`,
    name,
    unit: "mmHg",
    decimals: 0,
    target: null,
    better: "lower",
    catalog,
    archivedAt: null,
    archivedOn: null,
    low: null,
    high: null,
    panel: null,
    readings: 0,
    lastReading: null,
    ...more,
  };
}

function reading(id: string, metricId: string, value: number, takenAt: string): MetricReadingRow {
  return {
    id,
    metricId,
    value,
    unit: "mmHg",
    note: null,
    takenAt,
    takenOn: takenAt.slice(0, 10),
    takenLocal: takenAt.slice(0, 19),
    loggedAt: takenAt,
    versions: 1,
    flag: null,
    visitId: null,
    low: null,
    high: null,
  };
}

describe("measurements words and groups (measurements plan §4.8)", () => {
  it("pairs blood pressure into one group in starter-list order, then his own by name", () => {
    const groups = groupMetrics([
      metric("Resting heart rate", null),
      metric("Diastolic BP", "bp_diastolic"),
      metric("Ferritin", null),
      metric("Systolic BP", "bp_systolic"),
      metric("Waist", "waist"),
    ], METRIC_CATALOG);
    expect(groups.map((g) => [g.key, g.name, g.metrics.map((m) => m.name)])).toEqual([
      ["waist", "Waist", ["Waist"]],
      ["bp", "Blood pressure", ["Systolic BP", "Diastolic BP"]],
      ["id-Ferritin", "Ferritin", ["Ferritin"]],
      ["id-Resting heart rate", "Resting heart rate", ["Resting heart rate"]],
    ]);
    // Half a pair is a row of its own.
    expect(groupMetrics([metric("Diastolic BP", "bp_diastolic")], METRIC_CATALOG).map((g) => g.name)).toEqual(["Diastolic BP"]);
  });

  it("reads a pair's values and range together", () => {
    const [bp] = groupMetrics([
      metric("Systolic BP", "bp_systolic", { low: 90, high: 119 }),
      metric("Diastolic BP", "bp_diastolic", { low: 60, high: 79 }),
    ], METRIC_CATALOG);
    expect(groupValue(bp, [122, 78])).toBe("122/78 mmHg");
    expect(groupValue(bp, [122, null])).toBe("122/— mmHg");
    expect(groupRangeText(bp)).toBe("Normal: systolic 90–119, diastolic 60–79 mmHg");
  });

  it("puts readings taken together into one set, newest first", () => {
    const sys = [reading("s2", "sys", 121, "2026-11-05T07:20:00.000Z"), reading("s1", "sys", 118, "2026-11-01T07:20:00.000Z")];
    const dia = [reading("d2", "dia", 78, "2026-11-05T07:20:00.000Z"), reading("d0", "dia", 75, "2026-10-30T07:20:00.000Z")];
    expect(readingSets([sys, dia]).map((set) => set.map((r) => r?.id ?? null))).toEqual([
      ["s2", "d2"],
      ["s1", null],
      [null, "d0"],
    ]);
    // Two readings of one metric at one instant stay apart.
    const twice = [reading("a", "w", 192, "2026-11-05T07:00:00.000Z"), reading("b", "w", 193, "2026-11-05T07:00:00.000Z")];
    expect(readingSets([twice])).toHaveLength(2);
  });

  it("flags past the ends only, and says the range as the kernel does", () => {
    const range = { low: 90, high: 119 };
    expect([flagOf(89, range), flagOf(90, range), flagOf(119, range), flagOf(120, range)]).toEqual(["low", null, null, "high"]);
    expect(flagsText([{ flag: "high" }, { flag: null }])).toBe("High");
    expect(flagsText([{ flag: "high" }, { flag: "low" }])).toBe("High, low");
    expect(flagsText([null, { flag: null }])).toBeNull();
    const inches = { unit: "in", decimals: 1 };
    expect(rangeText({ ...inches, low: null, high: 40 })).toBe("up to 40.0 in");
    expect(rangeText({ ...inches, low: 30, high: null })).toBe("at least 30.0 in");
    expect(rangeText({ ...inches, low: null, high: null })).toBeNull();
  });

  it("keeps a name's capitals inside a sentence only when it starts with an acronym", () => {
    expect(["Weight", "Body fat", "VO2 max (lab)", "LDL", "Systolic BP"].map(nameInText)).toEqual([
      "weight",
      "body fat",
      "VO2 max (lab)",
      "LDL",
      "systolic BP",
    ]);
    expect(parseValue("", 0, "Systolic BP", "118")).toEqual({ problem: "Type the systolic BP, e.g. 118." });
  });

  it("offers the starter metrics not tracked yet, blood pressure while either half is missing", () => {
    const names = (metrics: MetricRow[]) => starterChoices(metrics, METRIC_CATALOG).map((c) => c.name);
    expect(names([])).toEqual(["Body fat", "Waist", "Blood pressure", "VO2 max (lab)"]);
    expect(names([metric("Waist", "waist"), metric("Systolic BP", "bp_systolic")])).toEqual([
      "Body fat",
      "Blood pressure",
      "VO2 max (lab)",
    ]);
    // An archived one can be started again (or restored).
    expect(names([metric("Body fat", "body_fat", { archivedAt: "2026-11-01T00:00:00Z" })])).toContain("Body fat");
  });
});
