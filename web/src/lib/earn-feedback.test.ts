import { afterEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { celebrate, chime } from "@/lib/celebrate";
import { announceAwards, announceEarned, rescoreMessage, splitRescores, summarizeAwards } from "@/lib/earn-feedback";
import type { Award, DomainChange } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

vi.mock("@/lib/celebrate", () => ({
  celebrate: vi.fn(() => ({ confetti: true, sound: true })),
  chime: vi.fn(() => true),
}));

afterEach(() => {
  vi.clearAllMocks();
});

const subject = (title: string) => ({ id: title, urn: `urn:bsk:x:${title}`, type: "Habit" as const, title });

function award(trigger: string, amounts: Award["amounts"], entry: Award["entry"] = "earn", title = "Morning mobility"): Award {
  return { id: `${trigger}-${title}`, entry, trigger, awardKey: `${trigger}:${title}`, subject: subject(title), amounts };
}

describe("summarizeAwards", () => {
  it("sums what was earned and given back, item by item", () => {
    const summary = summarizeAwards([
      award("followed", { coins: 1, freedom: 5, xp: 5 }),
      award("streak_7", { coins: 5, freedom: 15, xp: 25 }),
      award("completed", { coins: -2, xp: -10 }, "reversal", "Buy race shoes"),
    ]);
    expect(summary.earned).toEqual({ coins: 6, freedom: 20, xp: 30 });
    expect(summary.givenBack).toEqual({ coins: -2, xp: -10 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed, 7-day streak");
    expect(summary.givenBackFor).toBe("Buy race shoes — completed");
    expect(summary.big).toBe(true);
  });

  it("names time on the timer beside completing", () => {
    const summary = summarizeAwards([
      award("completed", { freedom: 10, xp: 10 }, "earn", "Write the report"),
      award("timed", { xp: 4 }, "earn", "Write the report"),
    ]);
    expect(summary.earned).toEqual({ freedom: 10, xp: 14 });
    expect(summary.earnedFor).toBe("Write the report — completed, time on the timer");
    expect(summary.big).toBe(false);
  });

  it("names a rank-up by its note, since it belongs to no item, and celebrates it", () => {
    const rankUp: Award = {
      id: "rank",
      entry: "earn",
      trigger: "rank",
      awardKey: "rank:health_xp:10",
      subject: null,
      note: "Health Apprentice",
      amounts: { gems: 1 },
    };
    const summary = summarizeAwards([award("followed", { health_xp: 70 }), rankUp]);
    expect(summary.earned).toEqual({ health_xp: 70, gems: 1 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed · Health Apprentice — rank-up");
    expect(summary.big).toBe(true);
  });

  it("shows how domain XP paid at a multiplier was worked out", () => {
    const multiplied: Award = {
      ...award("followed", { freedom: 5, health_xp: 78 }),
      base: { health_xp: 60 },
      multipliers: { health_xp: 1.3 },
    };
    const summary = summarizeAwards([multiplied, award("completed", { health_xp: 25 }, "earn", "Stretch")]);
    expect(summary.earned).toEqual({ freedom: 5, health_xp: 103 });
    expect(summary.earnedFor).toBe("Morning mobility — day followed (❤️ 60 × 1.3) · Stretch — completed");
  });

  it("is big for Gems, a streak milestone or establishing — not an ordinary day", () => {
    expect(summarizeAwards([award("followed", { xp: 5 })]).big).toBe(false);
    expect(summarizeAwards([award("completed", { gems: 1 })]).big).toBe(true);
    expect(summarizeAwards([award("established", { xp: 100 })]).big).toBe(true);
    expect(summarizeAwards([award("streak_7", { xp: -25 }, "reversal")]).big).toBe(false);
  });
});

describe("announceAwards", () => {
  it("toasts an earn and chimes", () => {
    announceAwards([award("completed", { coins: 2, freedom: 10, xp: 10 }, "earn", "Buy race shoes")]);
    expect(toast).toHaveBeenCalledWith("Earned +2 Coins +10 Freedom +10 Life XP", {
      icon: "🪙",
      description: "Buy race shoes — completed",
    });
    expect(chime).toHaveBeenCalledTimes(1);
    expect(celebrate).not.toHaveBeenCalled();
  });

  it("celebrates the big ones instead of chiming", () => {
    announceAwards([award("established", { coins: 25, gems: 1, xp: 100 })]);
    expect(celebrate).toHaveBeenCalledTimes(1);
    expect(chime).not.toHaveBeenCalled();
  });

  it("gives back quietly", () => {
    announceAwards([award("completed", { coins: -2 }, "reversal", "Buy race shoes")]);
    expect(toast).toHaveBeenCalledWith("Gave back −2 Coins", {
      icon: "↩",
      description: "Buy race shoes — completed",
    });
    expect(chime).not.toHaveBeenCalled();
    expect(celebrate).not.toHaveBeenCalled();
  });

  it("says nothing when nothing was paid", () => {
    announceAwards([]);
    announceAwards(undefined);
    expect(toast).not.toHaveBeenCalled();
  });
});

describe("food awards (food-log plan §3.3)", () => {
  const food = (entry: Award["entry"], trigger: string, health_xp: number, extra?: Partial<Award>): Award => ({
    id: `${entry}-${trigger}-${health_xp}`,
    entry,
    trigger,
    awardKey: `${trigger}:2026-10-20`,
    subject: null,
    note: trigger === "fast" ? "Fast ending Tue Oct 20 (23 h 00 m)" : "Food day Tue Oct 20",
    amounts: { health_xp },
    ...extra,
  });

  it("toasts a day's diet XP with a plate, naming the day and its working", () => {
    announceAwards([
      food("earn", "fast", 8, { base: { health_xp: 7 }, multipliers: { health_xp: 1.2 } }),
      food("earn", "diet", 38, { base: { health_xp: 32 }, multipliers: { health_xp: 1.2 } }),
    ]);
    expect(toast).toHaveBeenCalledWith("Earned +46 Health XP", {
      icon: "🍽️",
      description:
        "Fast ending Tue Oct 20 (23 h 00 m) — fasting (❤️ 7 × 1.2) · Food day Tue Oct 20 — diet score (❤️ 32 × 1.2)",
    });
    expect(chime).toHaveBeenCalledTimes(1);
  });

  it("shows a re-scored day as one change, not a give-back and an earn", () => {
    const { rescores, rest } = splitRescores([food("reversal", "diet", -38), food("earn", "diet", 35)]);
    expect(rest).toEqual([]);
    expect(rescores.map(rescoreMessage)).toEqual(["Food day Tue Oct 20 re-scored: 38 → 35 Health XP"]);

    announceAwards([food("reversal", "diet", -38), food("earn", "diet", 35)]);
    expect(toast).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith("Food day Tue Oct 20 re-scored: 38 → 35 Health XP", { icon: "🍽️" });
    // Scored lower: quiet. Scored higher: a chime.
    expect(chime).not.toHaveBeenCalled();
    announceAwards([food("reversal", "diet", -35), food("earn", "diet", 38)]);
    expect(chime).toHaveBeenCalledTimes(1);
  });

  it("still gives back quietly when a day no longer pays", () => {
    announceAwards([food("reversal", "diet", -38)]);
    expect(toast).toHaveBeenCalledWith("Gave back −38 Health XP", {
      icon: "↩",
      description: "Food day Tue Oct 20 — diet score",
    });
  });
});

describe("activity awards (activity plan §3.8, A9)", () => {
  const NOTES: Record<string, string> = { steps: "Steps", exercise: "Exercise", sleep: "Sleep" };
  const activity = (entry: Award["entry"], trigger: string, health_xp: number, extra?: Partial<Award>): Award => ({
    id: `${entry}-${trigger}-${health_xp}`,
    entry,
    trigger,
    awardKey: `${trigger}:2026-10-22`,
    subject: null,
    note: `${NOTES[trigger]} Thu Oct 22`,
    amounts: { health_xp },
    ...extra,
  });

  it("names each part by its note alone, with the working at a multiplier, and a shoe", () => {
    announceAwards([
      activity("earn", "steps", 20),
      activity("earn", "exercise", 33, { base: { health_xp: 25 }, multipliers: { health_xp: 1.3 } }),
    ]);
    expect(toast).toHaveBeenCalledWith("Earned +53 Health XP", {
      icon: "👟",
      description: "Steps Thu Oct 22 · Exercise Thu Oct 22 — ❤️ 25 × 1.3",
    });
    expect(chime).toHaveBeenCalledTimes(1);
  });

  it("gives sleep alone a moon, and a mix of food and activity a coin", () => {
    announceAwards([activity("earn", "sleep", 15)]);
    expect(toast).toHaveBeenLastCalledWith("Earned +15 Health XP", { icon: "🌙", description: "Sleep Thu Oct 22" });

    announceAwards([
      activity("earn", "sleep", 15),
      { ...activity("earn", "diet", 30), note: "Food day Thu Oct 22", awardKey: "diet:2026-10-22" },
    ]);
    expect(toast).toHaveBeenLastCalledWith("Earned +45 Health XP", {
      icon: "🪙",
      description: "Sleep Thu Oct 22 · Food day Thu Oct 22 — diet score",
    });
  });

  it("shows a day Oura revised as one change, with its part's icon", () => {
    announceAwards([activity("reversal", "steps", -20), activity("earn", "steps", 25)]);
    expect(toast).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith("Steps Thu Oct 22 re-scored: 20 → 25 Health XP", { icon: "👟" });

    announceAwards([activity("reversal", "sleep", -15), activity("earn", "sleep", 12)]);
    expect(toast).toHaveBeenLastCalledWith("Sleep Thu Oct 22 re-scored: 15 → 12 Health XP", { icon: "🌙" });
  });

  it("gives back by the part's note alone", () => {
    announceAwards([activity("reversal", "exercise", -15)]);
    expect(toast).toHaveBeenCalledWith("Gave back −15 Health XP", { icon: "↩", description: "Exercise Thu Oct 22" });
  });
});

describe("announceEarned (xp-domains plan §7.4.7)", () => {
  /** A change whose bests moved with the level unless given. */
  const change = (
    levelFrom: number,
    levelTo: number,
    rankFrom = 0,
    rankTo = rankFrom,
    rankTitle = "Novice",
    best: Partial<DomainChange> = {},
  ): DomainChange => ({
    domain: "health_xp",
    levelFrom,
    levelTo,
    rankFrom,
    rankTo,
    rankTitle,
    bestLevelFrom: levelFrom,
    bestLevelTo: Math.max(levelFrom, levelTo),
    bestRankFrom: rankFrom,
    bestRankTo: Math.max(rankFrom, rankTo),
    ...best,
  });
  const rankUp: Award = {
    id: "rank",
    entry: "earn",
    trigger: "rank",
    awardKey: "rank:health_xp:10",
    subject: null,
    note: "Health Apprentice",
    amounts: { gems: 1 },
  };

  afterEach(() => {
    localStorage.clear();
  });

  it("toasts a new best level and gives it the full celebration", () => {
    announceEarned({ awards: [award("followed", { health_xp: 90 })], progress: [change(11, 12, 1)] });
    expect(toast).toHaveBeenCalledWith("❤️ Health level 12!", { icon: "⬆️", description: "A new best level." });
    expect(celebrate).toHaveBeenCalledTimes(1);
    expect(celebrate).toHaveBeenCalledWith();
    expect(chime).not.toHaveBeenCalled();
  });

  it("names a new best rank's title and celebrates it bigger, once, over the Gem's own celebration", () => {
    announceEarned({
      awards: [award("followed", { health_xp: 108 }), rankUp],
      progress: [change(9, 10, 0, 1, "Apprentice")],
    });
    expect(toast).toHaveBeenCalledWith("You're now a Health Apprentice.", {
      icon: "🏅",
      description: "A new best rank: see it on your Character page.",
    });
    expect(toast).toHaveBeenCalledTimes(2);
    expect(celebrate).toHaveBeenCalledTimes(1);
    expect(celebrate).toHaveBeenCalledWith("grand");
    expect(chime).not.toHaveBeenCalled();
  });

  it("says a rank-up alone, as a settle on opening the Character page pays it", () => {
    announceEarned({ awards: [rankUp], progress: [change(19, 20, 1, 2, "Journeyman")] });
    expect(toast).toHaveBeenCalledWith("You're now a Health Journeyman.", expect.anything());
    expect(celebrate).toHaveBeenCalledWith("grand");
  });

  it("keeps every best it announces, so the Character page doesn't celebrate it again", () => {
    announceEarned({ awards: [rankUp], progress: [change(19, 20, 1, 2, "Journeyman")] });
    expect(JSON.parse(localStorage.getItem("lifeos.domains.best")!)).toEqual({ health_xp: { level: 20, rank: 2 } });
  });

  it("notes getting back to a level below the best quietly", () => {
    announceEarned({
      awards: [award("followed", { health_xp: 60 })],
      progress: [change(21, 22, 2, 2, "Journeyman", { bestLevelFrom: 30, bestLevelTo: 30, bestRankFrom: 3, bestRankTo: 3 })],
    });
    expect(toast).toHaveBeenCalledWith("Health back to level 22", { icon: "↗" });
    expect(celebrate).not.toHaveBeenCalled();
    expect(chime).toHaveBeenCalledTimes(1);
  });

  it("is quiet about a level a reversal took back, and keeps the best", () => {
    announceEarned({
      awards: [award("followed", { health_xp: -108 }, "reversal")],
      progress: [change(10, 9, 1, 0, "Novice", { bestLevelTo: 10, bestRankTo: 1 })],
    });
    expect(toast).toHaveBeenCalledWith("Health down to level 9", {
      icon: "↩",
      description: "A reversal took back the XP that level needed.",
    });
    expect(celebrate).not.toHaveBeenCalled();
    expect(chime).not.toHaveBeenCalled();
  });

  it("says why a level was lost when the write says so, else that the last 90 days support less", () => {
    const lost = change(23, 22, 2, 2, "Journeyman", { bestLevelTo: 23 });
    announceEarned({ awards: [], progress: [lost] }, { lostBecause: "Without that day, your last 90 days support a lower level." });
    expect(toast).toHaveBeenLastCalledWith("Health down to level 22", {
      icon: "↩",
      description: "Without that day, your last 90 days support a lower level.",
    });
    announceEarned({ awards: [], progress: [lost] });
    expect(toast).toHaveBeenLastCalledWith("Health down to level 22", {
      icon: "↩",
      description: "Your last 90 days support a lower level now.",
    });
  });

  it("chimes an ordinary earn that moved nothing", () => {
    announceEarned({ awards: [award("followed", { health_xp: 90 })], progress: [] });
    expect(toast).toHaveBeenCalledTimes(1);
    expect(chime).toHaveBeenCalledTimes(1);
    expect(celebrate).not.toHaveBeenCalled();
  });

  it("says nothing for an empty answer", () => {
    announceEarned({ awards: [], progress: [] });
    announceEarned(undefined);
    expect(toast).not.toHaveBeenCalled();
    expect(chime).not.toHaveBeenCalled();
  });
});
