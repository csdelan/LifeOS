import { describe, expect, it } from "vitest";
import { targetDirty, targetFields, targetProblem, targetWrite } from "@/lib/establish";
import type { HabitEstablishment } from "@/lib/production-ui-types";

const progress = (minAdherence: number, windowDays: number): HabitEstablishment => ({
  minAdherence,
  windowDays,
  windowStart: "2026-08-22",
  followed: 0,
  partial: 0,
  missed: 0,
  concluded: 0,
  adherence: null,
  ageDays: 10,
  targetMet: false,
});

describe("establish target draft", () => {
  it("pre-fills a percentage and days from the target the habit has now", () => {
    expect(targetFields(progress(0.8, 60))).toEqual({ percent: "80", days: "60" });
    expect(targetFields(progress(0.835, 7))).toEqual({ percent: "83.5", days: "7" });
  });

  it("writes only the part that changed, the percentage as a fraction", () => {
    const now = progress(0.8, 60);
    expect(targetWrite("h1", now, {})).toBeNull();
    expect(targetWrite("h1", now, { percent: "80.0", days: "60" })).toBeNull();
    expect(targetWrite("h1", now, { percent: "70" })).toEqual({ habit: "h1", minAdherence: 0.7 });
    expect(targetWrite("h1", now, { percent: "80", days: "30" })).toEqual({ habit: "h1", windowDays: 30 });
    expect(targetWrite("h1", now, { percent: "1", days: "90" })).toEqual({
      habit: "h1",
      minAdherence: 0.01,
      windowDays: 90,
    });
    // No progress (established, archived): nothing to edit.
    expect(targetWrite("h1", null, { percent: "70" })).toBeNull();
  });

  it("is dirty while something differs, a bad value included, so Save can say why", () => {
    const now = progress(0.8, 60);
    expect(targetDirty(now, {})).toBe(false);
    expect(targetDirty(now, { percent: "80" })).toBe(false);
    expect(targetDirty(now, { days: "61" })).toBe(true);
    expect(targetDirty(now, { percent: "" })).toBe(true);
    expect(targetDirty(undefined, { percent: "70" })).toBe(false);
  });

  it("refuses what the kernel would", () => {
    expect(targetProblem({ percent: "75", days: "30" })).toBeNull();
    expect(targetProblem({ percent: "100" })).toBeNull();
    for (const percent of ["0", "-5", "101", "", "abc"]) {
      expect(targetProblem({ percent })).toBe("The establish target is a percentage above 0 and up to 100.");
    }
    for (const days of ["0", "1.5", "3651", ""]) {
      expect(targetProblem({ days })).toBe("The window is a whole number of days from 1 to 3650.");
    }
  });
});
