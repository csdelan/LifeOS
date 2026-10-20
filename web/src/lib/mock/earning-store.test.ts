import { beforeEach, describe, expect, it } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import { todayHabitRows } from "@/lib/derive";
import {
  adhereHabit,
  applyTimer,
  detailWithBounty,
  getState,
  mockHabits,
  mockLedger,
  mockSetBounty,
  mockWallet,
  resetStore,
  setStatus,
  withBounty,
} from "@/lib/mock/store";
import { balanceOf } from "@/lib/rewards";

const ago = (n: number) => addDays(todayIso(), -n);
const coins = () => balanceOf(mockWallet(), "coins");

describe("mock earning (RewardService.ReconcileAsync, scoped to the write)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("pays a Task's default on completion, gives it back on reopen, and pays once on re-completion", () => {
    const before = coins();
    const [earn] = setStatus("task-shoes", "Completed");
    expect(earn).toMatchObject({
      entry: "earn",
      trigger: "completed",
      awardKey: "completed:task-shoes",
      amounts: { coins: 2, freedom: 10, xp: 10 },
      subject: { id: "task-shoes", title: "Buy race shoes" },
    });
    expect(coins()).toBe(before + 2);
    expect(setStatus("task-shoes", "Completed")).toEqual([]);

    const [reversal] = setStatus("task-shoes", "In progress");
    expect(reversal).toMatchObject({ entry: "reversal", amounts: { coins: -2, freedom: -10, xp: -10 } });
    expect(coins()).toBe(before);

    expect(setStatus("task-shoes", "Completed")).toHaveLength(1);
    expect(coins()).toBe(before + 2);
    expect(
      getState().ledger.filter((e) => e.awardKey === "completed:task-shoes").map((e) => e.entry),
    ).toEqual(["earn", "reversal", "earn"]);
    expect(mockLedger({ limit: 1 })[0]).toMatchObject({ entry: "earn", trigger: "completed" });
  });

  it("pays an item's own bounty, and nothing for one priced at nothing", () => {
    mockSetBounty({ subject: "task-shoes", amounts: { karma: 3, xp: 0 } });
    expect(setStatus("task-shoes", "Completed")[0].amounts).toEqual({ coins: 2, freedom: 10, karma: 3 });

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
    expect(day).toMatchObject({ trigger: "followed", awardKey: `day:${habit}:${todayIso()}:followed`, amounts: { coins: 1, freedom: 5, xp: 5 } });

    const swap = adhereHabit(habit, "partial", todayIso());
    expect(swap.map((a) => [a.entry, a.trigger, a.amounts])).toEqual([
      ["reversal", "followed", { coins: -1, freedom: -5, xp: -5 }],
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
    expect(award).toMatchObject({ trigger: "established", amounts: { coins: 25, freedom: 60, gems: 1, xp: 100 } });

    const habits = mockHabits();
    const mobility = habits.find((h) => h.id === "habit-mobility")!;
    expect(mobility.status).toBe("Established");
    expect(mobility.establishment).toBeNull();
    const today = todayHabitRows(habits, getState().occurrences, todayIso()).map((h) => h.habitId);
    expect(today).not.toContain("habit-mobility");
    // A graduated habit's days don't pay.
    expect(adhereHabit("habit-mobility", "followed", todayIso())).toEqual([]);

    expect(setStatus("habit-mobility", "Building")[0]).toMatchObject({ entry: "reversal", trigger: "established" });
    expect(todayHabitRows(mockHabits(), getState().occurrences, todayIso()).map((h) => h.habitId)).toContain(
      "habit-mobility",
    );
  });

  it("reads progress toward the establish target, and a young habit isn't ready", () => {
    const inbox = mockHabits().find((h) => h.id === "habit-inbox")!;
    expect(inbox.status).toBeNull();
    expect(inbox.bounty).toEqual({ coins: 1, freedom: 5, xp: 5 });
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
      amounts: { coins: 5, freedom: 15, xp: 25, resolve: 2 },
      isCustom: true,
      default: { coins: 5, freedom: 15, xp: 25 },
    });
    const detail = detailWithBounty(getState().details["habit-inbox"]);
    expect(detail.bounty?.find((l) => l.trigger === "streak_7")?.isCustom).toBe(true);

    mockSetBounty({ subject: "task-shoes", amounts: { xp: 30 } });
    expect(withBounty(getState().subjects.find((s) => s.id === "task-shoes")!).bounty).toEqual({
      coins: 2,
      freedom: 10,
      xp: 30,
    });
    mockSetBounty({ subject: "task-shoes", clear: true });
    expect(withBounty(getState().subjects.find((s) => s.id === "task-shoes")!).bounty).toEqual({
      coins: 2,
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
      "A bounty pays a whole number of XP from zero up; got -1.",
    );
    expect(() => mockSetBounty({ subject: "goal-archived", trigger: "followed", amounts: { xp: 1 } })).toThrow(
      "A Goal doesn't pay on 'followed'. Expected: completed.",
    );
    expect(() => mockSetBounty({ subject: "value-steward", amounts: { xp: 1 } })).toThrow(
      "only Task, Project, Goal, Habit earn rewards.",
    );
    expect(() => mockSetBounty({ subject: "task-shoes", nothing: true, clear: true })).toThrow(
      "Send one of amounts, nothing or clear.",
    );
  });
});
