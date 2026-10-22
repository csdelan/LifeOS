import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { stepBand, summarizeWeek } from "@/lib/activity";
import { addDays, startOfWeekMonday, todayIso } from "@/lib/dates";
import { exerciseSeconds, exerciseXp, sleepXp, stepsXp } from "@/lib/activity-score";
import { exerciseVerdict } from "@/lib/exercise-rules";
import {
  MAX_RANGE_DAYS,
  SEEDED_DAYS,
  mockActivityDay,
  mockActivityDays,
  mockActivitySummary,
  mockActivityTags,
  mockActivityWorkouts,
  mockAuthorizeOura,
  mockConnectOura,
  mockOuraStatus,
  mockSetWorkoutNote,
  mockSyncActivity,
  ouraTagName,
  setMockOura,
} from "@/lib/mock/activity-store";
import { getState, mockActivityPaid, mockSettleActivity, mockSettleDomains, resetStore } from "@/lib/mock/store";
import { createMockWriteClient } from "@/lib/mock/write-client";

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

describe("the mock's tags (activity plan §3.9)", () => {
  it.each([
    ["generic_happy", null, "Happy"],
    ["generic_excited", null, "Excited"],
    ["tag_generic_late_meal", null, "Late meal"],
    ["tag_generic_alcohol", null, "Alcohol"],
    ["tag_sauna", null, "Sauna"],
    ["tableTennis", null, "Table tennis"],
    ["generic_HIIT_class", null, "Hiit class"],
    ["  generic_happy  ", null, "Happy"],
    ["custom", "Hot tub", "Hot tub"],
    ["CUSTOM", "  Hot tub ", "Hot tub"],
    ["custom", null, "Custom tag"],
    ["custom", "  ", "Custom tag"],
    [null, null, "Note"],
    ["", "Ignored", "Note"],
    ["generic_", null, "Generic"],
    ["tag_", null, "Tag"],
  ])("names %j (custom name %j) as the kernel's OuraTags.Name does: %j", (code, customName, expected) => {
    expect(ouraTagName(code, customName)).toBe(expected);
  });

  it("answers the 30 days through today, newest first", () => {
    const read = mockActivityTags();
    expect(read).toMatchObject({ from: addDays(today, -29), to: today, today });
    expect(read.tags.length).toBeGreaterThan(20);
    expect(read.tags.every((t) => t.day >= read.from && t.day <= read.to)).toBe(true);
    const starts = read.tags.map((t) => t.startedAt);
    expect(starts).toEqual([...starts].sort().reverse());
    expect(read.tags.every((t) => t.name === ouraTagName(t.typeCode, t.customName))).toBe(true);
  });

  it("has a moment, a length, a custom tag, a note and days of being sick", () => {
    const { tags } = mockActivityTags(addDays(today, -89), today);
    const coffee = tags.find((t) => t.name === "Caffeine")!;
    expect(coffee).toMatchObject({ endedAt: null, seconds: null, endDay: null });
    expect(tags.find((t) => t.name === "Sauna")).toMatchObject({ seconds: 1_200 });
    expect(tags.find((t) => t.typeCode === "custom")).toMatchObject({ name: "Cold plunge", customName: "Cold plunge", seconds: 300 });
    expect(tags.find((t) => t.typeCode == null)).toMatchObject({ name: "Note", comment: "New mattress arrived" });
    const sick = tags.find((t) => t.name === "Sick")!;
    expect(sick).toMatchObject({ day: addDays(today, -33), endDay: addDays(today, -30), comment: "Head cold" });
    expect(sick.seconds).toBeGreaterThanOrEqual(3 * 86_400 - 3_600);
  });

  it("is the same on every load and refuses a range the API wouldn't", () => {
    const first = mockActivityTags();
    resetStore();
    expect(mockActivityTags()).toEqual(first);
    expect(() => mockActivityTags(today, addDays(today, -1))).toThrow(/before it starts/);
    expect(() => mockActivityTags(addDays(today, -MAX_RANGE_DAYS), today)).toThrow(/up to 731 days/);
  });

  it("allows tags when connected", () => {
    expect(mockActivitySummary().oura.scopes).toEqual(["daily", "workout", "tag"]);
    expect(mockActivitySummary().oura.missingScopes).toEqual([]);
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

  it("lists the day's tags earliest first", () => {
    const read = mockActivityDay(addDays(today, -2));
    expect(read.tags.map((t) => t.name)).toEqual(["Caffeine", "Alcohol"]);
    expect(read.tags.every((t) => t.day === read.day)).toBe(true);
    expect(read.tags[1].comment).toBe("Two IPAs with dinner");
  });

  it("refuses something that isn't a date", () => {
    expect(() => mockActivityDay("yesterday")).toThrow("'yesterday' isn't a date: use YYYY-MM-DD.");
    expect(() => mockActivityDay("2026-02-30x")).toThrow(/isn't a date/);
  });
});

describe("the mock's activity XP (activity plan A8–A9)", () => {
  /** Sunday Oct 25, 2026, 10 a.m. local: three days after activity XP started. */
  const NOW = new Date(2026, 9, 25, 10);
  const day = (d: number) => `2026-10-${d}`;
  const isActivity = (a: { awardKey?: string | null }) => /^(steps|exercise|sleep):/.test(a.awardKey ?? "");
  const activityKeys = () =>
    getState()
      .ledger.filter((e) => e.entry === "earn" && /^(steps|exercise|sleep):/.test(e.awardKey ?? ""))
      .map((e) => e.awardKey)
      .sort();

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(NOW);
    resetStore();
  });

  afterEach(() => {
    vi.useRealTimers();
    resetStore();
  });

  it("pays each part once it's due, from the day XP started, at the day's multiplier, once", () => {
    const earned = mockSettleActivity();
    // Health's level and any rank it reaches come with them, as `RewardService.EarnAsync` pays them.
    const activity = earned.awards.filter(isActivity);
    expect(earned.awards.filter((a) => !isActivity(a)).every((a) => a.trigger === "rank")).toBe(true);
    const { days } = mockActivityDays(day(19), day(25));
    const { workouts } = mockActivityWorkouts(day(19), day(25));
    const exerciseDays = [...new Set(workouts.filter((w) => w.counted && w.day >= day(22) && w.day < day(25)).map((w) => w.day))];

    // Steps and exercise once Oura's day is over (not today's); sleep once the night is.
    expect(activityKeys()).toEqual(
      [
        ...[22, 23, 24].map((d) => `steps:${day(d)}`),
        ...exerciseDays.map((d) => `exercise:${d}`),
        ...[22, 23, 24, 25].map((d) => `sleep:${day(d)}`),
      ].sort(),
    );
    for (const a of activity) {
      const [trigger, on] = a.awardKey!.split(":");
      const row = days.find((d) => d.day === on)!;
      const base =
        trigger === "steps"
          ? stepsXp(row.steps!)
          : trigger === "sleep"
            ? sleepXp(row.sleepScore!)
            : exerciseXp(exerciseSeconds(workouts.filter((w) => w.day === on)));
      expect(a).toMatchObject({ entry: "earn", subject: null, note: expect.stringMatching(/^(Steps|Exercise|Sleep) \w{3} \w{3} \d+$/) });
      expect(a.base?.health_xp ?? a.amounts.health_xp).toBe(base);
    }
    // Paid as the ledger has it, part by part.
    const paid = mockActivityPaid(day(22), day(25));
    const total = [...paid.values()].reduce((sum, p) => sum + p.total, 0);
    expect(total).toBe(activity.reduce((sum, a) => sum + (a.amounts.health_xp ?? 0), 0));
    expect(paid.get(day(25))).toMatchObject({ steps: null, exercise: null, sleep: expect.objectContaining({ paid: expect.any(Number) }) });
    expect(mockActivityPaid(day(1), day(21)).size).toBe(0);

    expect(mockSettleActivity().awards).toEqual([]);
  });

  it("waits for 4 a.m. before paying a day's steps", () => {
    vi.setSystemTime(new Date(2026, 9, 23, 3, 59));
    resetStore();
    mockSettleActivity();
    expect(activityKeys().filter((k) => k!.startsWith("steps:"))).toEqual([]);

    vi.setSystemTime(new Date(2026, 9, 23, 4, 0));
    mockSettleActivity();
    expect(activityKeys().filter((k) => k!.startsWith("steps:"))).toEqual([`steps:${day(22)}`]);
  });

  it("pays nothing before the wallet opens", () => {
    getState().ledger = getState().ledger.filter((e) => e.entry !== "open");
    expect(mockSettleActivity().awards).toEqual([]);
  });

  it("settles on the page's sync, even a skipped one, and in the domain settle", async () => {
    const writes = createMockWriteClient();
    const first = await writes.syncActivity(false);
    expect(first.ran).toBe(true);
    expect(first.awards.filter(isActivity).length).toBeGreaterThan(0);
    expect((await writes.syncActivity(false)).awards).toEqual([]);

    resetStore();
    const skipped = mockSyncActivity();
    expect(skipped.ran).toBe(true);
    const settled = await writes.syncActivity(false);
    expect(settled.ran).toBe(false);
    expect(settled.awards.filter(isActivity).length).toBeGreaterThan(0);

    resetStore();
    const domains = mockSettleDomains();
    expect(domains.awards.filter((a) => a.trigger === "sleep").map((a) => a.awardKey)).toContain(`sleep:${day(25)}`);
  });
});
