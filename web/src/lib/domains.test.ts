import { describe, expect, it } from "vitest";
import {
  bestText,
  exceptionMarkedMessage,
  exceptionUnmarkedMessage,
  fallHistoryText,
  fallSpeedText,
  multiplierChipText,
  multiplierLabel,
  needParts,
  needText,
  normalizeDomainDays,
  normalizeDomainReport,
  normalizeDomainRow,
  normalizeEarned,
  normalizeExceptionDays,
  normalizeExceptionResult,
  periodLabel,
  progressMessage,
  rankHoldText,
  shortDay,
  stateLine,
  todayText,
} from "@/lib/domains";
import type {
  DomainConsistencyNeed,
  DomainCurrent,
  DomainDaysRead,
  DomainReportRead,
  DomainRow,
} from "@/lib/production-ui-types";

const need: DomainConsistencyNeed = {
  level: 48,
  windowDays: 90,
  average: 49,
  averageNow: 47,
  averageMet: false,
  activeMinimum: 20,
  activeDaysNeeded: 62,
  activeDays: 60,
  activeDaysMet: false,
  ageNeeded: 49,
  age: 120,
  ageMet: true,
};

const current: DomainCurrent = {
  todayBase: 30,
  todayAction: 12,
  activeMinimum: 17,
  todayActive: false,
  todayException: false,
  rule: "at least 17 action XP (base XP less sleep)",
  activeShare: 0.8,
  activeDays: 24,
  windowDays: 30,
  multiplier: 1.2,
  nextTier: { minShare: 0.85, multiplier: 1.3, daysNeeded: 2 },
  settledThrough: "2026-10-24",
};

/** A level 47 Expert whose last 90 days support 47 and lifetime XP 52. */
function row(shown: Partial<NonNullable<DomainRow["shown"]>> = {}, more: Partial<DomainRow> = {}): DomainRow {
  return {
    key: "health_xp",
    label: "Health XP",
    name: "Health",
    totalXp: 60_000,
    configured: true,
    lifetime: { start: "2025-01-01", level: 52, levelXp: 2_400, xpIntoLevel: 1_160, xpToNext: 1_240 },
    consistency: { level: 47, windowDays: 90, from: "2026-07-27", to: "2026-10-24", averageBase: 47.3, next: need },
    shown: {
      level: 47,
      exact: 47,
      target: 47,
      rank: 4,
      rankTitle: "Expert",
      limitedBy: "consistency",
      falling: false,
      paused: false,
      fallingSince: null,
      fallingFrom: null,
      percentPerDay: 1,
      lastChange: null,
      backOnTrack: null,
      ...shown,
    },
    best: { level: 52, levelOn: "2026-06-01", rank: 5, rankTitle: "Veteran", rankOn: "2026-05-20" },
    current,
    ranks: null,
    ...more,
  };
}

describe("domain words (xp-domains plan §7.4.9)", () => {
  it("labels a multiplier to one place and a period by its length", () => {
    expect([multiplierLabel(1.2), multiplierLabel(1)]).toEqual(["×1.2", "×1.0"]);
    expect([7, 30, 90, 180, 365].map(periodLabel)).toEqual(["7 days", "30 days", "3 months", "6 months", "12 months"]);
  });

  it("names the best level and rank", () => {
    expect(bestText({ level: 52, rankTitle: "Veteran" })).toBe("Best: level 52, Veteran");
  });

  it("says how fast a 1% fall moves", () => {
    expect(fallSpeedText(50, 1)).toBe("about half a level a day");
    expect(fallSpeedText(97, 1)).toBe("about a level a day");
    expect(fallSpeedText(250, 1)).toBe("about 3 levels a day");
    expect(fallSpeedText(10, 1)).toBe("about a tenth of a level a day");
  });

  it("says which level sets the shown one when nothing is falling", () => {
    expect(stateLine(row())).toBe("Your last 90 days support level 47.");
    expect(stateLine(row({ limitedBy: "lifetime" }))).toBe("Lifetime XP sets your level: 1,240 XP to level 53.");
    const young = row({}, { consistency: { level: 1, windowDays: 0, from: null, to: null, averageBase: 0, next: null } });
    expect(stateLine(young)).toBe("Your level starts counting once a day settles.");
  });

  it("says a falling level's target, its speed and what pauses it", () => {
    const falling = row({
      level: 44,
      exact: 43.27,
      target: 41,
      rank: 4,
      falling: true,
      fallingSince: "2026-10-20",
      fallingFrom: 47,
      backOnTrack: { days: 7, average: 46, activeMinimum: 18, activeDays: 5 },
    });
    expect(stateLine(falling)).toBe(
      "Falling 1% a day toward 41 (about half a level a day). 7 days back on track pauses it: 46+ base XP a day on average, with 5 active.",
    );
    expect(fallHistoryText(falling)).toBe(`Down from 47 on ${shortDay("2026-10-20")} · falling 1% a day toward 41`);
  });

  it("says why a fall is paused", () => {
    const paused = row({ level: 44, exact: 43.27, target: 41, paused: true, fallingSince: "2026-10-20", fallingFrom: 47 });
    expect(stateLine(paused)).toBe("Paused: your last 7 days are back on track.");
    expect(fallHistoryText(paused)).toBe(`Down from 47 on ${shortDay("2026-10-20")}`);
    const exception = { ...paused, current: { ...current, todayException: true } };
    expect(stateLine(exception)).toBe("Paused: today is an exception day, so the fall toward 41 waits.");
    expect(fallHistoryText(row())).toBeNull();
  });

  it("lists what the next consistency level needs, with the age test only while it isn't met", () => {
    expect(needParts(need)).toEqual([
      { key: "average", text: "a 90-day average of 49 base XP (you're at 47)", met: false },
      { key: "active", text: "62 active days of 90 (you have 60)", met: false },
    ]);
    expect(needText(need)).toBe(
      "Level 48 needs a 90-day average of 49 base XP (you're at 47) and 62 active days of 90 (you have 60).",
    );
    const young = { ...need, windowDays: 30, ageMet: false, age: 30 };
    expect(needParts(young).map((p) => p.key)).toEqual(["average", "active", "age"]);
    expect(needParts(young)[2].text).toBe("49 active days so far (you have 30)");
    expect(needText({ ...need, windowDays: 0 })).toBe("Level 48 needs days to settle first.");
  });

  it("says today against the active minimum, and an exception day plainly", () => {
    expect(todayText(current)).toBe("Today: 12 of 17 action XP to count as active");
    expect(todayText({ ...current, todayAction: 20, todayActive: true })).toBe("Today: 20 of 17 action XP, active");
    expect(todayText({ ...current, todayException: true })).toBe(
      "Today is an exception day: it doesn't count for or against you.",
    );
    expect(multiplierChipText(current)).toBe("×1.2 · 24 of the last 30 days active");
  });

  it("says what holding a rank's first level takes", () => {
    const apprentice = {
      rank: 1,
      title: "Apprentice",
      level: 10,
      lastLevel: 19,
      average: 34,
      activeMinimum: 20,
      activeDays: 48,
      windowDays: 90,
      totalXp: 1_476,
      gems: 1,
      reachedOn: null,
    };
    expect(rankHoldText(apprentice)).toBe("a 90-day average of 34 base XP, 48 active days of 90 (20+ action XP)");
    expect(rankHoldText({ ...apprentice, rank: 0, average: null, activeMinimum: null, activeDays: null })).toBe(
      "where every domain starts",
    );
  });

  it("confirms a mark and an unmark by the day", () => {
    expect(exceptionMarkedMessage("2026-10-24")).toBe(`Marked ${shortDay("2026-10-24")} as an exception`);
    expect(exceptionMarkedMessage(undefined)).toBe("Marked today as an exception");
    expect(exceptionUnmarkedMessage("2026-10-24")).toBe(`Unmarked ${shortDay("2026-10-24")}: it counts again`);
  });
});

describe("progressMessage (xp-domains plan §7.4.7)", () => {
  const change = {
    domain: "health_xp",
    levelFrom: 11,
    levelTo: 12,
    rankFrom: 1,
    rankTo: 1,
    rankTitle: "Apprentice",
    bestLevelFrom: 11,
    bestLevelTo: 12,
    bestRankFrom: 1,
    bestRankTo: 1,
  };

  it("says a new best level with the domain's symbol", () => {
    expect(progressMessage(change)).toEqual({ kind: "level", text: "❤️ Health level 12!" });
  });

  it("says a new best rank by its title, whatever the level did", () => {
    expect(
      progressMessage({ ...change, levelFrom: 19, levelTo: 20, rankTo: 2, rankTitle: "Journeyman", bestLevelFrom: 19, bestLevelTo: 20, bestRankTo: 2 }),
    ).toEqual({ kind: "rank", text: "You're now a Health Journeyman." });
  });

  it("says getting back to a level below the best quietly", () => {
    expect(progressMessage({ ...change, levelFrom: 21, levelTo: 22, bestLevelFrom: 30, bestLevelTo: 30 })).toEqual({
      kind: "back",
      text: "Health back to level 22",
    });
  });

  it("says a level lost plainly, with the bests kept", () => {
    expect(progressMessage({ ...change, levelFrom: 12, levelTo: 11, bestLevelFrom: 12, bestLevelTo: 12 })).toEqual({
      kind: "lost",
      text: "Health down to level 11",
    });
  });
});

describe("normalizing domain reads", () => {
  it("reads an earning answer's int64 fields from strings, and an older answer's bests as the level", () => {
    const earned = normalizeEarned({
      awards: [{ id: "a", entry: "earn", trigger: "rank", awardKey: "rank:health_xp:10", subject: null, amounts: { gems: "1" } }],
      progress: [
        { domain: "health_xp", levelFrom: "9", levelTo: "10", rankFrom: "0", rankTo: "1", rankTitle: "Apprentice", bestLevelFrom: "9", bestLevelTo: "10", bestRankFrom: "0", bestRankTo: "1" },
        { domain: "health_xp", levelFrom: "12", levelTo: "11", rankFrom: "1", rankTo: "1", rankTitle: "Apprentice" },
      ],
    });
    expect(earned.awards[0].amounts).toEqual({ gems: 1 });
    expect(earned.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 9, bestLevelTo: 10, bestRankFrom: 0, bestRankTo: 1 },
      { domain: "health_xp", levelFrom: 12, levelTo: 11, rankFrom: 1, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 12, bestLevelTo: 11, bestRankFrom: 1, bestRankTo: 1 },
    ]);
    expect(normalizeEarned(undefined)).toEqual({ awards: [], progress: [] });
  });

  it("reads a domain row's numbers from strings and its missing parts as null", () => {
    const raw = {
      ...row({ exact: "43.27", level: "44", fallingFrom: "47" } as never),
      totalXp: "60000",
      lifetime: { start: "2025-01-01", level: "52", levelXp: "2400", xpIntoLevel: "1160", xpToNext: "1240" },
    } as unknown as DomainRow;
    const read = normalizeDomainRow(raw);
    expect(read.totalXp).toBe(60_000);
    expect(read.lifetime).toMatchObject({ level: 52, xpToNext: 1_240 });
    expect(read.shown).toMatchObject({ level: 44, exact: 43.27, fallingFrom: 47 });
    const bare = normalizeDomainRow({ key: "social_xp", label: "Social XP", name: "Social", totalXp: "40", configured: false } as unknown as DomainRow);
    expect(bare).toMatchObject({ totalXp: 40, lifetime: null, consistency: null, shown: null, best: null, current: null, ranks: null });
  });

  it("reads a report's numbers from strings, and an unconfigured row's progression as null", () => {
    const raw = {
      today: "2026-10-25",
      days: "30",
      domains: [
        { key: "health_xp", label: "Health XP", name: "Health", configured: true, from: "2026-09-24", to: "2026-10-23", base: "2100", paid: "2310", daysCounted: "28", exceptionDays: "2", activeDays: "25", activeShare: "0.8929", averageBase: "73.6", levelFrom: "12", levelTo: "17", rankFrom: "1", rankTo: "1", rankTitle: "Apprentice", bestLevelFrom: "12", bestLevelTo: "17", bestRank: "1", bestRankTitle: "Apprentice", ranksReached: [{ rank: "1", title: "Apprentice", qualifiedOn: "2026-10-14" }], ranks: [{ day: "2026-10-23", level: "17", rank: "1", bestRank: "1", exception: false }] },
        { key: "social_xp", label: "Social XP", name: "Social", configured: false, from: "2026-09-26", to: "2026-10-25", base: "20", paid: "20" },
      ],
    } as unknown as DomainReportRead;
    const report = normalizeDomainReport(raw);
    expect(report.days).toBe(30);
    expect(report.domains[0]).toMatchObject({
      paid: 2310,
      exceptionDays: 2,
      activeShare: 0.8929,
      levelTo: 17,
      bestRank: 1,
      ranksReached: [{ rank: 1 }],
      ranks: [{ level: 17, rank: 1, bestRank: 1, exception: false }],
    });
    expect(report.domains[1]).toMatchObject({ paid: 20, daysCounted: null, activeShare: null, rankTitle: null, ranksReached: null, ranks: null });
  });

  it("reads a domain's days with null standings for an unconfigured domain", () => {
    const raw = {
      key: "social_xp",
      label: "Social XP",
      name: "Social",
      configured: false,
      today: "2026-10-25",
      days: [{ day: "2026-10-25", base: "20", paid: "20" }],
    } as unknown as DomainDaysRead;
    expect(normalizeDomainDays(raw)).toMatchObject({
      settledThrough: null,
      days: [{ day: "2026-10-25", base: 20, action: 0, passive: 0, paid: 20, exception: null, counted: null, active: null, level: null }],
    });
  });

  it("reads exception days and a mark's answer", () => {
    expect(
      normalizeExceptionDays({ today: "2026-10-25", from: "2026-07-28", exceptions: [{ day: "2026-10-11", comment: "Sick" }] } as never),
    ).toEqual({ today: "2026-10-25", from: "2026-07-28", exceptions: [{ day: "2026-10-11", comment: "Sick", markedAt: "" }] });
    expect(normalizeExceptionResult({ exception: { day: "2026-10-11", comment: "Sick", markedAt: "x" } })).toEqual({
      exception: { day: "2026-10-11", comment: "Sick", markedAt: "x" },
      awards: [],
      progress: [],
    });
    expect(normalizeExceptionResult(undefined)).toEqual({ exception: null, awards: [], progress: [] });
  });
});
