import { describe, expect, it } from "vitest";
import { addDays } from "@/lib/dates";
import { dayValues, goal, roundAway, trend, type DayValue } from "@/lib/mock/metric-trend";

// The same cases as the kernel's MetricTrendTests, so the mock store and the API agree.
const START = "2026-11-01";
let n = 0;
const day = (offset: number, value: number): DayValue => ({
  day: addDays(START, offset),
  value,
  readingId: `r${String(n++).padStart(3, "0")}`,
});

describe("metric trend (measurements plan §2.2–§2.3)", () => {
  it("takes a day's value from its first reading by time", () => {
    const evening = { id: "b", value: 193.2, takenAt: `${START}T19:00:00Z`, takenOn: START };
    const morning = { id: "c", value: 192.4, takenAt: `${START}T07:00:00Z`, takenOn: START };
    const next = { id: "a", value: 192.0, takenAt: `${addDays(START, 1)}T06:00:00Z`, takenOn: addDays(START, 1) };

    expect(dayValues([evening, next, morning])).toEqual([
      { day: START, value: 192.4, readingId: "c" },
      { day: addDays(START, 1), value: 192.0, readingId: "a" },
    ]);
  });

  it("pulls a fifth of the way a day, and further after a gap", () => {
    const points = trend([day(0, 200), day(1, 199), day(3, 198)], 1);
    // 200; 200 + 0.2 × (199 − 200) = 199.8; a 2-day gap pulls 1 − 0.8² = 0.36 of the way.
    expect(points.map((p) => p.trend)).toEqual([200, 199.8, 199.15]);
    expect(points.map((p) => p.value)).toEqual([200, 199, 198]);
  });

  it("gives the date a steady loss reaches the target", () => {
    const days = Array.from({ length: 14 }, (_, i) => day(i, 200 - 0.2 * i));
    const g = goal(days, 185, "lower", 1);
    expect(g).toMatchObject({
      status: "heading",
      now: 198.16,
      ratePerWeek: -1.4,
      reachedBy: addDays(START, 13 + 66),
      basisDays: 14,
      basisFrom: START,
      basisTo: addDays(START, 13),
      direction: "lower",
    });
  });

  it("reads the rate over the newest 28 days only", () => {
    const g = goal([day(0, 210), day(30, 200), day(31, 200), day(32, 200), day(33, 200)], 185, "lower", 1);
    expect(g).toMatchObject({ status: "not_heading", ratePerWeek: 0, basisDays: 4, basisFrom: addDays(START, 30) });
    expect(g.reachedBy).toBeNull();
  });

  it("falls back to the newest two days with one day in the last 28", () => {
    const g = goal([day(0, 200), day(40, 196)], 185, "lower", 1);
    expect(g).toMatchObject({
      status: "heading",
      ratePerWeek: -0.7,
      basisDays: 2,
      basisFrom: START,
      basisTo: addDays(START, 40),
    });
  });

  it("needs two days for a date; one is too few, and no target says so", () => {
    expect(goal([day(0, 195)], 185, "lower", 1)).toMatchObject({ status: "too_few", now: 195, basisDays: 1, reachedBy: null });
    expect(goal([day(0, 195), day(1, 194)], 185, "lower", 1).reachedBy).not.toBeNull();
    expect(goal([day(0, 195), day(1, 194)], null, "lower", 1).status).toBe("no_target");
  });

  it("reads at or past the target as reached, and a rise as not heading", () => {
    // The trend: 186, 185.8, 185.44, 184.952: under 185.
    expect(goal([day(0, 186), day(1, 185), day(2, 184), day(3, 183)], 185, "lower", 1).status).toBe("reached");
    expect(goal([day(0, 190), day(1, 191)], 185, "lower", 1)).toMatchObject({ status: "not_heading", ratePerWeek: 7 });
    expect(goal([day(0, 40), day(7, 41)], 45, "higher", 1).status).toBe("heading");
  });

  it("reads a target more than two years away as far", () => {
    const g = goal([day(0, 200.0), day(27, 199.9)], 185, "lower", 1);
    expect(g).toMatchObject({ status: "far", reachedBy: null, ratePerWeek: -0.03 });
  });

  it("takes the side the trend is on without a direction", () => {
    expect(goal([day(0, 200), day(1, 199)], 185, null, 1)).toMatchObject({ status: "heading", direction: "lower" });
  });

  it("rounds half away from zero, as the kernel's decimals do", () => {
    expect(roundAway(1.005, 2)).toBe(1.01);
    expect(roundAway(-1.005, 2)).toBe(-1.01);
    expect(roundAway(198.1560195, 2)).toBe(198.16);
    expect(roundAway(2.5, 0)).toBe(3);
  });
});
