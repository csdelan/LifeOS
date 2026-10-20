import { describe, expect, it } from "vitest";
import {
  BOUNTY_DEFAULTS,
  bountyDirty,
  bountyProblem,
  bountyTriggerTitle,
  bountyWrites,
  linesFor,
  parseBounty,
  priceOf,
} from "@/lib/bounty";

describe("bounty pricing (Bounty.Parse / Bounty.Price)", () => {
  it("reads a stored bounty leniently: unknown currencies, fractions and non-objects drop out", () => {
    expect(
      parseBounty({ completed: { xp: 20, gold: 5, coins: 1.5, karma: 3 }, followed: "lots", Streak_7: {} }),
    ).toEqual({ completed: { karma: 3, xp: 20 }, streak_7: {} });
    expect(parseBounty("not an object")).toEqual({});
  });

  it("pays the default for a trigger the bounty doesn't name, and nothing for {}", () => {
    expect(priceOf("Task", "completed", {})).toEqual({ coins: 2, freedom: 10, xp: 10 });
    expect(priceOf("Task", "completed", { completed: {} })).toEqual({});
    expect(priceOf("Task", "completed", { completed: { karma: 3 } })).toEqual({ karma: 3 });
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
      default: { coins: 5, freedom: 15, xp: 25 },
    });
    expect(BOUNTY_DEFAULTS.find((d) => d.type === "Goal")?.lines[0].amounts).toEqual({
      coins: 25,
      freedom: 60,
      gems: 3,
      xp: 150,
    });
  });

  it("titles triggers for the editor", () => {
    expect(["completed", "followed", "streak_30", "established"].map(bountyTriggerTitle)).toEqual([
      "When completed",
      "Each day followed",
      "30-day streak",
      "When established",
    ]);
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
    expect(bountyWrites("t", task, typed("2", "10", "10"))).toEqual([]);
    expect(bountyDirty(task, typed("2", "10", "10"))).toBe(false);
  });

  it("stores the whole price, zeros included, for a changed trigger", () => {
    expect(bountyWrites("t", task, typed("2", "10", "10", "3"))).toEqual([
      {
        subject: "t",
        trigger: "completed",
        amounts: { coins: 2, freedom: 10, gems: 0, karma: 3, xp: 10, resolve: 0 },
      },
    ]);
  });

  it("clears a custom trigger edited back to its default, so retuning still reaches it", () => {
    expect(bountyWrites("t", priced, typed("2", "10", "10"))).toEqual([
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
