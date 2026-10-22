import { describe, expect, it } from "vitest";
import {
  applyMultiplier,
  costToNext,
  domainChanges,
  DomainProgress,
  gemsFor,
  HEALTH,
  levelFor,
  rankAwardKey,
  shiftDay,
  toDomainDays,
  toDomainReportRow,
  toDomainRow,
  topLevelOf,
  totalFor,
  type DomainDay,
  type DomainRules,
} from "@/lib/domain-fold";

// The C# tests' examples (DomainLevelTests, DomainProgressTests, DomainPricingTests), so the
// mock reads Health the way the API does.

const TODAY = "2026-10-20";
const curve = HEALTH.curve;
const ago = (n: number) => shiftDay(TODAY, -n);
const day = (daysAgo: number, base: number, paid = base): DomainDay => ({ day: ago(daysAgo), base, paid });
const good = (daysAgo: number) => day(daysAgo, 70);
/** The same credit on each day from `fromAgo` down to `toAgo` days ago. */
const days = (fromAgo: number, toAgo: number, xp: number) =>
  Array.from({ length: fromAgo - toAgo + 1 }, (_, i) => day(fromAgo - i, xp));
const fold = (credit: DomainDay[], rules: DomainRules = HEALTH) => DomainProgress.fold(rules, credit, TODAY);

describe("Health's level curve (§2.1)", () => {
  it.each([
    [1, 0],
    [2, 300],
    [10, 3_600],
    [20, 9_975],
    [30, 18_850],
    [45, 36_850],
    [46, 38_250],
    [50, 44_800],
    [70, 100_350],
    [90, 193_900],
    [100, 254_925],
  ])("reaches level %i at %i XP", (level, total) => {
    expect(totalFor(curve, level)).toBe(total);
    expect(levelFor(curve, total)).toBe(level);
    if (level > 1) expect(levelFor(curve, total - 1)).toBe(level - 1);
  });

  it("costs 300 plus 25 a level, then 70 more a level after 45", () => {
    expect([1, 2, 45, 46, 99, 100].map((l) => costToNext(curve, l))).toEqual([300, 325, 1_400, 1_495, 6_530, 0]);
    for (let level = 1; level < 100; level++) {
      expect(costToNext(curve, level)).toBe(totalFor(curve, level + 1) - totalFor(curve, level));
    }
    expect(levelFor(curve, 10_000_000)).toBe(100);
    expect(levelFor(curve, -5)).toBe(1);
    expect(() => totalFor(curve, 0)).toThrow(RangeError);
    expect(() => totalFor(curve, 101)).toThrow(RangeError);
  });

  it("tops each rank out below the next and pays its Gems once under its own key", () => {
    expect(HEALTH.ranks.map((_, i) => topLevelOf(HEALTH, i))).toEqual([9, 19, 29, 39, 49, 59, 69, 79, 89, 99, 100]);
    expect([1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((i) => gemsFor(HEALTH, i))).toEqual([1, 1, 1, 1, 1, 1, 1, 1, 1, 5]);
    expect(rankAwardKey(HEALTH, 1)).toBe("rank:health_xp:10");
    expect(rankAwardKey(HEALTH, 10)).toBe("rank:health_xp:100");
  });
});

describe("the daily fold (§3.5)", () => {
  it("judges a day on its base XP, not what was paid", () => {
    const progress = fold([day(3, 50, 65), day(2, 60), day(1, 35), day(1, 25), day(0, 59, 77)]);
    expect(progress.periods.map((p) => p.succeeded)).toEqual([false, true, true, false]);
    expect(progress.periods[2]).toMatchObject({ base: 60, paid: 60 });
    expect(progress.totalXp).toBe(65 + 60 + 60 + 77);
  });

  it("counts days before the start as unsuccessful and uncovered", () => {
    const progress = fold([day(20, 0), ...Array.from({ length: 9 }, (_, i) => good(i + 1))]);
    expect(progress.start).toBe(ago(9));
    expect(progress.periods).toHaveLength(10);
    expect(progress.succeededOn(ago(15))).toBe(false);
    const window = progress.consistency;
    expect([window.from, window.to, window.days, window.coveredDays, window.successfulDays]).toEqual([ago(30), ago(1), 30, 9, 9]);
    expect(window.share).toBeCloseTo(0.3);
    expect(progress.multiplierNow).toBe(1);
    expect(progress.nextTier).toEqual({ minShare: 0.5, multiplier: 1.1 });
    expect(window.daysNeededFor(0.5)).toBe(6);
  });

  it("reads nothing paid yet as not started", () => {
    const progress = fold([day(4, 0)]);
    expect(progress.start).toBeNull();
    expect(progress.periods).toEqual([]);
    expect([progress.totalXp, progress.todayBase, progress.todaySucceeded]).toEqual([0, 0, false]);
    expect(progress.consistency.days).toBe(30);
    expect(progress.consistency.daysNeededFor(progress.nextTier!.minShare)).toBe(15);
  });

  it("counts today toward a window ending today once it succeeds", () => {
    const thirteen = Array.from({ length: 13 }, (_, i) => good(i + 1));
    const notYet = fold([...thirteen, day(0, 40)]);
    const done = fold([...thirteen, day(0, 30), day(0, 40)]);

    const before = notYet.windowEndingOn(TODAY, 14);
    expect([before.from, before.to, before.successfulDays]).toEqual([ago(14), ago(1), 13]);
    const after = done.windowEndingOn(TODAY, 14);
    expect([after.from, after.to, after.successfulDays, after.share]).toEqual([ago(13), TODAY, 14, 1]);
    expect(done.consistency).toEqual(notYet.consistency);
    expect(done.windowEndingOn(shiftDay(TODAY, 5), 14).to).toBe(TODAY);
    const earlier = done.windowEndingOn(ago(3), 7);
    expect([earlier.from, earlier.to, earlier.successfulDays]).toEqual([ago(9), ago(3), 7]);
  });

  it.each([
    [14, 1.0],
    [15, 1.1],
    [20, 1.1],
    [21, 1.2],
    [25, 1.2],
    [26, 1.3],
  ])("picks the multiplier from %i successful days of the last 30", (successful, multiplier) => {
    const credit = Array.from({ length: 30 }, (_, i) => i + 1).map((n) => (n > 30 - successful ? good(n) : day(n, 10)));
    const progress = fold(credit);
    expect(progress.consistency.successfulDays).toBe(successful);
    expect(progress.multiplierNow).toBe(multiplier);
    expect(progress.multiplierFor(TODAY)).toBe(multiplier);
  });

  it("prices a day from the 30 days before it, never the day itself", () => {
    const progress = fold(Array.from({ length: 15 }, (_, i) => good(i + 6)));
    expect(progress.multiplierFor(ago(6))).toBe(1);
    expect(progress.multiplierFor(ago(5))).toBe(1.1);
    expect(progress.multiplierFor(ago(20))).toBe(1);
  });

  it("leaves out credit dated after today", () => {
    const progress = fold([good(0), day(-1, 500)]);
    expect(progress.totalXp).toBe(70);
    expect(progress.periods.at(-1)!.day).toBe(TODAY);
    expect(fold([day(-2, 90)]).start).toBeNull();
  });
});

describe("levels and ranks (§3.5)", () => {
  it("starts every domain as a level 1 Novice", () => {
    const progress = fold([]);
    expect([progress.level, progress.rawLevel, progress.rank, progress.topLevel]).toEqual([1, 1, 0, 9]);
    expect([progress.xpIntoLevel, progress.xpToNext, progress.bankedXp]).toEqual([0, 300, 0]);
    expect(progress.rankQualifiedOn).toBeNull();
    expect(progress.standingOn(TODAY)).toEqual({ day: TODAY, totalXp: 0, rawLevel: 1, level: 1, rank: 0 });
    expect(progress.next).toMatchObject({ daysOld: 0, levelReached: false, oldEnough: false, required: 7, daysNeeded: 7 });
    expect(progress.next!.rule.title).toBe("Apprentice");
  });

  it("stops the level at the rank's top and banks the rest", () => {
    const progress = fold([day(0, 5_000)]);
    expect([progress.rawLevel, progress.level, progress.rank, progress.capped]).toEqual([12, 9, 0, true]);
    expect(progress.xpIntoLevel).toBe(costToNext(curve, 9));
    expect(progress.xpToNext).toBe(0);
    expect(progress.bankedXp).toBe(5_000 - 3_600);
    expect(progress.next).toMatchObject({ levelReached: true, oldEnough: false, daysOld: 1, daysNeeded: 6 });
    expect(progress.next!.window.successfulDays).toBe(1);
  });

  it("waits for a domain as old as the window, then the level jumps", () => {
    const progress = fold([day(20, 5_000), ...days(19, 0, 70)]);
    expect(progress.ranksReached.map((r) => [r.rank, r.qualifiedOn])).toEqual([[1, ago(7)]]);
    expect(progress.standingOn(ago(8)).level).toBe(9);
    expect(progress.standingOn(ago(7))).toEqual({ day: ago(7), totalXp: 5_000 + 13 * 70, rawLevel: 14, level: 14, rank: 1 });
    expect([progress.level, progress.rawLevel, progress.rank, progress.capped, progress.topLevel]).toEqual([14, 14, 1, false, 19]);
  });

  it("enters several ranks on one day", () => {
    const progress = fold([...days(29, 1, 70), day(0, 10_000)]);
    expect(progress.ranksReached.map((r) => [r.rank, r.qualifiedOn])).toEqual([
      [1, TODAY],
      [2, TODAY],
    ]);
    expect([progress.rawLevel, progress.level, progress.rankRule.title]).toEqual([22, 22, "Journeyman"]);
    expect(progress.next!.rule.title).toBe("Adept");
    expect(progress.next!.levelReached).toBe(false);
  });

  it("reaches Grandmaster and level 100 after a year at the top, with nothing banked", () => {
    const progress = fold(days(399, 0, 700));
    expect(progress.ranksReached.map((r) => r.rank)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    expect(progress.ranksReached.at(-2)!.qualifiedOn).toBe(ago(399 - 364));
    expect(progress.ranksReached.at(-1)!.qualifiedOn).toBe(ago(399 - 364));
    expect([progress.level, progress.rank, progress.capped]).toEqual([100, 10, false]);
    expect(progress.xpIntoLevel).toBe(400 * 700 - 254_925);
    expect([progress.xpToNext, progress.bankedXp]).toEqual([0, 0]);
    expect(progress.next).toBeNull();
  });

  it("never lowers a rank or a level for inactivity", () => {
    const progress = fold([day(79, 5_000), ...days(78, 60, 70)]);
    expect(progress.rank).toBe(1);
    expect(progress.rankQualifiedOn).toBe(ago(66));
    expect(progress.standingOn(TODAY)).toEqual({ ...progress.standingOn(ago(60)), day: TODAY });
    expect(progress.consistency.successfulDays).toBe(0);
    expect(progress.multiplierNow).toBe(1);
    expect(progress.next!.barMet).toBe(false);
  });

  it("re-reads the same days under a retuned rule", () => {
    const credit = [day(14, 4_000), ...days(13, 0, 70).map((d, i) => (i % 2 === 0 ? d : { ...d, base: 10, paid: 10 }))];
    const stricter: DomainRules = {
      ...HEALTH,
      ranks: HEALTH.ranks.map((r, i) => (i === 1 ? { ...r, minConsistency: 0.6 } : r)),
    };
    expect(fold(credit).rank).toBe(1);
    expect(fold(credit, stricter).rank).toBe(0);
    expect(fold(credit, stricter).level).toBe(9);
  });

  it("opens a rank gate on today only once today has succeeded", () => {
    const history = [day(13, 4_000), ...days(12, 1, 70)];
    const notYet = fold([...history, day(0, 50)]);
    const done = fold([...history, day(0, 70)]);
    expect(notYet.rank).toBe(0);
    expect([notYet.next!.daysOld, notYet.next!.oldEnough]).toEqual([13, false]);
    expect(done.rank).toBe(1);
    expect(done.rankQualifiedOn).toBe(TODAY);
  });

  it("holds every rank above one that lists a milestone", () => {
    const gated: DomainRules = {
      ...HEALTH,
      ranks: HEALTH.ranks.map((r, i) => (i === 1 ? { ...r, milestones: ["Run a 10K"] } : r)),
    };
    const progress = fold([...days(29, 1, 70), day(0, 10_000)], gated);
    expect([progress.rank, progress.level]).toEqual([0, 9]);
  });
});

describe("the multiplier on what's paid (§3.6)", () => {
  it.each([
    [25, 1.1, 28],
    [60, 1.3, 78],
    [60, 1.2, 72],
    [15, 1.3, 20],
    [5, 1.1, 6],
    [4, 1.1, 4],
    [70, 1.0, 70],
    [-25, 1.1, -28],
  ])("pays %i at ×%d as %i, rounding halves up", (amount, multiplier, paid) => {
    expect(applyMultiplier(amount, multiplier)).toBe(paid);
  });
});

describe("a period report (§3.13, DomainReportTests)", () => {
  /** 60 days ending yesterday, all successful but 20–22 days ago, and `today` base XP today. */
  const sixtyDays = (today = 30) =>
    fold([
      ...Array.from({ length: 60 }, (_, i) => i + 1)
        .filter((n) => n < 20 || n > 22)
        .map(good),
      day(0, today),
    ]);

  it("counts the days a rank gate reads and what moved in them", () => {
    const r = sixtyDays().report(30);
    expect([r.from, r.to]).toEqual([ago(30), ago(1)]);
    expect([r.days, r.daysCounted, r.successfulDays, r.fullHistory]).toEqual([30, 30, 27, true]);
    expect([r.consistency, r.base, r.paid, r.averageBase]).toEqual([0.9, 27 * 70, 27 * 70, 63]);
    expect(r.before).toMatchObject({ totalXp: 2_100, level: 6, rank: 0 });
    expect(r.now).toMatchObject({ totalXp: 4_020, level: 10, rank: 1 });
    expect(r.ranksReached.map((x) => [x.rule.title, x.qualifiedOn])).toEqual([["Apprentice", ago(6)]]);
  });

  it("starts a week from the standing the day before it", () => {
    const r = sixtyDays().report(7);
    expect([r.from, r.to, r.daysCounted, r.successfulDays, r.paid]).toEqual([ago(7), ago(1), 7, 7, 490]);
    expect(r.before).toMatchObject({ totalXp: 3_500, level: 9, rank: 0 });
    expect(r.ranksReached.map((x) => x.rule.title)).toEqual(["Apprentice"]);
  });

  it("counts today once it has succeeded", () => {
    const r = sixtyDays(70).report(7);
    expect([r.from, r.to, r.daysCounted, r.successfulDays, r.base]).toEqual([ago(6), TODAY, 7, 7, 490]);
  });

  it("reports a young domain over the days it has", () => {
    const r = fold(days(9, 1, 70).filter((d) => d.day !== ago(4))).report(30);
    expect([r.daysCounted, r.successfulDays, r.fullHistory]).toEqual([9, 8, false]);
    expect(r.consistency).toBeCloseTo(8 / 9, 12);
    expect(r.now).toMatchObject({ totalXp: 560, level: 2 });
    expect(r.before.level).toBe(1);
  });

  it("counts nothing before the start, and refuses an empty period", () => {
    const r = fold([]).report(7);
    expect([r.daysCounted, r.consistency, r.averageBase, r.before.level, r.now.level]).toEqual([0, 0, 0, 1, 1]);
    expect(() => fold([]).report(0)).toThrow(RangeError);
  });

  it("reads a row as GET /api/domains/report does, and an unconfigured domain's XP only", () => {
    const credit = [...days(60, 23, 70), ...days(19, 1, 70)];
    const health = toDomainReportRow({ key: "health_xp", label: "Health XP" }, credit, TODAY, 30);
    expect(health).toMatchObject({
      name: "Health",
      configured: true,
      from: ago(30),
      to: ago(1),
      daysCounted: 30,
      successfulDays: 27,
      consistency: 0.9,
      averageBase: 63,
      levelFrom: 6,
      levelTo: 10,
      rankFrom: 0,
      rankTo: 1,
      rankTitle: "Apprentice",
      ranksReached: [{ rank: 1, title: "Apprentice", qualifiedOn: ago(6) }],
    });
    const young = toDomainReportRow({ key: "health_xp", label: "Health XP" }, days(9, 1, 70).filter((d) => d.day !== ago(4)), TODAY, 30);
    expect([young.consistency, young.averageBase]).toEqual([0.8889, 62.2]);

    const social = toDomainReportRow(
      { key: "social_xp", label: "Social XP" },
      [day(3, 15), day(10, 40), day(-1, 99)],
      TODAY,
      7,
    );
    expect(social).toMatchObject({ configured: false, from: ago(6), to: TODAY, paid: 15, daysCounted: null, consistency: null });
  });
});

describe("reading it as the API does", () => {
  it("answers GET /api/domains' row, capped text and all", () => {
    const progress = fold([day(0, 5_000)]);
    const row = toDomainRow({ key: "health_xp", label: "Health XP" }, progress.totalXp, progress);
    expect(row).toMatchObject({ name: "Health", configured: true, totalXp: 5_000 });
    expect(row.lifetime).toMatchObject({ level: 9, rawLevel: 12, capped: true, rankTitle: "Novice", bankedXp: 1_400, levelXp: costToNext(curve, 9) });
    expect(row.nextRank).toMatchObject({ title: "Apprentice", level: 10, bar: 0.5, windowDays: 14, requiredDays: 7, successfulDays: 1 });
    expect(row.current).toMatchObject({ todayBase: 5_000, todaySucceeded: true, target: 60, rule: "at least 60 base XP", multiplier: 1 });
    expect(row.ranks).toHaveLength(11);
    expect(row.ranks![10]).toMatchObject({ title: "Grandmaster", topLevel: 100, gems: 5 });
    expect(toDomainRow({ key: "social_xp", label: "Social XP" }, 40, null)).toMatchObject({
      name: "Social",
      configured: false,
      lifetime: null,
      ranks: null,
    });
  });

  it("answers GET /api/domains/{key}/days, oldest first", () => {
    const read = toDomainDays({ key: "health_xp", label: "Health XP" }, [day(5, 70), day(0, 50, 65)], TODAY, 6);
    expect(read.target).toBe(60);
    expect(read.days.map((d) => [d.base, d.paid, d.succeeded])).toEqual([
      [70, 70, true],
      [0, 0, false],
      [0, 0, false],
      [0, 0, false],
      [0, 0, false],
      [50, 65, false],
    ]);
    const social = toDomainDays({ key: "social_xp", label: "Social XP" }, [day(1, 25)], TODAY, 2);
    expect(social.days).toEqual([
      { day: ago(1), base: 25, paid: 25, succeeded: null },
      { day: TODAY, base: 0, paid: 0, succeeded: null },
    ]);
  });

  it("says which domains a write moved, from the rank below any rank-up it paid", () => {
    const before = fold([day(13, 4_000), ...days(12, 1, 70)]);
    const after = fold([day(13, 4_000), ...days(12, 1, 70), day(0, 70)]);
    // 4,910 XP: level 12 once Apprentice lifts the cap at 9.
    expect(domainChanges([before], [after], [{ currency: "health_xp", rank: 1 }])).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 12, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    // Settling a rank time alone reached 7 days ago: before and after are the same fold.
    const reached = fold(days(20, 14, 520));
    expect(reached.rankQualifiedOn).toBe(ago(7));
    expect(domainChanges([reached], [reached], [{ currency: "health_xp", rank: 1 }])).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    expect(domainChanges([reached], [reached], [])).toEqual([]);
  });
});
