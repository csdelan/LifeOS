import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import {
  clearMockMeasurements,
  mockAddMetric,
  mockArchiveMetric,
  mockAttachLabReport,
  mockDeleteLabVisit,
  mockDeleteReading,
  mockEditReading,
  mockLabReportUrl,
  mockLabVisits,
  mockLogLabVisit,
  mockLogReading,
  mockLogReadings,
  mockMetricDetail,
  mockMetrics,
  mockRemoveLabReport,
  mockSaveLabVisit,
  mockSetMetric,
  resetMeasurementsStore,
} from "@/lib/mock/measurements-store";
import type { LabResultRequest, MetricReadingRow } from "@/lib/production-ui-types";

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 7, 12, 0, 0));
  resetMeasurementsStore();
});

afterEach(() => {
  vi.useRealTimers();
});

/** A local wall-clock time today, `days` ago, as an instant. */
function localAt(daysAgo: number, hour: number, minute = 0): string {
  const [y, m, d] = addDays(todayIso(), -daysAgo).split("-").map(Number);
  return new Date(y, m - 1, d, hour, minute).toISOString();
}

describe("the mock store's measurements (measurements plan §4.7–§4.8)", () => {
  it("seeds Weight with a 185 goal and weeks of morning weigh-ins, the newest yesterday", () => {
    const weight = mockMetrics().find((m) => m.catalog === "weight")!;
    expect(weight).toMatchObject({ name: "Weight", unit: "lb", decimals: 1, target: 185, better: "lower", catalog: "weight" });
    expect(weight.readings).toBeGreaterThan(30);
    expect(weight.lastReading?.takenOn).toBe(addDays(todayIso(), -1));

    const all = mockMetricDetail("weight");
    expect(all.from).toBeNull();
    // One evening weigh-in sits on a morning one: a reading, but not the day's number.
    expect(all.readings.length).toBe(all.days.length + 1);
    expect(all.goal).toMatchObject({ status: "heading", target: 185, direction: "lower" });
    expect(all.goal.reachedBy! > todayIso()).toBe(true);
    expect(all.goal.basis.days).toBeGreaterThan(20);

    const month = mockMetricDetail(weight.id, 30);
    expect(month.from).toBe(addDays(todayIso(), -29));
    expect(month.days.every((d) => d.day >= month.from!)).toBe(true);
    // The trend carries in from before the range, so the first day's trend isn't its value.
    expect(month.days[0].trend).not.toBe(month.days[0].value);
    expect(month.goal).toEqual(all.goal);
  });

  it("logs, backdates, changes and deletes readings, refused in the kernel's words", () => {
    const logged = mockLogReading({ metric: "Weight", value: 192.4, note: "  after a run " }).reading!;
    expect(logged).toMatchObject({ value: 192.4, unit: "lb", note: "after a run", takenOn: todayIso(), versions: 1 });

    const backdated = mockLogReading({ metric: "weight", value: 194, takenAt: localAt(9, 7, 10) }).reading!;
    expect(backdated).toMatchObject({ takenOn: addDays(todayIso(), -9), takenLocal: `${addDays(todayIso(), -9)}T07:10:00` });

    expect(() => mockLogReading({ metric: "Weight", value: 190, takenAt: localAt(-1, 8) })).toThrow(
      "A reading is taken now or earlier, not in the future.",
    );
    expect(() => mockLogReading({ metric: "Weight", value: 190.25 })).toThrow(
      "Weight takes at most 1 decimal place; got 190.25.",
    );
    expect(() => mockLogReading({ metric: "Neck", value: 15 })).toThrow("No subject matches 'Neck'.");

    const edited = mockEditReading(backdated.id, { value: 193.8, note: "scale on carpet" }).reading!;
    expect(edited).toMatchObject({ value: 193.8, note: "scale on carpet", versions: 2, takenAt: backdated.takenAt });
    // Changing nothing writes nothing.
    expect(mockEditReading(backdated.id, { value: 193.8, takenAt: backdated.takenAt }).reading!.versions).toBe(2);
    const moved = mockEditReading(backdated.id, { takenAt: localAt(10, 6, 50), note: "" }).reading!;
    expect(moved).toMatchObject({ takenOn: addDays(todayIso(), -10), note: null, versions: 3 });

    expect(mockDeleteReading(moved.id)).toEqual({ reading: null });
    expect(mockMetricDetail("Weight").readings.some((r) => r.id === moved.id)).toBe(false);
    expect(() => mockEditReading(moved.id, { value: 190 })).toThrow(`No reading has the id ${moved.id} (it may have been deleted).`);
  });

  it("starts tracking weight from the starter list and keeps the kernel's rules for metrics", () => {
    clearMockMeasurements();
    expect(mockMetrics()).toEqual([]);

    const weight = mockAddMetric({ name: " weight ", target: 185 });
    expect(weight).toMatchObject({ name: "weight", unit: "lb", decimals: 1, better: "lower", catalog: "weight", readings: 0 });
    expect(mockMetricDetail("Weight").goal.status).toBe("too_few");
    expect(() => mockAddMetric({ name: "Weight" })).toThrow("You already have a metric called 'weight'.");
    expect(() => mockAddMetric({ name: "Neck" })).toThrow("Give the metric a unit, e.g. lb, in, % or mg/dL.");
    expect(() => mockAddMetric({ name: "Body fat", unit: "%", target: 18.25 })).toThrow(
      "The target takes at most 1 decimal place; got 18.25.",
    );

    expect(mockSetMetric("Weight", { target: 180 }).target).toBe(180);
    expect(mockSetMetric(weight.id, { clearTarget: true }).target).toBeNull();
    expect(() => mockSetMetric("Weight", {})).toThrow("Say what to change");
    expect(() => mockSetMetric("Weight", { target: 180, clearTarget: true })).toThrow("Set a target or clear it, not both.");

    mockLogReading({ metric: "Weight", value: 192.4 });
    expect(mockMetricDetail("Weight").goal.status).toBe("no_target");
    expect(() => mockSetMetric("Weight", { unit: "kg" })).toThrow(
      "weight has 1 reading in lb, so its unit stays: make a new metric for kg.",
    );

    expect(mockArchiveMetric(weight.urn, true)).toBe(true);
    expect(() => mockLogReading({ metric: "Weight", value: 191 })).toThrow(
      "'weight' is archived: bring it back before logging to it.",
    );
    expect(mockArchiveMetric("urn:bsk:task:not-a-metric", true)).toBe(false);
  });

  it("seeds blood pressure as a pair with its normal ranges, the newest high, and Waist with none", () => {
    const metrics = mockMetrics();
    const systolic = metrics.find((m) => m.catalog === "bp_systolic")!;
    const diastolic = metrics.find((m) => m.catalog === "bp_diastolic")!;
    expect(systolic).toMatchObject({ name: "Systolic BP", unit: "mmHg", decimals: 0, low: 90, high: 119, target: null });
    expect(diastolic).toMatchObject({ name: "Diastolic BP", low: 60, high: 79 });
    // Logged together, so each pair shares its instant; the newest is two days ago.
    expect(systolic.lastReading).toMatchObject({ value: 121, flag: "high", takenOn: addDays(todayIso(), -2) });
    expect(diastolic.lastReading).toMatchObject({ value: 78, flag: null, takenAt: systolic.lastReading!.takenAt });

    const all = mockMetricDetail("Systolic BP");
    expect(all.readings.filter((r) => r.flag === "high").length).toBeGreaterThan(1);
    expect(all.readings.every((r) => r.flag === "high" || r.flag === null)).toBe(true);

    const waist = metrics.find((m) => m.catalog === "waist")!;
    expect(waist).toMatchObject({ unit: "in", low: null, high: null, target: null });
    expect(waist.lastReading?.flag).toBeNull();
    // Body fat and VO2 max (lab) are Chris's to add.
    expect(metrics.some((m) => m.catalog === "body_fat" || m.catalog === "vo2max_lab")).toBe(false);
  });

  it("keeps a normal range, refused in the kernel's words, and flags against it as it is now", () => {
    clearMockMeasurements();
    // A starter name fills in its range; his own has none until he gives one.
    expect(mockAddMetric({ name: "systolic bp" })).toMatchObject({ unit: "mmHg", decimals: 0, low: 90, high: 119 });
    expect(mockAddMetric({ name: "Neck", unit: "in", high: 16 })).toMatchObject({ low: null, high: 16 });
    expect(() => mockAddMetric({ name: "Ferritin", unit: "ng/mL", decimals: 0, low: 300, high: 30 })).toThrow(
      "The normal range's low end (300) is above its high end (30).",
    );
    expect(() => mockAddMetric({ name: "Ferritin", unit: "ng/mL", decimals: 0, low: 30.5 })).toThrow(
      "The normal range's low end takes a whole number; got 30.5.",
    );

    const high = mockLogReading({ metric: "Neck", value: 16.5 }).reading!;
    expect(high.flag).toBe("high");
    // The end itself is normal.
    expect(mockLogReading({ metric: "Neck", value: 16, takenAt: localAt(1, 7) }).reading!.flag).toBeNull();

    // Raising the high end re-flags the old reading; clearing it leaves none flagged.
    expect(mockSetMetric("Neck", { high: 17 })).toMatchObject({ high: 17 });
    expect(mockMetricDetail("Neck").readings.find((r) => r.id === high.id)!.flag).toBeNull();
    expect(mockSetMetric("Neck", { low: 16.2 }).lastReading?.flag).toBeNull();
    expect(mockMetricDetail("Neck").readings.map((r) => r.flag)).toEqual([null, "low"]);
    expect(() => mockSetMetric("Neck", { low: 18 })).toThrow("The normal range's low end (18.0) is above its high end (17.0).");
    expect(() => mockSetMetric("Neck", { high: 17, clearHigh: true })).toThrow(
      "Set an end of the normal range or clear it, not both.",
    );
    expect(mockSetMetric("Neck", { clearLow: true, clearHigh: true })).toMatchObject({ low: null, high: null });
    expect(() => mockSetMetric("Neck", {})).toThrow(
      "Say what to change: a name, a unit, decimals, a target, which way is better or the normal range.",
    );
  });

  it("logs readings together at one time with one note, all or none", () => {
    const before = mockMetricDetail("Systolic BP").readings.length;
    const { readings } = mockLogReadings({
      readings: [
        { metric: "Systolic BP", value: 126 },
        { metric: "diastolic bp", value: 77 },
      ],
      takenAt: localAt(0, 7, 30),
      note: "after coffee",
    });
    expect(readings.map((r) => [r.value, r.flag, r.note])).toEqual([
      [126, "high", "after coffee"],
      [77, null, "after coffee"],
    ]);
    expect(readings[0].takenAt).toBe(readings[1].takenAt);

    // Any refusal writes none.
    const refusals: [Parameters<typeof mockLogReadings>[0], string][] = [
      [{ readings: [] }, "Give at least one reading: a metric and its value."],
      [
        { readings: [{ metric: "Systolic BP", value: 120 }, { metric: "Systolic BP", value: 121 }] },
        "Systolic BP is given twice: log one value for each metric.",
      ],
      [
        { readings: [{ metric: "Systolic BP", value: 120 }, { metric: "Diastolic BP", value: 78.5 }] },
        "Diastolic BP takes a whole number; got 78.5.",
      ],
      [{ readings: [{ metric: "Systolic BP", value: 120 }, { metric: "Pulse", value: 60 }] }, "No subject matches 'Pulse'."],
    ];
    for (const [req, message] of refusals) expect(() => mockLogReadings(req)).toThrow(message);
    expect(mockMetricDetail("Systolic BP").readings.length).toBe(before + 1);

    const waist = mockMetrics().find((m) => m.catalog === "waist")!;
    mockArchiveMetric(waist.urn, true);
    expect(() =>
      mockLogReadings({ readings: [{ metric: "Systolic BP", value: 120 }, { metric: waist.id, value: 37.5 }] }),
    ).toThrow("'Waist' is archived: bring it back before logging to it.");
    expect(mockMetricDetail("Systolic BP").readings.length).toBe(before + 1);
  });
});

/** A visit's result sent back as it is, so an edit keeps it. */
function kept(r: MetricReadingRow): LabResultRequest {
  return {
    metric: r.metricId,
    value: r.value,
    ...(r.low == null ? {} : { low: r.low }),
    ...(r.high == null ? {} : { high: r.high }),
    reading: r.id,
  };
}

/** A test's name, by its id. */
function testName(metricId: string): string {
  return mockMetrics().find((m) => m.id === metricId)!.name;
}

describe("the mock store's lab visits (measurements plan §4.9)", () => {
  it("seeds a visit whose tests take its report's ranges, each result flagged by the range printed beside it", () => {
    const { visits } = mockLabVisits();
    expect(visits).toHaveLength(1);
    const [visit] = visits;
    expect(visit).toMatchObject({
      lab: "Quest Diagnostics",
      note: "Fasting",
      collectedOn: addDays(todayIso(), -120),
      outside: 3,
      report: { filename: "quest-results.pdf", contentType: "application/pdf" },
    });
    expect(visit.results).toHaveLength(21);
    expect(visit.results.filter((r) => r.flag).map((r) => [testName(r.metricId), r.flag])).toEqual([
      ["Glucose", "high"],
      ["LDL", "high"],
      ["Non-HDL cholesterol", "high"],
    ]);
    expect(mockLabReportUrl(visit.report!.id)).toMatch(/^data:application\/pdf/);
    // The report's range, a little off the starter list's 70–100.
    expect(mockMetrics().find((m) => m.catalog === "glucose")).toMatchObject({ panel: "cmp", low: 65, high: 99 });
    // Lab tests are metrics, but the Add a metric list leaves them to the visit.
    expect(mockMetrics().filter((m) => m.panel != null)).toHaveLength(21);
  });

  it("logs a visit by test name, makes a test it doesn't know, and moves each test's range to the newest report", () => {
    const logged = mockLogLabVisit({
      collectedAt: localAt(0, 8, 5),
      lab: " LabCorp ",
      results: [
        { metric: "glucose", value: 104, low: 70, high: 110 },
        { metric: "TSH", value: 2.15, low: 0.45, high: 4.5, unit: "mIU/L" },
      ],
    }).visit!;
    expect(logged).toMatchObject({ lab: "LabCorp", note: null, collectedOn: todayIso(), outside: 0, versions: 1, report: null });
    expect(logged.results.map((r) => [testName(r.metricId), r.value, r.flag])).toEqual([
      ["Glucose", 104, null],
      ["TSH", 2.15, null],
    ]);
    expect(mockMetrics().find((m) => m.name === "TSH")).toMatchObject({
      panel: "other",
      unit: "mIU/L",
      decimals: 2,
      low: 0.45,
      high: 4.5,
      catalog: null,
    });
    expect(mockMetrics().find((m) => m.catalog === "glucose")).toMatchObject({ low: 70, high: 110 });
    // The July 104 keeps the range printed beside it, so it's still high though it's inside today's.
    const glucose = mockMetricDetail("Glucose").readings;
    expect(glucose.find((r) => r.visitId === logged.id)).toMatchObject({ low: 70, high: 110, flag: null });
    expect(glucose.find((r) => r.visitId !== logged.id)).toMatchObject({ value: 104, low: 65, high: 99, flag: "high" });

    // A visit can be logged before its results are back; the newest collected comes first.
    const waiting = mockLogLabVisit({ collectedAt: localAt(1, 7, 30), results: [] }).visit!;
    expect(waiting).toMatchObject({ results: [], outside: 0, lab: null });
    expect(mockLabVisits().visits.map((v) => v.id)).toEqual([logged.id, waiting.id, expect.any(String)]);
  });

  it("refuses a bad visit in the kernel's words and writes none of it", () => {
    const before = { visits: mockLabVisits().visits.length, metrics: mockMetrics().length };
    expect(() =>
      mockLogLabVisit({ results: [{ metric: "TSH", value: 2.15, unit: "mIU/L" }, { metric: "Ferritin", value: 80 }] }),
    ).toThrow("'Ferritin' isn't a test you track yet: give its unit to add it, as the report prints it (e.g. mIU/L).");
    expect(() => mockLogLabVisit({ collectedAt: localAt(-1, 8), results: [] })).toThrow(
      "A lab visit is collected now or earlier, not in the future.",
    );
    expect(() => mockLogLabVisit({ results: [{ metric: "Glucose", value: 90 }, { metric: "glucose", value: 91 }] })).toThrow(
      "Glucose is given twice: a visit has one result for each test.",
    );
    expect(() => mockLogLabVisit({ results: [{ metric: "Glucose", value: 90.5 }] })).toThrow(
      "Glucose takes a whole number; got 90.5.",
    );
    expect(() => mockLogLabVisit({ results: [{ metric: "Glucose", value: 90, low: 100, high: 70 }] })).toThrow(
      "Glucose: The normal range's low end (100) is above its high end (70).",
    );
    expect(() => mockLogLabVisit({ lab: "Quest\nDiagnostics", results: [] })).toThrow(
      "A lab's name is one line: take out the line break.",
    );
    expect(() => mockSaveLabVisit("zz", { results: [] })).toThrow(
      "'zz' isn't a lab visit's id: give at least its first 4 hex digits.",
    );
    expect({ visits: mockLabVisits().visits.length, metrics: mockMetrics().length }).toEqual(before);
  });

  it("saves an edit: a changed result is a new version, one left out is deleted, and the visit's time moves them all", () => {
    const visit = mockLabVisits().visits[0];
    const glucose = visit.results.find((r) => testName(r.metricId) === "Glucose")!;
    const bun = visit.results.find((r) => testName(r.metricId) === "BUN")!;
    const others = visit.results.filter((r) => r !== glucose && r !== bun);

    const saved = mockSaveLabVisit(visit.id, {
      results: [{ ...kept(glucose), value: 98 }, ...others.map(kept)],
    }).visit!;
    // The lab and note are kept, so the visit itself is unchanged.
    expect(saved).toMatchObject({ lab: "Quest Diagnostics", note: "Fasting", versions: visit.versions, outside: 2 });
    expect(saved.results).toHaveLength(20);
    expect(saved.results.find((r) => r.id === glucose.id)).toMatchObject({ value: 98, flag: null, versions: 2 });
    expect(saved.results.filter((r) => r.id !== glucose.id).every((r) => r.versions === 1)).toBe(true);
    expect(mockMetricDetail("BUN").readings).toHaveLength(0);

    // A result's time is the visit's: it moves with the visit, never on its own.
    expect(() => mockEditReading(glucose.id, { takenAt: localAt(3, 8) })).toThrow(
      "That Glucose result is part of a lab visit: change the visit's time instead, and its results move with it.",
    );
    const moved = mockSaveLabVisit(visit.id, { collectedAt: localAt(121, 7, 45), results: saved.results.map(kept) }).visit!;
    expect(moved).toMatchObject({ collectedOn: addDays(todayIso(), -121), versions: visit.versions + 1 });
    expect(moved.results.every((r) => r.takenLocal === `${addDays(todayIso(), -121)}T07:45:00`)).toBe(true);
  });

  it("deletes a visit with its results, and each test's range goes back to the report before", () => {
    const logged = mockLogLabVisit({ results: [{ metric: "Glucose", value: 92, low: 70, high: 110 }] }).visit!;
    expect(mockMetrics().find((m) => m.catalog === "glucose")).toMatchObject({ low: 70, high: 110 });

    expect(mockDeleteLabVisit(logged.id)).toEqual({ visit: null });
    expect(mockMetrics().find((m) => m.catalog === "glucose")).toMatchObject({ low: 65, high: 99 });
    expect(mockLabVisits().visits).toHaveLength(1);
    expect(mockMetricDetail("Glucose").readings).toHaveLength(1);
    expect(() => mockDeleteLabVisit(logged.id)).toThrow(`No lab visit has the id ${logged.id} (it may have been deleted).`);
  });

  it("keeps a report, a PDF or a photo, in place of any before it, and takes it off", () => {
    const visit = mockLogLabVisit({ results: [] }).visit!;
    const attached = mockAttachLabReport(visit.id, new Blob(["%PDF-1.4"]), "C:\\Users\\chris\\results.PDF").visit!;
    expect(attached.report).toMatchObject({ filename: "results.PDF", contentType: "application/pdf", byteSize: 8 });
    expect(attached.versions).toBe(2);
    expect(mockLabReportUrl(attached.report!.id)).not.toBeNull();

    expect(() => mockAttachLabReport(visit.id, new Blob(["hello"], { type: "text/plain" }), "notes.txt")).toThrow(
      "That isn't a PDF or a photo: send the report as a PDF, or a JPEG, PNG or WebP photo of it.",
    );
    expect(() => mockAttachLabReport(visit.id, new Blob([]), "empty.pdf")).toThrow("That report is empty.");

    const photo = mockAttachLabReport(visit.id, new Blob(["jpeg"], { type: "image/jpeg" }), "page-1.jpg").visit!;
    expect(photo.report).toMatchObject({ filename: "page-1.jpg", contentType: "image/jpeg" });
    expect(photo.report!.id).not.toBe(attached.report!.id);

    expect(mockRemoveLabReport(visit.id).visit).toMatchObject({ report: null, versions: 4 });
    // Taking off a report that isn't there changes nothing.
    expect(mockRemoveLabReport(visit.id).visit).toMatchObject({ report: null, versions: 4 });
  });
});
