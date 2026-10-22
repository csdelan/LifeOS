import { describe, expect, it } from "vitest";
import { stepBand, summarizeWeek } from "@/lib/activity";
import { addDays, startOfWeekMonday, todayIso } from "@/lib/dates";
import { exerciseVerdict } from "@/lib/exercise-rules";
import {
  MAX_RANGE_DAYS,
  SEEDED_DAYS,
  mockActivityDay,
  mockActivityDays,
  mockActivitySummary,
  mockActivityWorkouts,
  mockAuthorizeOura,
  mockConnectOura,
  mockOuraStatus,
  mockSetWorkoutNote,
  mockSyncActivity,
  setMockOura,
} from "@/lib/mock/activity-store";
import { resetStore } from "@/lib/mock/store";

const today = todayIso();

describe("the mock's activity days", () => {
  it("answers every day of the 30 through today, oldest first, with the thresholds", () => {
    const read = mockActivityDays();
    expect(read).toMatchObject({ from: addDays(today, -29), to: today, today, minimumSteps: 5_000, goalSteps: 7_500 });
    expect(read.days.map((d) => d.day)).toEqual(Array.from({ length: 30 }, (_, i) => addDays(today, i - 29)));
  });

  it("bands each day as the kernel does, and leaves a day without the ring empty", () => {
    const { days } = mockActivityDays(addDays(today, -(SEEDED_DAYS - 1)), today);
    expect(days).toHaveLength(SEEDED_DAYS);
    for (const d of days.filter((x) => x.steps != null)) expect(d.band).toBe(stepBand(d.steps!));
    const empty = days.filter((d) => d.steps == null);
    expect(empty.length).toBeGreaterThan(0);
    expect(empty.every((d) => d.band == null && d.sleepScore == null)).toBe(true);
    // A day before the seed is there too, with nothing on it.
    expect(mockActivityDays(addDays(today, -400), addDays(today, -399)).days.every((d) => d.steps == null)).toBe(true);
  });

  it("has days in every band and a day with hours off the ring", () => {
    const { days } = mockActivityDays(addDays(today, -89), today);
    expect(new Set(days.map((d) => d.band))).toEqual(new Set(["under", "minimum", "goal", null]));
    expect(days.some((d) => (d.nonWearS ?? 0) >= 2 * 3_600)).toBe(true);
  });

  it("is the same on every load", () => {
    const first = mockActivityDays();
    resetStore();
    expect(mockActivityDays()).toEqual(first);
  });

  it("refuses a range backwards or longer than the API allows", () => {
    expect(() => mockActivityDays(today, addDays(today, -1))).toThrow(/before it starts/);
    expect(() => mockActivityDays(addDays(today, -MAX_RANGE_DAYS), today)).toThrow(/up to 731 days/);
  });
});

describe("the mock's workouts", () => {
  it("answers the last 12 whole weeks, newest first, counted or not", () => {
    const read = mockActivityWorkouts();
    expect(read.from).toBe(addDays(startOfWeekMonday(today), -77));
    const starts = read.workouts.map((w) => w.startedAt);
    expect(starts).toEqual([...starts].sort().reverse());
    expect(read.workouts.every((w) => w.day >= read.from && w.day <= today)).toBe(true);
  });

  it("counts only what Chris recorded, never a walk, with the kernel's reasons", () => {
    const { workouts } = mockActivityWorkouts(addDays(today, -200), today);
    for (const w of workouts) {
      expect({ counted: w.counted, reason: w.reason }).toEqual(exerciseVerdict(w.source, w.activity));
    }
    const reasons = new Set(workouts.filter((w) => !w.counted).map((w) => w.reason));
    expect(reasons).toEqual(new Set(["A walk: it's in your steps", "Detected by Oura", "Unknown source"]));
    expect(workouts.some((w) => w.note)).toBe(true);
  });
});

describe("the mock's summary", () => {
  it("answers today, this week against last from Monday, and the connection", () => {
    const summary = mockActivitySummary();
    const monday = startOfWeekMonday(today);
    expect(summary.todaySoFar.day).toBe(today);
    expect(summary.todayWorkouts.every((w) => w.day === today)).toBe(true);
    expect(summary.thisWeek.from).toBe(monday);
    expect(summary.lastWeek).toMatchObject({ from: addDays(monday, -7), to: addDays(monday, -1) });

    const days = mockActivityDays(addDays(monday, -7), today).days;
    const workouts = mockActivityWorkouts(addDays(monday, -7), today).workouts;
    expect(summary.lastWeek).toEqual(summarizeWeek(addDays(monday, -7), addDays(monday, -1), days, workouts, today));
    expect(summary.oura.state).toBe("connected");
  });
});

describe("the mock's sync", () => {
  it("runs on the first open, then skips under 5 minutes unless forced", () => {
    expect(mockSyncActivity().ran).toBe(true);
    const skipped = mockSyncActivity();
    expect(skipped).toMatchObject({ ran: false, changed: false });
    expect(skipped.lastSyncedAt).toBe(mockOuraStatus().lastSyncedAt);
    expect(mockSyncActivity(true).ran).toBe(true);
  });

  it("refuses without a connection, in the API's words", () => {
    setMockOura({ state: "reconnect", needsReconnect: true });
    expect(() => mockSyncActivity(true)).toThrow(/Reconnect Oura/);
    setMockOura({ state: "not_connected", connected: false, needsReconnect: false });
    expect(() => mockSyncActivity(true)).toThrow(/Connect Oura/);
    setMockOura({ state: "not_configured", configured: false });
    expect(() => mockSyncActivity(true)).toThrow(/isn't set up/);
  });
});

describe("connecting the mock", () => {
  it("sends the browser back with a code and a once-only state", () => {
    setMockOura({ state: "not_connected", connected: false, lastSyncedAt: null });
    const back = new URL(mockAuthorizeOura("http://localhost:5177/activity/connected"));
    expect(back.pathname).toBe("/activity/connected");
    const state = back.searchParams.get("state")!;

    expect(() => mockConnectOura("mock-code", "someone-else's")).toThrow(/wasn't started here/);
    expect(mockConnectOura(back.searchParams.get("code")!, state)).toMatchObject({ state: "connected", connected: true });
    expect(() => mockConnectOura("mock-code", state)).toThrow(/wasn't started here/);
  });

  it("can't begin when Oura isn't set up", () => {
    setMockOura({ configured: false });
    expect(() => mockAuthorizeOura("http://localhost/activity/connected")).toThrow(/isn't set up/);
  });
});

describe("workout notes in the mock", () => {
  const first = () => mockActivityWorkouts().workouts.find((w) => w.counted && !w.note)!;

  it("sets a trimmed note and clears it with an empty one", () => {
    const id = first().id;
    expect(mockSetWorkoutNote(id, "  Easy pace, felt good  ")).toMatchObject({ note: "Easy pace, felt good" });
    expect(mockActivityWorkouts().workouts.find((w) => w.id === id)?.noteAt).not.toBeNull();
    expect(mockSetWorkoutNote(id, "")).toMatchObject({ note: null, noteAt: null });
  });

  it("refuses a line break, too long a note, or a workout it doesn't hold", () => {
    const id = first().id;
    expect(() => mockSetWorkoutNote(id, "one\ntwo")).toThrow(/one line/);
    expect(() => mockSetWorkoutNote(id, "x".repeat(141))).toThrow(/up to 140 characters; that one is 141/);
    expect(mockSetWorkoutNote(id, "x".repeat(140)).note).toHaveLength(140);
    expect(() => mockSetWorkoutNote("nope", "hi")).toThrow(/holds no workout 'nope'/);
  });
});

describe("one day in the mock", () => {
  it("answers today by default, with the thresholds and the day's row", () => {
    const read = mockActivityDay();
    expect(read).toMatchObject({ day: today, today, minimumSteps: 5_000, goalSteps: 7_500 });
    expect(read.summary).toEqual(mockActivityDays(today, today).days[0]);
  });

  it("splits the night into stages that add up to the time asleep, and the rest awake", () => {
    const { summary, night } = mockActivityDay(addDays(today, -2));
    expect(night).not.toBeNull();
    const n = night!;
    expect(n).toMatchObject({ type: "long_sleep", startedAt: summary.bedAt, endedAt: summary.wokeAt });
    expect(n.deepS! + n.lightS! + n.remS!).toBe(summary.asleepS);
    expect(n.awakeS).toBe(summary.inBedS! - summary.asleepS!);
    expect(n.lowestHr).toBeGreaterThanOrEqual(40);
  });

  it("gives both scores' contributors between 15 and 100, without the newest readiness one", () => {
    const { sleepContributors, readinessContributors } = mockActivityDay(addDays(today, -2));
    expect(Object.keys(sleepContributors)).toHaveLength(7);
    expect(Object.keys(readinessContributors)).toHaveLength(8);
    expect(readinessContributors).not.toHaveProperty("sleep_regularity");
    for (const v of [...Object.values(sleepContributors), ...Object.values(readinessContributors)]) {
      expect(v).toBeGreaterThanOrEqual(15);
      expect(v).toBeLessThanOrEqual(100);
    }
  });

  it("gives a nap on a day that had one, and nothing on a day without the ring", () => {
    const napDay = mockActivityDay(addDays(today, -4));
    expect(napDay.summary.napCount).toBe(1);
    expect(napDay.naps).toHaveLength(1);
    expect(napDay.naps[0].asleepS).toBe(napDay.summary.napAsleepS);

    const empty = mockActivityDay(addDays(today, -23));
    expect(empty).toMatchObject({ night: null, naps: [], sleepContributors: {}, readinessContributors: {} });
    expect(empty.temperatureDeviation).toBeNull();
  });

  it("lists the day's workouts earliest first", () => {
    const { workouts } = mockActivityWorkouts(addDays(today, -60), today);
    const busy = workouts.find((w) => workouts.filter((x) => x.day === w.day).length > 1) ?? workouts[0];
    const read = mockActivityDay(busy.day);
    expect(read.workouts.length).toBeGreaterThan(0);
    expect(read.workouts.every((w) => w.day === busy.day)).toBe(true);
    const starts = read.workouts.map((w) => w.startedAt);
    expect(starts).toEqual([...starts].sort());
  });

  it("refuses something that isn't a date", () => {
    expect(() => mockActivityDay("yesterday")).toThrow("'yesterday' isn't a date: use YYYY-MM-DD.");
    expect(() => mockActivityDay("2026-02-30x")).toThrow(/isn't a date/);
  });
});
