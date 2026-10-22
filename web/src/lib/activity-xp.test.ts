import { describe, expect, it } from "vitest";
import { NOTHING_PAID } from "@/lib/activity";
import {
  dayPartXp,
  dayXp,
  dayXpWords,
  ouraDayAt,
  ouraDayOver,
  paidBetween,
  paidFor,
  partXpText,
  partXpWords,
} from "@/lib/activity-xp";
import { formatDayFull } from "@/lib/dates";
import type { ActivityDayRow, ActivityPaid } from "@/lib/production-ui-types";

/** October 2026, local time, as the page reads the clock. */
const at = (day: number, hour: number, minute = 0) => new Date(2026, 9, day, hour, minute);

type XpRow = Pick<ActivityDayRow, "day" | "steps" | "sleepScore" | "paid">;

function row(day: string, patch: Partial<XpRow> = {}): XpRow {
  return { day, steps: null, sleepScore: null, paid: { ...NOTHING_PAID }, ...patch };
}

/** A workout on Oct `day` from `hour` for `minutes`. */
function workout(day: number, hour: number, minutes: number, counted = true) {
  const start = at(day, hour);
  return {
    day: `2026-10-${day}`,
    counted,
    startedAt: start.toISOString(),
    endedAt: new Date(start.getTime() + minutes * 60_000).toISOString(),
  };
}

const paid = (patch: Partial<ActivityPaid>): ActivityPaid => {
  const p = { ...NOTHING_PAID, ...patch };
  p.total = (p.steps?.paid ?? 0) + (p.exercise?.paid ?? 0) + (p.sleep?.paid ?? 0);
  p.baseTotal = (p.steps?.base ?? 0) + (p.exercise?.base ?? 0) + (p.sleep?.base ?? 0);
  return p;
};

describe("Oura's day (activity plan §3.8)", () => {
  it("ends at 4 a.m. the next morning", () => {
    expect(ouraDayAt(at(23, 3, 59))).toBe("2026-10-22");
    expect(ouraDayAt(at(23, 4))).toBe("2026-10-23");
    expect(ouraDayOver("2026-10-22", at(23, 3, 59))).toBe(false);
    expect(ouraDayOver("2026-10-22", at(23, 4))).toBe(true);
  });
});

describe("a day's activity XP (A9)", () => {
  it("projects today's parts until they pay, never as earned, from the day's own counted workouts", () => {
    const xp = dayXp(
      row("2026-10-23", { steps: 6_000, sleepScore: 82 }),
      [workout(23, 7, 30), workout(23, 12, 60, false), workout(22, 7, 60)],
      at(23, 18),
    );
    expect(xp.parts.map((p) => [p.part, p.state, p.xp, p.why])).toEqual([
      ["steps", "projected", 15, "Pays when the day ends, at 4 a.m."],
      ["exercise", "projected", 25, "Pays when the day ends, at 4 a.m."],
      ["sleep", "projected", 17, "Pays at the next sync"],
    ]);
    expect([xp.paid, xp.projected, xp.total]).toEqual([0, 57, 57]);
    expect(xp.parts.map(partXpText)).toEqual(["15 projected", "25 projected", "17 projected"]);
    expect(dayXpWords(xp)).toBe("57 Health XP projected");
  });

  it("says a day that's over pays at the next sync", () => {
    const xp = dayXp(row("2026-10-22", { steps: 6_000 }), [workout(22, 7, 20)], at(23, 5));
    expect(xp.parts.slice(0, 2).map((p) => [p.state, p.xp, p.why])).toEqual([
      ["projected", 15, "Pays at the next sync"],
      ["projected", 15, "Pays at the next sync"],
    ]);
  });

  it("shows what the ledger paid, multiplier included, beside what's still projected", () => {
    const xp = dayXp(
      row("2026-10-23", {
        steps: 8_000,
        sleepScore: 80,
        paid: paid({ steps: { base: 20, paid: 20, multiplier: null }, sleep: { base: 15, paid: 20, multiplier: 1.3 } }),
      }),
      [workout(23, 7, 45)],
      at(24, 5),
    );
    expect(xp.parts.map((p) => [p.state, p.xp, p.base, p.multiplier])).toEqual([
      ["paid", 20, 20, null],
      ["projected", 35, 35, null],
      ["paid", 20, 15, 1.3],
    ]);
    expect([xp.paid, xp.projected, xp.total]).toEqual([40, 35, 75]);
    expect(xp.parts.map(partXpText)).toEqual(["20", "35 projected", "20 (15 ×1.3)"]);
    expect(xp.parts.map(partXpWords)).toEqual([
      "Steps: 20 Health XP earned",
      "Exercise: 35 Health XP projected, pays at the next sync",
      "Sleep: 20 Health XP earned (15 base ×1.3)",
    ]);
    expect(dayXpWords(xp)).toBe("75 Health XP: 40 earned, 35 projected");

    const all = dayXp(row("2026-10-22", { sleepScore: 80, paid: paid({ sleep: { base: 15, paid: 15, multiplier: null } }) }), [], at(24, 5));
    expect(dayXpWords(all)).toBe("15 Health XP earned");
  });

  it("says why a part pays nothing, and never shows it as 0 earned", () => {
    const quiet = dayXp(
      row("2026-10-23", { steps: 0, sleepScore: 50 }),
      [workout(23, 7, 8), workout(23, 9, 30, false)],
      at(23, 18),
    );
    expect(quiet.parts.map((p) => [p.state, p.xp, p.why])).toEqual([
      ["none", 0, "No steps yet"],
      ["none", 0, "Pays from 10 minutes"],
      ["none", 0, "Pays above a sleep score of 50"],
    ]);
    expect(quiet.parts.map(partXpText)).toEqual(["—", "—", "—"]);
    expect(partXpWords(quiet.parts[1])).toBe("Exercise: nothing to pay (pays from 10 minutes)");
    expect(dayXpWords(quiet)).toBe("No Health XP yet");

    const empty = dayXp(row("2026-10-23"), [], at(23, 18));
    expect(empty.parts.map((p) => p.why)).toEqual(["No steps from Oura", "No exercise recorded", "No sleep score"]);
  });

  it("pays nothing on a day before activity XP started", () => {
    const xp = dayXp(row("2026-10-21", { steps: 9_000, sleepScore: 90 }), [workout(21, 7, 60)], at(23, 18));
    expect(xp.pays).toBe(false);
    expect([xp.paid, xp.projected, xp.total]).toEqual([0, 0, 0]);
    expect(xp.parts.every((p) => p.state === "none")).toBe(true);
    expect(dayXpWords(xp)).toBe(`Activity XP starts on ${formatDayFull("2026-10-22")}`);
  });

  it("answers one part for the steps and sleep tables", () => {
    const day = row("2026-10-23", { steps: 6_000, sleepScore: 75 });
    expect(dayPartXp(day, "steps", at(23, 18))).toMatchObject({ state: "projected", xp: 15 });
    expect(dayPartXp(day, "sleep", at(23, 18))).toMatchObject({ state: "projected", xp: 13 });
  });
});

describe("paid totals", () => {
  it("adds what the days paid for a part, after the multiplier, and only between the dates asked", () => {
    const rows = [
      row("2026-10-22", { paid: paid({ steps: { base: 20, paid: 24, multiplier: 1.2 } }) }),
      row("2026-10-23", { paid: paid({ steps: { base: 12, paid: 12, multiplier: null }, sleep: { base: 10, paid: 10, multiplier: null } }) }),
      row("2026-10-24"),
    ];
    expect(paidFor(rows, "steps")).toBe(36);
    expect(paidFor(rows, "sleep")).toBe(10);
    expect(paidFor(rows, "exercise")).toBe(0);
    expect(paidBetween(rows, "steps", "2026-10-23", "2026-10-29")).toBe(12);
  });
});
