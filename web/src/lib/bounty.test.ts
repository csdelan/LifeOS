import { describe, expect, it } from "vitest";
import {
  BOUNTY_DEFAULTS,
  bountyDirty,
  bountyJson,
  bountyProblem,
  bountyTriggerTitle,
  bountyWrites,
  linesFor,
  parseBounty,
  parseEveryMinutes,
  priceOf,
  priceTimed,
} from "@/lib/bounty";

describe("bounty pricing (Bounty.Parse / Bounty.Price)", () => {
  it("reads a stored bounty leniently: unknown currencies, fractions and non-objects drop out", () => {
    expect(
      parseBounty({ completed: { xp: 20, gold: 5, coins: 1.5, karma: 3 }, followed: "lots", Streak_7: {} }),
    ).toEqual({ completed: { karma: 3, xp: 20 }, streak_7: {} });
    expect(parseBounty("not an object")).toEqual({});
  });

  it("pays the default for a trigger the bounty doesn't name, and nothing for {}", () => {
    expect(priceOf("Task", "completed", {})).toEqual({ freedom: 10, xp: 10 });
    expect(priceOf("Task", "completed", { completed: {} })).toEqual({});
    expect(priceOf("Task", "completed", { completed: { karma: 3 } })).toEqual({ karma: 3 });
  });

  it("pays no Coins, Karma or Resolve by default: those are awarded per item", () => {
    for (const { lines } of BOUNTY_DEFAULTS) {
      for (const line of lines) {
        expect(Object.keys(line.default)).not.toContain("coins");
        expect(Object.keys(line.default)).not.toContain("karma");
        expect(Object.keys(line.default)).not.toContain("resolve");
      }
    }
  });

  it("prices a partial day at half a followed one, rounded down, dropping what halves to nothing", () => {
    expect(priceOf("Habit", "partial", {})).toEqual({ freedom: 2, xp: 2 });
    expect(priceOf("Habit", "partial", { followed: { xp: 9, karma: 1 } })).toEqual({ xp: 4 });
  });

  it("lists each habit trigger in order with its default", () => {
    const lines = linesFor("Habit", { streak_7: { xp: 40 } });
    expect(lines.map((l) => l.trigger)).toEqual(["followed", "streak_7", "streak_30", "streak_100", "established"]);
    expect(lines[1]).toEqual({
      trigger: "streak_7",
      amounts: { xp: 40 },
      isCustom: true,
      default: { freedom: 15, xp: 25 },
    });
    expect(BOUNTY_DEFAULTS.find((d) => d.type === "Goal")?.lines[0].amounts).toEqual({
      freedom: 60,
      gems: 3,
      xp: 150,
    });
  });

  it("titles triggers for the editor", () => {
    expect(["completed", "followed", "streak_30", "established", "timed"].map(bountyTriggerTitle)).toEqual([
      "When completed",
      "Each day followed",
      "30-day streak",
      "When established",
      "For time on the timer",
    ]);
  });
});

describe("time on the timer (the timed trigger)", () => {
  it("is a Task's second trigger, paying nothing until priced, every minute", () => {
    const lines = linesFor("Task", {});
    expect(lines.map((l) => l.trigger)).toEqual(["completed", "timed"]);
    expect(lines[1]).toEqual({ trigger: "timed", amounts: {}, isCustom: false, default: {}, everyMinutes: 1 });
    expect(lines[0].everyMinutes).toBeUndefined();
  });

  it("keeps its minutes inside timed, as the kernel stores them, and only while timed is priced", () => {
    const raw = bountyJson({ timed: { xp: 2 }, completed: { coins: 1 } }, 15);
    expect(raw).toEqual({ timed: { xp: 2, every_minutes: 15 }, completed: { coins: 1 } });
    expect(parseBounty(raw)).toEqual({ timed: { xp: 2 }, completed: { coins: 1 } });
    expect(parseEveryMinutes(raw)).toBe(15);
    expect(bountyJson({ timed: { xp: 2 } }, 1)).toEqual({ timed: { xp: 2 } });
    expect(parseEveryMinutes({ timed: { every_minutes: 0 } })).toBeNull();
    expect(linesFor("Task", parseBounty(raw), parseEveryMinutes(raw))[1].everyMinutes).toBe(15);
    expect(linesFor("Task", {}, 15)[1].everyMinutes).toBe(1);
  });

  it("pays once per whole block of minutes, rounded down, and nothing under one", () => {
    expect(priceTimed({ xp: 2, coins: 1 }, 39 * 60, 15)).toEqual({ coins: 2, xp: 4 });
    expect(priceTimed({ xp: 1 }, 5 * 60 + 59, 1)).toEqual({ xp: 5 });
    expect(priceTimed({ xp: 1 }, 19 * 60, 30)).toEqual({});
    expect(priceTimed({}, 3600, 1)).toEqual({});
  });

  it("writes the minutes with the price, alone when only they changed, and clears back to the default", () => {
    const task = linesFor("Task", {});
    const priced = linesFor("Task", { timed: { xp: 2 } }, 15);
    const timed = (xp: string, every: string) => ({
      timed: { coins: "", freedom: "", gems: "", karma: "", xp, resolve: "", every_minutes: every },
    });

    expect(bountyWrites("t", task, timed("2", "15"))).toEqual([
      {
        subject: "t",
        trigger: "timed",
        amounts: { coins: 0, freedom: 0, gems: 0, karma: 0, xp: 2, resolve: 0, health_xp: 0, social_xp: 0, environment_xp: 0, finance_xp: 0 },
        everyMinutes: 15,
      },
    ]);
    expect(bountyWrites("t", priced, timed("2", "15"))).toEqual([]);
    expect(bountyWrites("t", priced, timed("2", "30"))[0]).toMatchObject({ trigger: "timed", everyMinutes: 30 });
    // Minutes with nothing to pay keep the minutes; nothing at 1 minute is the default.
    expect(bountyWrites("t", priced, timed("", "30"))).toEqual([
      { subject: "t", trigger: "timed", nothing: true, everyMinutes: 30 },
    ]);
    expect(bountyWrites("t", priced, timed("", ""))).toEqual([{ subject: "t", trigger: "timed", clear: true }]);
    expect(bountyDirty(task, timed("", "1"))).toBe(false);
  });

  it("refuses minutes that aren't a whole number from 1 up", () => {
    const timed = (every: string) => ({ timed: { xp: "1", every_minutes: every } });
    expect(bountyProblem(timed("0"))).toBe("Pay for every whole number of minutes on the timer from 1 up.");
    expect(bountyProblem(timed("2.5"))).toBe("Pay for every whole number of minutes on the timer from 1 up.");
    expect(bountyProblem(timed("15"))).toBeNull();
    expect(bountyProblem(timed(""))).toBeNull();
  });
});

describe("bountyWrites — save only what changed", () => {
  const task = linesFor("Task", {});
  const priced = linesFor("Task", { completed: { karma: 3, xp: 10 } });
  const typed = (coins: string, freedom: string, xp: string, karma = "") => ({
    completed: { coins, freedom, gems: "", karma, xp, resolve: "" },
  });

  it("writes nothing for an untouched or unchanged trigger", () => {
    expect(bountyWrites("t", task, {})).toEqual([]);
    expect(bountyWrites("t", task, typed("", "10", "10"))).toEqual([]);
    expect(bountyDirty(task, typed("0", "10", "10"))).toBe(false);
  });

  it("stores the whole price, zeros included, for a changed trigger", () => {
    expect(bountyWrites("t", task, typed("2", "10", "10", "3"))).toEqual([
      {
        subject: "t",
        trigger: "completed",
        amounts: { coins: 2, freedom: 10, gems: 0, karma: 3, xp: 10, resolve: 0, health_xp: 0, social_xp: 0, environment_xp: 0, finance_xp: 0 },
      },
    ]);
  });

  it("clears a custom trigger edited back to its default, so retuning still reaches it", () => {
    expect(bountyWrites("t", priced, typed("", "10", "10"))).toEqual([
      { subject: "t", trigger: "completed", clear: true },
    ]);
  });

  it("stores nothing for an emptied trigger", () => {
    expect(bountyWrites("t", task, typed("", "0", ""))).toEqual([
      { subject: "t", trigger: "completed", nothing: true },
    ]);
  });

  it("refuses fractions and negatives in the kernel's words", () => {
    expect(bountyProblem(typed("1.5", "", ""))).toBe("A bounty pays a whole number of Coins from zero up.");
    expect(bountyProblem(typed("", "-4", ""))).toBe("A bounty pays a whole number of Freedom from zero up.");
    expect(bountyProblem(typed("0", "", "12"))).toBeNull();
  });
});
