import { describe, expect, it } from "vitest";
import {
  ACTIVITY_TARGETS,
  bandWords,
  describeStepDay,
  describeSteps,
  formatDistance,
  formatDuration,
  normalizeActivityDays,
  normalizeSummary,
  readableActivity,
  rollingAverage,
  stepBand,
  stepWeeks,
  summarizeWeek,
  syncedAgo,
  weekTitle,
  weeklyExercise,
  workoutName,
  workoutWeeks,
} from "@/lib/activity";
import { exerciseVerdict } from "@/lib/exercise-rules";
import type { ActivityDayRow, ActivitySummaryRead, ActivityDaysRead, WorkoutRow } from "@/lib/production-ui-types";

function day(date: string, steps: number | null, extra: Partial<ActivityDayRow> = {}): ActivityDayRow {
  return {
    day: date,
    steps,
    band: steps == null ? null : stepBand(steps),
    activeCalories: null,
    totalCalories: null,
    walkingM: null,
    highS: null,
    mediumS: null,
    lowS: null,
    nonWearS: null,
    activityScore: null,
    sleepScore: null,
    readinessScore: null,
    asleepS: null,
    inBedS: null,
    bedAt: null,
    wokeAt: null,
    napCount: 0,
    napAsleepS: 0,
    ...extra,
  };
}

function workout(id: string, date: string, opts: Partial<WorkoutRow> = {}): WorkoutRow {
  const activity = opts.activity ?? "running";
  const source = opts.source ?? "manual";
  const seconds = opts.seconds ?? 40 * 60;
  const startedAt = opts.startedAt ?? `${date}T07:00:00Z`;
  return {
    id,
    day: date,
    activity,
    label: null,
    startedAt,
    endedAt: new Date(Date.parse(startedAt) + seconds * 1_000).toISOString(),
    seconds,
    calories: 420,
    distanceM: null,
    intensity: null,
    source,
    ...exerciseVerdict(source, activity),
    note: null,
    noteAt: null,
    syncedAt: startedAt,
    ...opts,
  };
}

describe("step bands", () => {
  it("puts a count under the minimum, over it, or at the goal", () => {
    expect([4_999, 5_000, 7_499, 7_500, 12_000].map((s) => stepBand(s))).toEqual([
      "under",
      "minimum",
      "minimum",
      "goal",
      "goal",
    ]);
    expect(stepBand(6_000, { minimumSteps: 6_000, goalSteps: 9_000 })).toBe("minimum");
    expect(bandWords("minimum")).toBe("over the minimum");
  });

  it("says a count against the thresholds", () => {
    expect(describeSteps(6_240, ACTIVITY_TARGETS)).toBe("6,240 steps, over the minimum, 1,260 short of the goal");
    expect(describeSteps(3_100, ACTIVITY_TARGETS, true)).toBe("3,100 steps so far, 1,900 under the minimum");
    expect(describeSteps(7_500, ACTIVITY_TARGETS)).toBe("7,500 steps, at the goal");
  });

  it("says a day's steps, a long time without the ring, and a day with nothing", () => {
    expect(describeStepDay(day("2026-10-22", 4_000), ACTIVITY_TARGETS, "2026-10-22")).toBe(
      "Today: 4,000 steps so far, 1,000 under the minimum",
    );
    expect(describeStepDay(day("2026-10-20", 8_100, { nonWearS: 3 * 3_600 }), ACTIVITY_TARGETS, "2026-10-22")).toMatch(
      /: 8,100 steps, at the goal, 3 h without the ring$/,
    );
    expect(describeStepDay(day("2026-10-20", 8_100, { nonWearS: 1_800 }), ACTIVITY_TARGETS, "2026-10-22")).not.toMatch(
      /without the ring/,
    );
    expect(describeStepDay(day("2026-10-19", null), ACTIVITY_TARGETS, "2026-10-22")).toMatch(/: no steps recorded$/);
  });
});

describe("the 7-day average", () => {
  it("averages the days with steps among each day and the six before, and not today", () => {
    const days = [
      day("2026-10-14", 7_000),
      day("2026-10-15", null),
      day("2026-10-16", 5_000),
      day("2026-10-17", 6_000),
      day("2026-10-18", 6_000),
      day("2026-10-19", 6_000),
      day("2026-10-20", 6_000),
      day("2026-10-21", 13_000),
      day("2026-10-22", 1_000),
    ];
    expect(rollingAverage(days, "2026-10-22")).toEqual([
      7_000,
      7_000,
      6_000,
      6_000,
      6_000,
      6_000,
      6_000, // 14th–20th: six days with steps
      7_000, // 15th–21st: the 15th has none
      null, // today is still going
    ]);
  });
});

describe("weekly step bars", () => {
  it("groups days into Monday weeks and averages the complete days", () => {
    const days = [
      day("2026-10-12", 9_000),
      day("2026-10-13", 7_500),
      day("2026-10-14", 4_999),
      day("2026-10-15", null),
      day("2026-10-19", 8_000),
      day("2026-10-20", 4_000),
      day("2026-10-21", 6_000),
      day("2026-10-22", 7_600),
    ];
    expect(stepWeeks(days, "2026-10-22")).toEqual([
      { from: "2026-10-12", to: "2026-10-18", average: 7_166, days: 3, partial: false },
      { from: "2026-10-19", to: "2026-10-25", average: 6_000, days: 3, partial: true },
    ]);
  });

  it("shows a Monday's count so far when the week has no complete days", () => {
    expect(stepWeeks([day("2026-10-26", 2_000)], "2026-10-26")).toEqual([
      { from: "2026-10-26", to: "2026-11-01", average: 2_000, days: 0, partial: true },
    ]);
  });
});

describe("summarizeWeek", () => {
  // The kernel's hand-worked week (ActivityEndpointTests.The_summary_has_today_so_far_this_week_against_last…).
  const today = "2026-10-22";
  const days = [
    day("2026-10-12", 9_000),
    day("2026-10-13", 7_500),
    day("2026-10-14", 4_999),
    day("2026-10-19", 8_000),
    day("2026-10-20", 4_000, { sleepScore: 80 }),
    day("2026-10-21", 6_000),
    day("2026-10-22", 7_600, { sleepScore: 85, readinessScore: 76 }),
  ];
  const workouts = [
    workout("w-ride", "2026-10-13", { activity: "cycling", seconds: 3_600, calories: 500 }),
    workout("w-auto", "2026-10-15", { source: "autodetected" }),
    workout("w-walk", "2026-10-20", { activity: "walking" }),
    workout("w-run", "2026-10-21"),
    workout("w-yoga", today, { activity: "yoga", seconds: 30 * 60, calories: null }),
  ];

  it("averages this week's complete days and counts today at the goal once it gets there", () => {
    expect(summarizeWeek("2026-10-19", "2026-10-25", days, workouts, today)).toEqual({
      from: "2026-10-19",
      to: "2026-10-25",
      averageSteps: 6_000,
      stepDays: 3,
      goalDays: 2,
      underDays: 1,
      sessions: 2,
      exerciseSeconds: 70 * 60,
      exerciseCalories: 420,
      averageSleep: 83,
      averageReadiness: 76,
    });
  });

  it("counts last week the same, without the detected workout", () => {
    expect(summarizeWeek("2026-10-12", "2026-10-18", days, workouts, today)).toEqual({
      from: "2026-10-12",
      to: "2026-10-18",
      averageSteps: 7_166,
      stepDays: 3,
      goalDays: 2,
      underDays: 1,
      sessions: 1,
      exerciseSeconds: 3_600,
      exerciseCalories: 500,
      averageSleep: null,
      averageReadiness: null,
    });
  });

  it("doesn't count today under the minimum until it's over", () => {
    const week = summarizeWeek("2026-10-19", "2026-10-25", [day("2026-10-19", 5_000), day(today, 3_000)], [], today);
    expect([week.averageSteps, week.stepDays, week.goalDays, week.underDays]).toEqual([5_000, 1, 0, 0]);
  });
});

describe("workout words", () => {
  it("makes Oura's activities readable", () => {
    expect(["tableTennis", "strength_training", "padel_v2", "running", ""].map(readableActivity)).toEqual([
      "Table tennis",
      "Strength training",
      "Padel v2",
      "Running",
      "Workout",
    ]);
    expect(workoutName({ activity: "hiking", label: "Ridge trail" })).toBe("Ridge trail");
    expect(workoutName({ activity: "hiking", label: "  " })).toBe("Hiking");
  });

  it("says durations and distances", () => {
    expect([40 * 60, 65 * 60, 120 * 60, 59].map(formatDuration)).toEqual(["40 min", "1 h 5 min", "2 h", "1 min"]);
    expect([850, 5_150].map(formatDistance)).toEqual(["850 m", "5.2 km"]);
  });

  it("names weeks from today's", () => {
    expect(weekTitle("2026-10-19", "2026-10-22")).toBe("This week");
    expect(weekTitle("2026-10-12", "2026-10-22")).toBe("Last week");
    expect(weekTitle("2026-10-05", "2026-10-22")).toMatch(/^Week of Oct 5$/);
  });

  it("groups workouts by week, newest first", () => {
    const weeks = workoutWeeks([
      workout("a", "2026-10-13"),
      workout("b", "2026-10-21"),
      workout("c", "2026-10-19"),
    ]);
    expect(weeks.map((w) => [w.from, w.workouts.map((x) => x.id)])).toEqual([
      ["2026-10-19", ["b", "c"]],
      ["2026-10-12", ["a"]],
    ]);
  });

  it("sums counted exercise per week, oldest first, through this week", () => {
    const weeks = weeklyExercise(
      [workout("run", "2026-10-21"), workout("walk", "2026-10-20", { activity: "walking" }), workout("old", "2026-09-01")],
      "2026-10-22",
      3,
    );
    expect(weeks).toEqual([
      { from: "2026-10-05", seconds: 0, sessions: 0 },
      { from: "2026-10-12", seconds: 0, sessions: 0 },
      { from: "2026-10-19", seconds: 2_400, sessions: 1 },
    ]);
  });
});

describe("syncedAgo", () => {
  const now = new Date("2026-10-22T12:00:00Z");
  it("says how long ago LifeOS synced", () => {
    expect(syncedAgo("2026-10-22T11:59:30Z", now)).toBe("just now");
    expect(syncedAgo("2026-10-22T11:57:00Z", now)).toBe("3 min ago");
    expect(syncedAgo("2026-10-22T09:00:00Z", now)).toBe("3 h ago");
    expect(syncedAgo("2026-10-21T09:00:00Z", now)).toBe("yesterday");
    expect(syncedAgo("2026-10-23T09:00:00Z", now)).toBe("just now");
  });
});

describe("normalizers", () => {
  it("turns the API's string numbers into numbers and fills the defaults", () => {
    const days = normalizeActivityDays({
      from: "2026-10-22",
      to: "2026-10-22",
      today: "2026-10-22",
      minimumSteps: "5000",
      goalSteps: "7500",
      days: [{ ...day("2026-10-22", null), steps: "6100", napCount: "1", asleepS: undefined } as unknown as ActivityDayRow],
    } as unknown as ActivityDaysRead);
    expect(days.minimumSteps).toBe(5_000);
    expect(days.days[0]).toMatchObject({ steps: 6_100, napCount: 1, asleepS: null });

    const summary = normalizeSummary({
      today: "2026-10-22",
      minimumSteps: 5_000,
      goalSteps: 7_500,
      todaySoFar: day("2026-10-22", 100),
      todayWorkouts: [{ ...workout("w", "2026-10-22"), seconds: "600" } as unknown as WorkoutRow],
      thisWeek: { from: "a", to: "b", averageSteps: "6000", stepDays: "3" },
      lastWeek: { from: "a", to: "b" },
      oura: { state: "something new" },
    } as unknown as ActivitySummaryRead);
    expect(summary.todayWorkouts[0].seconds).toBe(600);
    expect(summary.thisWeek).toMatchObject({ averageSteps: 6_000, stepDays: 3, sessions: 0, averageSleep: null });
    expect(summary.lastWeek.averageSteps).toBeNull();
    expect(summary.oura).toMatchObject({ state: "not_connected", connected: false, scopes: [] });
  });
});
