import { describe, expect, it } from "vitest";
import {
  ACTIVITY_CAP,
  ACTIVITY_RULES,
  activityPays,
  EXERCISE_MAX,
  exerciseSeconds,
  exerciseXp,
  scoreActivityDay,
  SLEEP_MAX,
  sleepPart,
  sleepXp,
  STEPS_MAX,
  stepsPart,
  stepsXp,
  type ExerciseSpan,
} from "@/lib/activity-score";

const MORNING = Date.parse("2026-10-22T07:00:00-07:00");

/** A workout starting `at` minutes after 7 a.m. and lasting `minutes`. */
function workout(at: number, minutes: number, counted = true): ExerciseSpan {
  const start = MORNING + at * 60_000;
  return {
    counted,
    startedAt: new Date(start).toISOString(),
    endedAt: new Date(start + minutes * 60_000).toISOString(),
  };
}

// The same table as the kernel's ActivityXpRulesTests (activity plan §3.8).
describe("ACTIVITY_RULES", () => {
  it("adds up to 80: 25 steps, 35 exercise, 20 sleep, paid in Health XP from 2026-10-22", () => {
    expect([STEPS_MAX, EXERCISE_MAX, SLEEP_MAX, ACTIVITY_CAP]).toEqual([25, 35, 20, 80]);
    expect(ACTIVITY_RULES.currency).toBe("health_xp");
    expect(ACTIVITY_RULES.xpFrom).toBe("2026-10-22");
  });

  it("rises along every curve and tier", () => {
    for (const curve of [ACTIVITY_RULES.stepsCurve, ACTIVITY_RULES.sleepCurve]) {
      curve.slice(1).forEach((point, i) => {
        expect(point.at).toBeGreaterThan(curve[i].at);
        expect(point.xp).toBeGreaterThan(curve[i].xp);
      });
    }
    const tiers = ACTIVITY_RULES.exerciseTiers;
    tiers.slice(1).forEach((tier, i) => {
      expect(tier.fromMinutes).toBeGreaterThan(tiers[i].fromMinutes);
      expect(tier.xp).toBeGreaterThan(tiers[i].xp);
    });
  });
});

describe("the worked examples", () => {
  it("pays 6,000 steps 15 and 3,750 steps 9 (8.5, halves up)", () => {
    expect([stepsPart(6_000), stepsXp(6_000)]).toEqual([15.2, 15]);
    expect([stepsPart(3_750), stepsXp(3_750)]).toEqual([8.5, 9]);
  });

  it("pays a sleep score of 75 13 (12.5) and 82 17", () => {
    expect([sleepPart(75), sleepXp(75)]).toEqual([12.5, 13]);
    expect([sleepPart(82), sleepXp(82)]).toEqual([17, 17]);
  });

  it("adds two runs of 12 and 20 minutes to 32, which pays 25", () => {
    const seconds = exerciseSeconds([workout(0, 12), workout(120, 20)]);
    expect(seconds).toBe(32 * 60);
    expect(exerciseXp(seconds)).toBe(25);
  });

  it("pays 7,500 steps, a 30-minute workout and a sleep score of 80 20 + 25 + 15 = 60", () => {
    expect(scoreActivityDay({ steps: 7_500, exerciseSeconds: 30 * 60, sleepScore: 80 })).toEqual({
      steps: 20,
      exercise: 25,
      sleep: 15,
      total: 60,
    });
  });
});

describe("stepsXp", () => {
  it.each([
    [0, 0],
    [1, 0],
    [1_250, 3],
    [2_500, 5],
    [3_750, 9],
    [4_999, 12],
    [5_000, 12],
    [6_000, 15],
    [7_499, 20],
    [7_500, 20],
    [8_750, 23],
    [10_000, 25],
    [10_001, 25],
    [30_000, 25],
  ])("pays %i steps %i", (steps, xp) => {
    expect(stepsXp(steps)).toBe(xp);
  });

  it.each([
    [1_250, 2.5],
    [4_999, 11.9972],
    [6_250, 16],
    [8_750, 22.5],
  ])("puts %i steps on the line at %d", (steps, part) => {
    expect(stepsPart(steps)).toBeCloseTo(part, 9);
  });
});

describe("sleepXp", () => {
  it.each([
    [0, 0],
    [50, 0],
    [51, 1],
    [55, 3],
    [60, 5],
    [65, 8],
    [70, 10],
    [75, 13],
    [80, 15],
    [82, 17],
    [84, 19],
    [85, 20],
    [100, 20],
  ])("pays a score of %i %i", (score, xp) => {
    expect(sleepXp(score)).toBe(xp);
  });
});

describe("exerciseXp", () => {
  it.each([
    [0, 0],
    [599, 0],
    [600, 8],
    [1_199, 8],
    [1_200, 15],
    [1_799, 15],
    [1_800, 25],
    [2_699, 25],
    [2_700, 35],
    [4 * 3600, 35],
  ])("pays %i seconds %i", (seconds, xp) => {
    expect(exerciseXp(seconds)).toBe(xp);
  });
});

describe("exerciseSeconds", () => {
  it("adds a day's workouts and counts overlaps once", () => {
    expect(exerciseSeconds([workout(0, 20), workout(60, 15)])).toBe(35 * 60);
    expect(exerciseSeconds([workout(20, 20), workout(0, 30)])).toBe(40 * 60);
    expect(exerciseSeconds([workout(0, 45), workout(10, 5)])).toBe(45 * 60);
    expect(exerciseSeconds([workout(0, 15), workout(15, 15)])).toBe(30 * 60);
  });

  it("pays a split workout no more than the whole", () => {
    const whole = exerciseXp(exerciseSeconds([workout(0, 40)]));
    expect(whole).toBe(25);
    expect(exerciseXp(exerciseSeconds([workout(0, 20), workout(20, 20)]))).toBe(whole);
    expect(exerciseXp(exerciseSeconds([workout(0, 40), workout(0, 40)]))).toBe(whole);
  });

  it("adds nothing for workouts that don't count", () => {
    expect(exerciseSeconds([workout(0, 60, false), workout(0, 60, false)])).toBe(0);
    expect(exerciseSeconds([workout(0, 60, false), workout(90, 15)])).toBe(15 * 60);
  });

  it("covers nothing for a workout that ends before it starts", () => {
    expect(exerciseSeconds([workout(0, 0), workout(0, -5)])).toBe(0);
    expect(exerciseSeconds([])).toBe(0);
  });
});

describe("scoreActivityDay", () => {
  it("pays nothing for missing data and never reads it as 0", () => {
    expect(scoreActivityDay({ steps: null, exerciseSeconds: 0, sleepScore: null })).toEqual({
      steps: null,
      exercise: 0,
      sleep: null,
      total: 0,
    });
    expect(scoreActivityDay({ steps: 0, exerciseSeconds: 0, sleepScore: 40 })).toMatchObject({ steps: 0, sleep: 0 });
  });
});

describe("activityPays", () => {
  it("pays from 2026-10-22, never before", () => {
    expect(activityPays("2026-10-21")).toBe(false);
    expect(activityPays("2026-10-22")).toBe(true);
    expect(activityPays("2027-01-01")).toBe(true);
  });
});
