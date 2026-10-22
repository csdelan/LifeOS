import { describe, expect, it } from "vitest";
import {
  ACTIVITY_TARGETS,
  bandWords,
  contributorName,
  describeStepDay,
  describeSteps,
  formatDistance,
  formatDuration,
  formatTemperature,
  metricWeeks,
  normalizeActivityDays,
  normalizeDayDetail,
  normalizeSummary,
  orderedContributors,
  ouraScoreWords,
  rangeFrom,
  rangeStart,
  READINESS_CONTRIBUTORS,
  readableActivity,
  rollingAverage,
  rollingMean,
  scoreBand,
  SLEEP_CONTRIBUTORS,
  sleepDayText,
  sleepStages,
  sleepValue,
  sleepValueWords,
  stepBand,
  stepWeeks,
  summarizeWeek,
  syncedAgo,
  asSentence,
  weekTitle,
  weeklyExercise,
  workoutName,
  workoutWeeks,
} from "@/lib/activity";
import { exerciseVerdict } from "@/lib/exercise-rules";
import type {
  ActivityDayDetailRead,
  ActivityDayRow,
  ActivitySummaryRead,
  ActivityDaysRead,
  SleepPeriodRow,
  WorkoutRow,
} from "@/lib/production-ui-types";

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

describe("asSentence", () => {
  it("ends the API's words with one period", () => {
    expect(asSentence("Oura refused LifeOS's access token even after refreshing it.")).toBe(
      "Oura refused LifeOS's access token even after refreshing it.",
    );
    expect(asSentence("Oura refused the stored sign-in")).toBe("Oura refused the stored sign-in.");
    expect(asSentence("for daily_activity: “The token is invalid for other reasons.”")).toBe(
      "for daily_activity: “The token is invalid for other reasons.”",
    );
    expect(asSentence("Oura answered “Unauthorized”")).toBe("Oura answered “Unauthorized”.");
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

describe("the charts' ranges", () => {
  const today = "2026-10-22"; // a Thursday
  it("asks for six days before the first one shown, for the 7-day average", () => {
    expect(rangeStart("30", today)).toBe("2026-09-23");
    expect(rangeFrom("30", today)).toBe("2026-09-17");
    expect(rangeStart("90", today)).toBe("2026-07-25");
    expect(rangeFrom("90", today)).toBe("2026-07-19");
  });

  it("starts a year of weeks on the Monday 51 weeks before this one", () => {
    expect(rangeFrom("365", today)).toBe("2025-10-27");
    expect(rangeStart("365", today)).toBe("2025-10-27");
  });
});

describe("Oura's score bands", () => {
  it("puts a score under 70 to pay attention to, 70 good, and 85 optimal", () => {
    expect([0, 69, 70, 84, 85, 100].map(scoreBand)).toEqual([
      "attention",
      "attention",
      "good",
      "good",
      "optimal",
      "optimal",
    ]);
    expect([69, 70, 85].map(ouraScoreWords)).toEqual(["Pay attention", "Good", "Optimal"]);
  });
});

describe("sleep metrics", () => {
  const today = "2026-10-22";

  it("reads a day's scores, and its time asleep in hours", () => {
    const d = day("2026-10-21", 6_000, { sleepScore: 73, readinessScore: 65, asleepS: 6.5 * 3_600 });
    expect(sleepValue(d, "sleep")).toBe(73);
    expect(sleepValue(d, "readiness")).toBe(65);
    expect(sleepValue(d, "asleep")).toBe(6.5);
    expect(sleepValue(day("2026-10-21", null), "asleep")).toBeNull();
    expect(sleepValueWords(72.6, "sleep")).toBe("73");
    expect(sleepValueWords(7.2, "asleep")).toBe("7 h 12 min");
  });

  it("averages each value with the six before, counting the last one, and skips the gaps", () => {
    expect(rollingMean([70, null, 80, 90], 2)).toEqual([70, 70, 80, 85]);
    expect(rollingMean([null, null])).toEqual([null, null]);
    const week = rollingMean([60, 70, 80, 90, 100, 60, 70, 80]);
    expect(week[6]).toBeCloseTo(530 / 7);
    expect(week[7]).toBeCloseTo(550 / 7);
  });

  it("says a day's score with its band, and its time asleep against time in bed with the naps", () => {
    const d = day("2026-10-21", 6_000, {
      sleepScore: 73,
      readinessScore: 65,
      asleepS: 6 * 3_600 + 41 * 60,
      inBedS: 7 * 3_600 + 21 * 60,
      napCount: 1,
      napAsleepS: 25 * 60,
    });
    expect(sleepDayText(d, "sleep", today)).toBe("sleep score 73, good");
    expect(sleepDayText(d, "readiness", today)).toBe("readiness 65, pay attention");
    expect(sleepDayText(d, "asleep", today)).toBe("6 h 41 min asleep of 7 h 21 min in bed, and a nap (25 min)");
    expect(sleepDayText({ ...d, napCount: 2, napAsleepS: 50 * 60, inBedS: null }, "asleep", today)).toBe(
      "6 h 41 min asleep, and 2 naps (50 min)",
    );
  });

  it("says a night that hasn't synced, and a day without one", () => {
    const empty = day(today, null);
    expect(sleepDayText(empty, "sleep", today)).toBe("last night hasn't synced yet");
    expect(sleepDayText(empty, "asleep", today)).toBe("last night hasn't synced yet");
    const old = day("2026-10-01", null);
    expect(sleepDayText(old, "sleep", today)).toBe("no sleep score");
    expect(sleepDayText(old, "readiness", today)).toBe("no readiness");
    expect(sleepDayText(old, "asleep", today)).toBe("no sleep recorded");
  });

  it("groups nights into Monday weeks, averages the known ones, and marks this week partial", () => {
    const days = [
      day("2026-10-12", null, { sleepScore: 70 }),
      day("2026-10-13", null, { sleepScore: 80 }),
      day("2026-10-14", null),
      day("2026-10-18", null, { sleepScore: 90 }),
      day("2026-10-19", null),
      day("2026-10-22", null, { sleepScore: 61 }),
      day("2026-10-23", null, { sleepScore: 99 }),
    ];
    expect(metricWeeks(days, today, (d) => d.sleepScore)).toEqual([
      { from: "2026-10-12", to: "2026-10-18", average: 80, nights: 3, partial: false },
      { from: "2026-10-19", to: "2026-10-25", average: 61, nights: 1, partial: true },
    ]);
    expect(metricWeeks([day("2026-10-19", null)], today, (d) => d.sleepScore)[0]).toMatchObject({
      average: null,
      nights: 0,
      partial: false,
    });
  });
});

describe("contributors", () => {
  it("names Oura's contributors as the app does", () => {
    expect(contributorName("hrv_balance")).toBe("HRV balance");
    expect(contributorName("rem_sleep")).toBe("REM sleep");
    expect(contributorName("previous_day_activity")).toBe("Previous day activity");
    for (const key of [...SLEEP_CONTRIBUTORS, ...READINESS_CONTRIBUTORS]) {
      expect(contributorName(key)).not.toMatch(/_/);
    }
  });

  it("lists them in Oura's order, leaves out the missing, and puts new ones after by name", () => {
    const rows = orderedContributors(
      { timing: 80, zebra_index: 50, deep_sleep: null, total_sleep: 90, apple_score: 40 },
      SLEEP_CONTRIBUTORS,
    );
    expect(rows).toEqual([
      { key: "total_sleep", name: "Total sleep", value: 90 },
      { key: "deep_sleep", name: "Deep sleep", value: null },
      { key: "timing", name: "Timing", value: 80 },
      { key: "apple_score", name: "Apple score", value: 40 },
      { key: "zebra_index", name: "Zebra index", value: 50 },
    ]);
    expect(orderedContributors({}, READINESS_CONTRIBUTORS)).toEqual([]);
  });
});

describe("a night's stages and temperature", () => {
  const period = (extra: Partial<SleepPeriodRow>): SleepPeriodRow => ({
    id: "p",
    type: "long_sleep",
    startedAt: "2026-10-21T23:00:00Z",
    endedAt: "2026-10-22T07:00:00Z",
    inBedS: null,
    asleepS: null,
    deepS: null,
    lightS: null,
    remS: null,
    awakeS: null,
    latencyS: null,
    efficiency: null,
    averageHrv: null,
    averageHr: null,
    lowestHr: null,
    ...extra,
  });

  it("splits the time in bed by stage, deepest first, with each one's share", () => {
    expect(sleepStages(period({ deepS: 3_600, lightS: 7_200, remS: 5_400, awakeS: 1_800 }))).toEqual([
      { stage: "deep", seconds: 3_600, share: 0.2 },
      { stage: "light", seconds: 7_200, share: 0.4 },
      { stage: "rem", seconds: 5_400, share: 0.3 },
      { stage: "awake", seconds: 1_800, share: 0.1 },
    ]);
    expect(sleepStages(period({ deepS: 1_800, lightS: 1_800 })).map((s) => s.share)).toEqual([0.5, 0.5, 0, 0]);
  });

  it("has no stages when Oura gave none", () => {
    expect(sleepStages(period({}))).toEqual([]);
    expect(sleepStages(period({ deepS: 0, lightS: 0, remS: 0, awakeS: 0 }))).toEqual([]);
  });

  it("says the temperature against the usual to a tenth, with its sign", () => {
    expect(formatTemperature(0.26)).toBe("+0.3 °C");
    expect(formatTemperature(-0.2)).toBe("−0.2 °C");
    expect(formatTemperature(0.04)).toBe("±0.0 °C");
    expect(formatTemperature(-0.04)).toBe("±0.0 °C");
  });
});

describe("normalizeDayDetail", () => {
  it("turns the API's string numbers into numbers and fills what's missing", () => {
    const detail = normalizeDayDetail({
      day: "2026-10-21",
      today: "2026-10-22",
      minimumSteps: "5000",
      goalSteps: "7500",
      summary: { ...day("2026-10-21", null), steps: "6100", sleepScore: "73" },
      sleepContributors: { total_sleep: "88", timing: null },
      readinessContributors: null,
      temperatureDeviation: "-0.2",
      temperatureTrendDeviation: null,
      night: {
        id: "n",
        startedAt: "2026-10-20T23:00:00Z",
        endedAt: "2026-10-21T07:00:00Z",
        inBedS: "28800",
        asleepS: "25200",
        deepS: "5000",
        lowestHr: "48",
      },
    } as unknown as ActivityDayDetailRead);
    expect(detail).toMatchObject({ minimumSteps: 5_000, goalSteps: 7_500, temperatureDeviation: -0.2 });
    expect(detail.summary).toMatchObject({ steps: 6_100, sleepScore: 73 });
    expect(detail.sleepContributors).toEqual({ total_sleep: 88, timing: null });
    expect(detail.readinessContributors).toEqual({});
    expect(detail.night).toMatchObject({
      type: "",
      inBedS: 28_800,
      asleepS: 25_200,
      deepS: 5_000,
      remS: null,
      lowestHr: 48,
    });
    expect(detail.naps).toEqual([]);
    expect(detail.workouts).toEqual([]);
  });
});
