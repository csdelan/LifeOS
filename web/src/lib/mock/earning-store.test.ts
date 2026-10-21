import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import { maintainingHabitRows, todayHabitRows } from "@/lib/derive";
import {
  adhereHabit,
  applyTimer,
  detailWithBounty,
  getState,
  mockHabits,
  mockLedger,
  mockSetBounty,
  mockSetHabitTarget,
  mockWallet,
  resetStore,
  setStatus,
  withBounty,
} from "@/lib/mock/store";
import { balanceOf } from "@/lib/rewards";

const ago = (n: number) => addDays(todayIso(), -n);
const xp = () => balanceOf(mockWallet(), "xp");

describe("mock earning (RewardService.ReconcileAsync, scoped to the write)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("pays a Task's default on completion, gives it back on reopen, and pays once on re-completion", () => {
    const before = xp();
    const [earn] = setStatus("task-shoes", "Completed");
    expect(earn).toMatchObject({
      entry: "earn",
      trigger: "completed",
      awardKey: "completed:task-shoes",
      amounts: { freedom: 10, xp: 10 },
      subject: { id: "task-shoes", title: "Buy race shoes" },
    });
    expect(earn.amounts.coins).toBeUndefined();
    expect(xp()).toBe(before + 10);
    expect(setStatus("task-shoes", "Completed")).toEqual([]);

    const [reversal] = setStatus("task-shoes", "In progress");
    expect(reversal).toMatchObject({ entry: "reversal", amounts: { freedom: -10, xp: -10 } });
    expect(xp()).toBe(before);

    expect(setStatus("task-shoes", "Completed")).toHaveLength(1);
    expect(xp()).toBe(before + 10);
    expect(
      getState().ledger.filter((e) => e.awardKey === "completed:task-shoes").map((e) => e.entry),
    ).toEqual(["earn", "reversal", "earn"]);
    expect(mockLedger({ limit: 1 })[0]).toMatchObject({ entry: "earn", trigger: "completed" });
  });

  it("pays an item's own bounty, and nothing for one priced at nothing", () => {
    mockSetBounty({ subject: "task-shoes", amounts: { karma: 3, xp: 0 } });
    expect(setStatus("task-shoes", "Completed")[0].amounts).toEqual({ freedom: 10, karma: 3 });

    mockSetBounty({ subject: "task-longrun", nothing: true });
    expect(setStatus("task-longrun", "Completed")).toEqual([]);
  });

  it("pays the timer's Complete", () => {
    const result = applyTimer("task-nutrition", "complete");
    expect(result.awards?.[0]).toMatchObject({ trigger: "completed", amounts: { xp: 10 } });
  });

  it("pays a followed day, half for a partial one, and gives a corrected day back", () => {
    const habit = "habit-stretch";
    const [day] = adhereHabit(habit, "followed", todayIso());
    expect(day).toMatchObject({ trigger: "followed", awardKey: `day:${habit}:${todayIso()}:followed`, amounts: { freedom: 5, xp: 5 } });

    const swap = adhereHabit(habit, "partial", todayIso());
    expect(swap.map((a) => [a.entry, a.trigger, a.amounts])).toEqual([
      ["reversal", "followed", { freedom: -5, xp: -5 }],
      ["earn", "partial", { freedom: 2, xp: 2 }],
    ]);

    expect(adhereHabit(habit, "missed", todayIso()).map((a) => a.entry)).toEqual(["reversal"]);
  });

  it("pays a 7-day streak when a correction joins two runs, and takes it back when undone", () => {
    // Inbox Zero: followed 1–4 days ago, missed 5 days ago, followed 6–7 days ago.
    const fixed = adhereHabit("habit-inbox", "followed", ago(5));
    expect(fixed.map((a) => a.trigger)).toEqual(["followed", "streak_7"]);
    expect(fixed[1]).toMatchObject({ awardKey: `streak_7:habit-inbox:${ago(7)}`, amounts: { xp: 25 } });

    const undone = adhereHabit("habit-inbox", "missed", ago(5));
    expect(undone.map((a) => [a.entry, a.trigger])).toEqual([
      ["reversal", "followed"],
      ["reversal", "streak_7"],
    ]);
  });

  it("never re-pays the seeded mobility streak when today is recorded", () => {
    const awards = adhereHabit("habit-mobility", "followed", todayIso());
    expect(awards.map((a) => a.trigger)).toEqual(["followed"]);
  });

  it("establishing pays and graduates; back to Building gives it back and tracks it again", () => {
    expect(mockHabits().find((h) => h.id === "habit-mobility")?.establishment?.targetMet).toBe(true);
    const [award] = setStatus("habit-mobility", "Established");
    expect(award).toMatchObject({ trigger: "established", amounts: { freedom: 60, gems: 1, xp: 100 } });

    const habits = mockHabits();
    const mobility = habits.find((h) => h.id === "habit-mobility")!;
    expect(mobility.status).toBe("Established");
    expect(mobility.establishment).toBeNull();
    const today = todayHabitRows(habits, getState().occurrences, todayIso()).map((h) => h.habitId);
    expect(today).not.toContain("habit-mobility");
    // It's maintained instead: a logged day pays its day price, and no streak.
    expect(maintainingHabitRows(habits, getState().occurrences, todayIso()).map((h) => h.habitId)).toContain(
      "habit-mobility",
    );
    expect(adhereHabit("habit-mobility", "followed", todayIso())).toEqual([
      expect.objectContaining({ entry: "earn", trigger: "followed", awardKey: `day:habit-mobility:${todayIso()}:followed` }),
    ]);

    expect(setStatus("habit-mobility", "Building")[0]).toMatchObject({ entry: "reversal", trigger: "established" });
    expect(todayHabitRows(mockHabits(), getState().occurrences, todayIso()).map((h) => h.habitId)).toContain(
      "habit-mobility",
    );
  });

  it("reads progress toward the establish target, and a young habit isn't ready", () => {
    const inbox = mockHabits().find((h) => h.id === "habit-inbox")!;
    expect(inbox.status).toBeNull();
    expect(inbox.bounty).toEqual({ freedom: 5, xp: 5 });
    expect(inbox.establishment).toMatchObject({ minAdherence: 0.8, windowDays: 60, targetMet: false });
    expect(inbox.establishment!.ageDays).toBe(40);
  });
});

describe("mockSetBounty (BountyService)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("sets currencies on what the trigger pays now, and shows on the reads", () => {
    const result = mockSetBounty({ subject: "habit-inbox", trigger: "streak_7", amounts: { resolve: 2 } });
    expect(result.lines.find((l) => l.trigger === "streak_7")).toEqual({
      trigger: "streak_7",
      amounts: { freedom: 15, xp: 25, resolve: 2 },
      isCustom: true,
      default: { freedom: 15, xp: 25 },
    });
    const detail = detailWithBounty(getState().details["habit-inbox"]);
    expect(detail.bounty?.find((l) => l.trigger === "streak_7")?.isCustom).toBe(true);

    mockSetBounty({ subject: "task-shoes", amounts: { xp: 30 } });
    expect(withBounty(getState().subjects.find((s) => s.id === "task-shoes")!).bounty).toEqual({
      freedom: 10,
      xp: 30,
    });
    mockSetBounty({ subject: "task-shoes", clear: true });
    expect(withBounty(getState().subjects.find((s) => s.id === "task-shoes")!).bounty).toEqual({
      freedom: 10,
      xp: 10,
    });
  });

  it("refuses what the kernel refuses, in its words", () => {
    expect(() => mockSetBounty({ subject: "habit-inbox", amounts: { xp: 5 } })).toThrow(
      "Say which trigger to price: followed, streak_7, streak_30, streak_100, established.",
    );
    expect(() => mockSetBounty({ subject: "habit-inbox", trigger: "partial", amounts: { xp: 5 } })).toThrow(
      "A partial day always pays half of followed; price followed instead.",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", amounts: { gold: 5 } })).toThrow("Unknown currency 'gold'.");
    expect(() => mockSetBounty({ subject: "task-shoes", amounts: { xp: -1 } })).toThrow(
      "A bounty pays a whole number of Life XP from zero up; got -1.",
    );
    expect(() => mockSetBounty({ subject: "goal-archived", trigger: "followed", amounts: { xp: 1 } })).toThrow(
      "A Goal doesn't pay on 'followed'. Expected: completed.",
    );
    expect(() => mockSetBounty({ subject: "value-steward", amounts: { xp: 1 } })).toThrow(
      "only Task, Project, Goal, Habit earn rewards.",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", nothing: true, clear: true })).toThrow(
      "Send one of amounts, nothing or clear (everyMinutes may come alone, with amounts or with nothing).",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", trigger: "timed", clear: true, everyMinutes: 5 })).toThrow(
      "Send one of amounts, nothing or clear",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", amounts: { xp: 1 }, everyMinutes: 15 })).toThrow(
      "Only timed pays by the minutes on the timer; completed pays once.",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", trigger: "timed", everyMinutes: 0 })).toThrow(
      "Pay for every whole number of minutes on the timer from 1 up; got 0.",
    );
  });

  it("keeps the minutes inside timed: minutes alone keep the price, and clearing timed drops both", () => {
    const timed = () => detailWithBounty(getState().details["task-shoes"]).bounty?.find((l) => l.trigger === "timed");
    const stored = () =>
      (JSON.parse(getState().details["task-shoes"].attributes ?? "{}") as { bounty?: unknown }).bounty;

    expect(timed()).toEqual({ trigger: "timed", amounts: {}, isCustom: false, default: {}, everyMinutes: 1 });
    mockSetBounty({ subject: "task-shoes", trigger: "timed", amounts: { xp: 2 }, everyMinutes: 25 });
    mockSetBounty({ subject: "task-shoes", trigger: "timed", everyMinutes: 45 });
    expect(timed()).toMatchObject({ amounts: { xp: 2 }, isCustom: true, everyMinutes: 45 });
    mockSetBounty({ subject: "task-shoes", amounts: { coins: 3 } });
    expect(stored()).toEqual({ timed: { xp: 2, every_minutes: 45 }, completed: { coins: 3, freedom: 10, xp: 10 } });

    mockSetBounty({ subject: "task-shoes", trigger: "timed", clear: true });
    expect(timed()).toEqual({ trigger: "timed", amounts: {}, isCustom: false, default: {}, everyMinutes: 1 });
    expect(stored()).toEqual({ completed: { coins: 3, freedom: 10, xp: 10 } });
  });
});

describe("time on the timer (the timed reward)", () => {
  beforeEach(() => {
    resetStore();
    vi.useFakeTimers({ toFake: ["Date"] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("pays once per block of minutes on top of completing, clips a running timer, and gives it back on reopen", () => {
    const t0 = Date.now();
    const at = (minutes: number) => vi.setSystemTime(t0 + minutes * 60_000);
    mockSetBounty({ subject: "task-shoes", trigger: "timed", amounts: { xp: 2, coins: 1 }, everyMinutes: 15 });

    applyTimer("task-shoes", "start");
    at(20);
    applyTimer("task-shoes", "pause");
    at(30);
    applyTimer("task-shoes", "resume");
    // 20 + 19 = 39 minutes, still running: completing stops it here. Two whole blocks of 15.
    at(49);
    const done = applyTimer("task-shoes", "complete");
    expect(done.awards?.map((a) => [a.trigger, a.awardKey, a.amounts])).toEqual([
      ["completed", "completed:task-shoes", { freedom: 10, xp: 10 }],
      ["timed", "timed:task-shoes", { coins: 2, xp: 4 }],
    ]);

    at(60);
    expect(setStatus("task-shoes", "In progress").map((a) => [a.entry, a.trigger, a.amounts])).toEqual([
      ["reversal", "completed", { freedom: -10, xp: -10 }],
      ["reversal", "timed", { coins: -2, xp: -4 }],
    ]);

    // Six more minutes, then done again: paid on all 45.
    applyTimer("task-shoes", "start");
    at(66);
    expect(applyTimer("task-shoes", "complete").awards?.find((a) => a.trigger === "timed")?.amounts).toEqual({
      coins: 3,
      xp: 6,
    });
  });

  it("pays nothing for time unless it is priced, and nothing without the timer", () => {
    // task-longrun's seeded timer recorded 25 minutes; its time was never priced.
    expect(setStatus("task-longrun", "Completed").map((a) => a.trigger)).toEqual(["completed"]);
    setStatus("task-longrun", "In progress");
    mockSetBounty({ subject: "task-longrun", trigger: "timed", amounts: { freedom: 1 }, everyMinutes: 10 });
    expect(setStatus("task-longrun", "Completed").find((a) => a.trigger === "timed")?.amounts).toEqual({
      freedom: 2,
    });

    // Priced, but done without the timer.
    mockSetBounty({ subject: "task-shoes", trigger: "timed", amounts: { xp: 5 } });
    expect(setStatus("task-shoes", "Completed").map((a) => a.trigger)).toEqual(["completed"]);
  });
});

describe("mockSetHabitTarget (HabitTargetService)", () => {
  beforeEach(() => {
    resetStore();
  });

  const target = (id: string) => mockHabits().find((h) => h.id === id)!.establishment!;
  const stored = (id: string) =>
    (JSON.parse(getState().details[id]?.attributes ?? "{}") as { establish_target?: unknown }).establish_target;

  it("stores numbers the progress reads, keeps the part left out, and clear puts the default back", () => {
    const set = mockSetHabitTarget({ habit: "habit-inbox", minAdherence: 75 });
    expect(set).toMatchObject({ minAdherence: 0.75, windowDays: 60, adherenceIsCustom: true, windowIsCustom: false });
    expect(set.habit).toMatchObject({ id: "habit-inbox", type: "Habit" });
    expect(stored("habit-inbox")).toEqual({ min_adherence: 0.75 });
    expect(target("habit-inbox")).toMatchObject({ minAdherence: 0.75, windowDays: 60 });

    mockSetHabitTarget({ habit: "habit-inbox", windowDays: 30 });
    expect(stored("habit-inbox")).toEqual({ min_adherence: 0.75, window_days: 30 });
    expect(target("habit-inbox")).toMatchObject({ minAdherence: 0.75, windowDays: 30 });

    const cleared = mockSetHabitTarget({ habit: "habit-inbox", clear: true });
    expect(cleared).toMatchObject({ minAdherence: 0.8, windowDays: 60, adherenceIsCustom: false, windowIsCustom: false });
    expect(stored("habit-inbox")).toBeUndefined();
    expect(target("habit-inbox")).toMatchObject({ minAdherence: 0.8, windowDays: 60 });
  });

  it("refuses what the kernel refuses, in its words, and writes nothing", () => {
    expect(() => mockSetHabitTarget({ habit: "habit-inbox" })).toThrow(
      "Send minAdherence and/or windowDays, or clear — one of them.",
    );
    expect(() => mockSetHabitTarget({ habit: "habit-inbox", windowDays: 30, clear: true })).toThrow("one of them");
    expect(() => mockSetHabitTarget({ habit: "habit-inbox", minAdherence: 0 })).toThrow(
      "An establish target is the share of days followed: above 0 and up to 1 (0.8), or a percentage up to 100 (80); got 0.",
    );
    expect(() => mockSetHabitTarget({ habit: "habit-inbox", minAdherence: 120 })).toThrow("got 120.");
    expect(() => mockSetHabitTarget({ habit: "habit-inbox", windowDays: 0 })).toThrow(
      "An establish window is a whole number of days from 1 to 3650; got 0.",
    );
    expect(() => mockSetHabitTarget({ habit: "habit-inbox", windowDays: 1.5 })).toThrow("got 1.5.");
    expect(() => mockSetHabitTarget({ habit: "task-shoes", windowDays: 30 })).toThrow(
      "is a Task; only a Habit has an establish target.",
    );
    expect(stored("habit-inbox")).toBeUndefined();
  });
});
