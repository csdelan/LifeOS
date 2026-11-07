import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import {
  clearMockMeasurements,
  mockAddMetric,
  mockArchiveMetric,
  mockDeleteReading,
  mockEditReading,
  mockLogReading,
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

describe("the mock store's measurements (measurements plan §4.7)", () => {
  it("seeds Weight with a 185 goal and weeks of morning weigh-ins, the newest yesterday", () => {
    const [weight] = mockMetrics();
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
    expect(() => mockLogReading({ metric: "Waist", value: 34 })).toThrow("No subject matches 'Waist'.");

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
    expect(() => mockAddMetric({ name: "Waist" })).toThrow("Give the metric a unit, e.g. lb, in, % or mg/dL.");
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
});
