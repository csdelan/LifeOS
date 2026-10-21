import { describe, expect, it } from "vitest";
import {
  multiplierLabel,
  nextRankText,
  normalizeDomainDays,
  normalizeEarned,
  progressMessage,
  rankRequirement,
} from "@/lib/domains";
import type { DomainDaysRead, DomainNextRank } from "@/lib/production-ui-types";

const journeyman: DomainNextRank = {
  rank: 2,
  title: "Journeyman",
  level: 20,
  bar: 0.6,
  windowDays: 30,
  consistency: 0.47,
  successfulDays: 14,
  requiredDays: 18,
  daysNeeded: 4,
  daysOld: 120,
  levelReached: true,
  oldEnough: true,
  barMet: false,
};

describe("domain words (xp-domains plan §3.12)", () => {
  it("names a rank's requirement, and Novice's as where every domain starts", () => {
    expect(rankRequirement({ bar: 0.6, windowDays: 30 })).toBe("a successful day on 60% of days over 30 days");
    expect(rankRequirement({ bar: 0, windowDays: 0 })).toBe("where every domain starts");
  });

  it("labels a multiplier to one place", () => {
    expect(multiplierLabel(1.2)).toBe("×1.2");
    expect(multiplierLabel(1)).toBe("×1.0");
  });

  it("says what a capped domain still needs for its next rank", () => {
    expect(nextRankText(journeyman)).toBe("Journeyman needs a successful day on 60% of days over 30 days: 14 of 18 so far");
  });

  it("leads with the level while it is short, and says when the window is met", () => {
    expect(nextRankText({ ...journeyman, levelReached: false, barMet: true })).toBe(
      "Journeyman at level 20, with a successful day on 60% of days over 30 days: met",
    );
  });

  it("says how old a domain younger than the window is", () => {
    expect(nextRankText({ ...journeyman, oldEnough: false, daysOld: 12 })).toBe(
      "Journeyman needs a successful day on 60% of days over 30 days: 14 of 18 so far · 12 of 30 days old",
    );
  });
});

describe("progressMessage (xp-domains plan §3.8)", () => {
  const change = { domain: "health_xp", levelFrom: 11, levelTo: 12, rankFrom: 1, rankTo: 1, rankTitle: "Apprentice" };

  it("says a level-up with the domain's symbol", () => {
    expect(progressMessage(change)).toEqual({ kind: "level", text: "❤️ Health level 12!" });
  });

  it("says a rank-up by its title, whatever the level did", () => {
    expect(progressMessage({ ...change, levelFrom: 19, levelTo: 20, rankTo: 2, rankTitle: "Journeyman" })).toEqual({
      kind: "rank",
      text: "You're now a Health Journeyman.",
    });
  });

  it("says a level lost plainly", () => {
    expect(progressMessage({ ...change, levelFrom: 12, levelTo: 11 })).toEqual({ kind: "lost", text: "Health back to level 11" });
  });
});

describe("normalizing domain reads", () => {
  it("reads an earning answer's int64 fields from strings and tolerates missing parts", () => {
    const earned = normalizeEarned({
      awards: [{ id: "a", entry: "earn", trigger: "rank", awardKey: "rank:health_xp:10", subject: null, amounts: { gems: "1" } }],
      progress: [{ domain: "health_xp", levelFrom: "9", levelTo: "10", rankFrom: "0", rankTo: "1", rankTitle: "Apprentice" }],
    });
    expect(earned.awards[0].amounts).toEqual({ gems: 1 });
    expect(earned.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    expect(normalizeEarned(undefined)).toEqual({ awards: [], progress: [] });
  });

  it("reads a domain's days with a null target and success for an unconfigured domain", () => {
    const raw = {
      key: "social_xp",
      label: "Social XP",
      name: "Social",
      configured: false,
      today: "2026-10-20",
      days: [{ day: "2026-10-20", base: "20", paid: "20" }],
    } as unknown as DomainDaysRead;
    expect(normalizeDomainDays(raw)).toMatchObject({
      target: null,
      days: [{ day: "2026-10-20", base: 20, paid: 20, succeeded: null }],
    });
  });
});
