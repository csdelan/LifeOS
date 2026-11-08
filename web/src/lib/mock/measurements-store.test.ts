import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import {
  clearMockMeasurements,
  mockAddMetric,
  mockArchiveMetric,
  mockDeleteReading,
  mockEditReading,
  mockLogReading,
  mockLogReadings,
  mockMetricDetail,
  mockMetrics,
  mockSetMetric,
  resetMeasurementsStore,
} from "@/lib/mock/measurements-store";

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
