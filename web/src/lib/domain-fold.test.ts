import { describe, expect, it } from "vitest";
import {
  activeDaysNeededFor,
  activeMinimumFor,
  ageNeededFor,
  applyMultiplier,
  averageFor,
  costToNext,
  describeRule,
  domainChanges,
  DomainProgress,
  DomainWalker,
  fallNext,
  gemsFor,
  HEALTH,
  levelFor,
  levelOf,
  rankAwardKey,
  rankFor,
  shiftDay,
  toDomainDays,
  toDomainReportRow,
  toDomainRow,
  topLevelOf,
  totalFor,
  type DomainDay,
  type DomainStanding,
} from "@/lib/domain-fold";

// The C# tests' examples (DomainLevelTests, DomainShownLevelTests, DomainProgressTests,
// DomainReportTests), so the mock reads Health the way the API does (xp-domains plan §7).

const START = "2026-01-01";
const curve = HEALTH.curve;
const rule = HEALTH.consistency;
const HEALTH_XP = { key: "health_xp", label: "Health XP" };

/** Day `index` (0 is the start). */
const D = (index: number) => shiftDay(START, index);
/** Credit on day `index`: base, its passive part, and paid (base unless given). */
const on = (index: number, base: number, passive = 0, paid = base): DomainDay => ({ day: D(index), base, paid, passive });
/** The same credit on `count` days from day `from`. */
const run = (from: number, count: number, base: number, passive = 0) =>
  Array.from({ length: count }, (_, i) => on(from + i, base, passive));
const range = (from: number, count: number) => Array.from({ length: count }, (_, i) => from + i);
/** Folds as of day `today`, settled through the day before unless `settled` says otherwise. */
const fold = (credit: DomainDay[], today: number, exceptions: number[] = [], settled?: number) =>
  DomainProgress.fold(HEALTH, credit, D(today), exceptions.map(D), settled == null ? undefined : D(settled));

/** A small seeded random (mulberry32): `next(min, max)` is a whole number in [min, max). */
function seeded(seed: number) {
  let a = seed >>> 0;
  return (min: number, max: number) => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    const r = ((t ^ (t >>> 14)) >>> 0) / 4_294_967_296;
    return min + Math.floor(r * (max - min));
  };
}

/** 100 days of 3,000 XP (days 0–99): level 100, Grandmaster, from day 89. */
const grandmaster = run(0, 100, 3_000);

/** The first day from `from` whose standing passes `test`. */
const firstDay = (progress: DomainProgress, from: number, test: (s: DomainStanding) => boolean) =>
  progress.standings.findIndex((s, i) => i >= from && test(s));

describe("Health's curve and consistency formulas (§7.2, DomainLevelTests)", () => {
  it.each([
    [1, 0],
    [2, 60],
    [3, 144],
    [10, 1_476],
    [20, 6_068],
    [30, 14_590],
    [40, 27_893],
    [45, 36_602],
    [50, 46_825],
    [60, 72_237],
    [70, 104_979],
    [80, 145_902],
    [90, 195_854],
    [100, 255_686],
  ])("reaches level %i at %i XP", (level, total) => {
    expect(totalFor(curve, level)).toBe(total);
    expect(levelFor(curve, total)).toBe(level);
    if (level > 1) expect(levelFor(curve, total - 1)).toBe(level - 1);
  });

  it("is the whole-number form of the cubic, and each level costs at least the last", () => {
    for (let level = 1; level <= 100; level++) {
      const n = level - 1;
      const expected = 60 * n + 12 * n * (n - 1) + Math.floor((17 * n * (n - 1) * (n - 2) + 119) / 120);
      expect(totalFor(curve, level)).toBe(expected);
    }
    expect([2, 10, 20, 30, 45, 50, 70, 90, 100].map((l) => costToNext(curve, l))).toEqual([
      84, 306, 661, 1_101, 1_920, 2_235, 3_710, 5_524, 0,
    ]);
    for (let level = 2; level < 100; level++) {
      expect(costToNext(curve, level)).toBeGreaterThanOrEqual(costToNext(curve, level - 1));
    }
    expect(levelFor(curve, 10_000_000)).toBe(100);
    expect(levelFor(curve, -5)).toBe(1);
    expect(() => totalFor(curve, 0)).toThrow(RangeError);
    expect(() => totalFor(curve, 101)).toThrow(RangeError);
  });

  it.each([
    [10, "Apprentice", 25, 10, 48, 1_476],
    [20, "Journeyman", 31, 12, 52, 6_068],
    [30, "Adept", 37, 15, 56, 14_590],
    [40, "Expert", 43, 17, 59, 27_893],
    [50, "Veteran", 49, 20, 63, 46_825],
    [60, "Elite", 56, 22, 67, 72_237],
    [70, "Master", 62, 25, 70, 104_979],
    [80, "Champion", 68, 27, 74, 145_902],
    [90, "Legend", 74, 30, 78, 195_854],
    [100, "Grandmaster", 80, 32, 81, 255_686],
  ])("holds level %i, %s, at the plan's figures", (level, title, average, activeMinimum, activeDaysOf90, total) => {
    const rank = rankFor(HEALTH, level);
    expect([HEALTH.ranks[rank].title, HEALTH.ranks[rank].startLevel]).toEqual([title, level]);
    expect(averageFor(rule, level)).toBe(average);
    expect(activeMinimumFor(rule, level)).toBe(activeMinimum);
    expect(activeDaysNeededFor(rule, level, 90)).toBe(activeDaysOf90);
    expect(totalFor(curve, level)).toBe(total);
    expect(rank).toBe(rankFor(HEALTH, level - 1) + 1);
  });

  it("runs 20 to 80, 10 to 32 and half to nine tenths, exactly", () => {
    expect([averageFor(rule, 2), averageFor(rule, 100)]).toEqual([20, 80]);
    expect([activeMinimumFor(rule, 2), activeMinimumFor(rule, 100)]).toEqual([10, 32]);
    expect([activeDaysNeededFor(rule, 2, 90), activeDaysNeededFor(rule, 100, 90)]).toEqual([45, 81]);
    expect([activeDaysNeededFor(rule, 2, 7), activeDaysNeededFor(rule, 100, 7)]).toEqual([4, 7]);
    expect([ageNeededFor(rule, 2), ageNeededFor(rule, 89), ageNeededFor(rule, 100)]).toEqual([3, 90, 90]);
    for (let level = 2; level <= 100; level++) {
      expect(averageFor(rule, level)).toBe(20 + Math.round((60 * (level - 2)) / 98));
      for (const days of [1, 7, 30, 45, 89, 90]) {
        const numerator = days * (490 + 4 * (level - 2));
        expect(activeDaysNeededFor(rule, level, days)).toBe(Math.floor((numerator + 979) / 980));
      }
    }
  });

  it("ranks the shown level in tens and pays each rank's Gems once under its own key", () => {
    expect([1, 9, 10, 19, 90, 99, 100].map((l) => rankFor(HEALTH, l))).toEqual([0, 0, 1, 1, 9, 9, 10]);
    expect(HEALTH.ranks.map((_, i) => topLevelOf(HEALTH, i))).toEqual([9, 19, 29, 39, 49, 59, 69, 79, 89, 99, 100]);
    expect(range(1, 10).map((i) => gemsFor(HEALTH, i))).toEqual([1, 1, 1, 1, 1, 1, 1, 1, 1, 5]);
    expect(rankAwardKey(HEALTH, 1)).toBe("rank:health_xp:10");
    expect(rankAwardKey(HEALTH, 10)).toBe("rank:health_xp:100");
  });

  it("falls in ten-thousandths of a level and shows rounded up", () => {
    expect(fallNext(HEALTH.fall, 1_000_000)).toBe(990_000);
    expect(fallNext(HEALTH.fall, 990_000)).toBe(980_100);
    expect(fallNext(HEALTH.fall, 10_000)).toBe(9_900);
    expect([levelOf(1_000_000), levelOf(990_001), levelOf(990_000), levelOf(9_900)]).toEqual([100, 100, 99, 1]);
  });
});

describe("the shown level (§7.3, DomainShownLevelTests)", () => {
  it("falls from level 100 on the plan's timeline when nothing is logged", () => {
    const progress = fold(grandmaster, 360);

    // Raw consistency holds 9 days: the fall starts on day 10.
    expect(progress.standingOn(D(99 + 9)).level).toBe(100);
    const day10 = progress.standingOn(D(99 + 10));
    expect([day10.consistencyLevel, day10.level, day10.rank, day10.falling]).toEqual([97, 99, 9, true]);
    expect(day10.shownExact).toBe(990_000);

    // Legend day 10, Champion 21, Master 33, Elite 46, Novice 249.
    expect(firstDay(progress, 100, (s) => s.rank <= 9)).toBe(99 + 10);
    expect(firstDay(progress, 100, (s) => s.rank <= 8)).toBe(99 + 21);
    expect(firstDay(progress, 100, (s) => s.rank <= 7)).toBe(99 + 33);
    expect(firstDay(progress, 100, (s) => s.rank <= 6)).toBe(99 + 46);
    expect(firstDay(progress, 100, (s) => s.rank === 0)).toBe(99 + 249);

    for (let i = 110; i < 340; i++) {
      const before = progress.standingOn(D(i - 1));
      const after = progress.standingOn(D(i));
      expect(after.shownExact).toBe(Math.max(after.target * 10_000, Math.floor((before.shownExact * 99) / 100)));
    }

    for (const s of progress.standings.slice(89)) expect([s.bestLevel, s.bestRank]).toEqual([100, 10]);
    expect([progress.bestLevel, progress.bestRank]).toEqual([100, 10]);
  });

  it("never falls below the target, and reports the fall", () => {
    const progress = fold([...grandmaster, ...run(110, 30, 3_000)], 140);

    expect(range(109, 5).map((i) => progress.standingOn(D(i)).level)).toEqual([99, 99, 98, 97, 97]);
    expect(progress.standingOn(D(112)).shownExact).toBe(970_000);
    for (const s of progress.standings) expect(s.level).toBeGreaterThanOrEqual(s.target);
    expect([progress.level, progress.target, progress.falling, progress.paused]).toEqual([97, 97, false, false]);
    expect(progress.lastChange).toEqual({ day: D(112), from: 98, to: 97 });
    expect(progress.fallingSince).toBeNull();
    expect([progress.bestLevel, progress.bestRank]).toEqual([100, 10]);
  });

  it("pauses the fall once the last 7 counted days are back on track", () => {
    const progress = fold([...grandmaster, ...run(130, 70, 3_000)], 200);

    for (const i of range(130, 5)) expect(progress.standingOn(D(i)).falling).toBe(true);
    const paused = progress.standingOn(D(135));
    expect([paused.paused, paused.falling, paused.target]).toEqual([true, false, 42]);
    expect(paused.shownExact).toBe(progress.standingOn(D(134)).shownExact);
    expect(activeDaysNeededFor(rule, paused.level, 7)).toBe(6);
    for (const i of range(135, 65)) {
      const s = progress.standingOn(D(i));
      expect([s.paused, s.shownExact]).toEqual([true, paused.shownExact]);
    }

    expect([progress.paused, progress.falling]).toEqual([true, false]);
    expect([progress.fallingSince, progress.fallingFrom, progress.limitedBy]).toEqual([D(109), 100, "consistency"]);
  });

  it("leaves level and rank alone through a marked lapse", () => {
    const progress = fold([...grandmaster, ...run(130, 70, 3_000)], 200, range(100, 30));

    for (const s of progress.standings.slice(89)) expect([s.level, s.rank, s.consistencyLevel]).toEqual([100, 10, 100]);
    for (const s of progress.standings.slice(100, 130)) {
      expect([s.exception, s.counted, s.falling]).toEqual([true, false, false]);
    }
    expect([progress.level, progress.target]).toEqual([100, 100]);
    // The window reaches back past the marked days.
    expect([progress.window.days, progress.window.from]).toEqual([90, D(80)]);
  });

  it("pauses a fall already under way on an exception day", () => {
    const progress = fold(grandmaster, 125, range(115, 5));

    const lastFall = progress.standingOn(D(114));
    expect(lastFall.falling).toBe(true);
    for (const i of range(115, 5)) {
      const s = progress.standingOn(D(i));
      expect([s.paused, s.exception, s.shownExact, s.consistencyLevel]).toEqual([
        true,
        true,
        lastFall.shownExact,
        lastFall.consistencyLevel,
      ]);
    }
    expect(progress.standingOn(D(120)).falling).toBe(true);
    expect(progress.standingOn(D(120)).shownExact).toBeLessThan(lastFall.shownExact);

    // Today marked: the fall reads as paused.
    const markedToday = fold(grandmaster, 125, [125]);
    expect([markedToday.todayException, markedToday.paused, markedToday.falling]).toEqual([true, true, false]);
  });

  it("never lowers the level on a day that hasn't settled", () => {
    // Day 109 is the tenth day off: the day the fall would start.
    expect(fold(grandmaster, 109).level).toBe(100);
    expect(fold(grandmaster, 109, [], 109).level).toBe(99);

    // Before 4 a.m. yesterday hasn't settled either.
    const early = fold(grandmaster, 110, [], 108);
    expect(early.level).toBe(100);
    expect(early.standingOn(D(109)).counted).toBe(false);
    expect(fold(grandmaster, 110).level).toBe(99);

    // Unsettled, the level reads as falling toward the target it would reach.
    expect(fold(grandmaster, 112).falling).toBe(true);
  });

  it("rises to the target at once, even on an unsettled day", () => {
    // 90 days of 60 base XP (50 of it action): the last 90 days support 68, lifetime XP 18.
    const steady = run(0, 90, 60, 10);
    const before = fold(steady, 90);
    expect([before.lifetimeLevel, before.consistencyLevel, before.level, before.limitedBy]).toEqual([
      18,
      68,
      18,
      "lifetime",
    ]);

    // A huge day today lifts lifetime XP past level 70: the level goes straight to 68.
    const after = fold([...steady, on(90, 100_000)], 90);
    expect([after.lifetimeLevel, after.consistencyLevel, after.target, after.level, after.limitedBy]).toEqual([
      70,
      68,
      68,
      68,
      "consistency",
    ]);
    expect(after.lastChange).toEqual({ day: D(90), from: 18, to: 68 });
    expect(after.bestLevelOn).toBe(D(90));
    expect(after.ranksReached.map((r) => r.rank)).toEqual([1, 2, 3, 4, 5, 6]);
    for (const r of after.ranksReached.slice(1)) expect(r.qualifiedOn).toBe(D(90));
  });

  it("never lowers the best level or rank", () => {
    const random = seeded(11);
    const credit = range(0, 600).map((i) =>
      Math.floor(i / 60) % 3 === 2
        ? on(i, 0)
        : { day: D(i), base: random(40, 400), paid: random(40, 400), passive: 15 },
    );
    const progress = fold(credit, 600);

    const s = progress.standings;
    for (let i = 1; i < s.length; i++) {
      expect(s[i].bestLevel).toBeGreaterThanOrEqual(s[i - 1].bestLevel);
      expect(s[i].bestRank).toBeGreaterThanOrEqual(s[i - 1].bestRank);
      expect(s[i].bestLevel).toBeGreaterThanOrEqual(s[i].level);
      expect(s[i].bestRank).toBe(rankFor(HEALTH, s[i].bestLevel));
    }
    expect(s.some((x) => x.level < x.bestLevel)).toBe(true);
  });
});

describe("the walk (§7.4.4, DomainProgressTests)", () => {
  it("reads nothing paid yet as level 1, not started", () => {
    const progress = fold([on(0, 0)], 4);

    expect([progress.start, progress.periods, progress.standings, progress.todayPeriod]).toEqual([null, [], [], null]);
    expect([progress.level, progress.lifetimeLevel, progress.consistencyLevel, progress.target, progress.rank]).toEqual([
      1, 1, 1, 1, 0,
    ]);
    expect([progress.bestLevel, progress.bestRank]).toEqual([1, 0]);
    expect([progress.totalXp, progress.xpIntoLevel, progress.xpToNext]).toEqual([0, 0, 60]);
    expect(progress.window).toEqual({ days: 0, from: null, to: null, base: 0 });
    expect(progress.nextConsistency).toMatchObject({
      level: 2,
      windowDays: 0,
      average: 20,
      ageNeeded: 3,
      ageNow: 0,
      averageMet: false,
      activeDaysMet: false,
      ageMet: false,
    });
    expect([progress.activeMinimum, progress.todayActive, progress.falling]).toEqual([10, false, false]);
    const w = progress.consistency;
    expect([w.days, w.coveredDays, w.successfulDays]).toEqual([30, 0, 0]);
    expect(progress.multiplierNow).toBe(1);
    expect([progress.limitedBy, progress.lastChange, progress.bestLevelOn, progress.ranksReached]).toEqual([
      null,
      null,
      null,
      [],
    ]);
    expect(progress.settledThrough).toBe(D(3));
  });

  it("runs from the first paid day to today, counting only settled non-exception days", () => {
    // A zero day earlier doesn't move the start: only paid XP starts a domain.
    const progress = fold([on(0, 0), ...run(2, 6, 50, 15)], 7, [4]);

    expect(progress.start).toBe(D(2));
    expect(progress.periods).toHaveLength(6);
    expect([progress.periods[0].day, progress.periods.at(-1)!.day]).toEqual([D(2), D(7)]);
    expect(progress.periods[0].base - progress.periods[0].passive).toBe(35);

    // Day 4 is an exception and day 7 (today) hasn't settled.
    expect(progress.standings.map((s) => s.counted)).toEqual([true, true, false, true, true, false]);
    expect(progress.standings.map((s) => s.exception)).toEqual([false, false, true, false, false, false]);
    expect(progress.window).toEqual({ days: 4, from: D(2), to: D(6), base: 200 });

    // XP on the exception day and today still counts toward lifetime XP.
    expect(progress.totalXp).toBe(300);
    expect([progress.todayBase, progress.todayAction]).toEqual([50, 35]);
  });

  it("settles through yesterday by default, and never past today", () => {
    const credit = run(0, 5, 50);

    expect(fold(credit, 4).settledThrough).toBe(D(3));
    expect(fold(credit, 4, [], 2).settledThrough).toBe(D(2));
    expect(fold(credit, 4, [], 9).settledThrough).toBe(D(4));
    expect(fold(credit, 4).window.days).toBe(4);
    expect(fold(credit, 4, [], 4).window.days).toBe(5);
  });

  it("leaves out credit dated after today", () => {
    const progress = fold([on(0, 70), on(1, 500)], 0);

    expect(progress.totalXp).toBe(70);
    expect(progress.periods.at(-1)!.day).toBe(D(0));
    expect(progress.activeOn(D(1))).toBe(false);
    expect(fold([on(2, 90)], 0).start).toBeNull();
    expect(progress.standingOn(D(9))).toEqual({ ...progress.standingOn(D(0)), day: D(9) });
  });

  it("climbs with active days so far, because the age test counts them", () => {
    // 14 days of 1,000: the age test holds level 13.
    const binge = run(0, 14, 1_000);
    const after = fold(binge, 14);
    expect([after.consistencyLevel, after.lifetimeLevel, after.level, after.limitedBy]).toEqual([
      13,
      29,
      13,
      "consistency",
    ]);
    const next = after.nextConsistency!;
    expect([next.level, next.ageNeeded, next.ageNow]).toEqual([14, 15, 14]);
    expect([next.averageMet, next.activeDaysMet, next.ageMet]).toEqual([true, true, false]);

    // Four days of nothing: active days so far are still 14, so the level doesn't climb.
    const stopped = fold(binge, 18);
    expect([stopped.consistencyLevel, stopped.window.days]).toEqual([13, 18]);

    // Two weeks of nothing: 14 of 28 days active is level 2's half, and no more.
    expect(fold(binge, 28).consistencyLevel).toBe(2);
  });

  it("never passes a level while one below it fails", () => {
    const random = seeded(7);
    const walker = new DomainWalker(HEALTH);
    for (let i = 0; i < 400; i++) {
      const base = random(0, 4) === 0 ? 0 : random(0, 160);
      const passive = Math.min(base, random(0, 25));
      const exception = random(0, 20) === 0;
      const standing = walker.step({ day: D(i), base, paid: base, passive }, exception, true);

      const met = range(2, 99).map((level) => {
        const need = walker.needFor(level);
        return need.averageMet && need.activeDaysMet && need.ageMet;
      });
      const passing = met.findIndex((m) => !m);
      const count = passing === -1 ? met.length : passing;
      expect(met.slice(count)).not.toContain(true);
      expect(standing.consistencyLevel).toBe(1 + count);
    }
  });

  it("supports level 100 with a full window at the top", () => {
    const progress = fold(run(0, 100, 3_000), 100);

    expect([progress.lifetimeLevel, progress.consistencyLevel, progress.target, progress.level, progress.rank]).toEqual([
      100, 100, 100, 100, 10,
    ]);
    expect([progress.bestLevel, progress.bestRank, progress.bestRankRule.title]).toEqual([100, 10, "Grandmaster"]);
    expect(progress.nextConsistency).toBeNull();
    expect([progress.xpToNext, progress.xpIntoLevel]).toEqual([0, 300_000 - 255_686]);
    expect([progress.window.days, progress.limitedBy]).toEqual([90, null]);

    // Level 100 first qualified on day 89: the 90th active day.
    expect(progress.bestLevelOn).toBe(D(89));
    expect(progress.ranksReached.map((r) => r.rank)).toEqual(range(1, 10));
    expect(progress.ranksReached.at(-1)!.qualifiedOn).toBe(D(89));
  });

  it("judges a day active on its action XP at the level going into it", () => {
    // At level 1 an active day takes 10 action XP: sleep doesn't count toward it.
    expect(fold([on(0, 40, 30)], 0).todayActive).toBe(true);
    expect(fold([on(0, 40, 31)], 0).todayActive).toBe(false);

    // At Grandmaster it takes 32.
    const top = run(0, 100, 3_000);
    const day = fold([...top, on(100, 15)], 100);
    expect([day.activeMinimum, day.todayActive]).toEqual([32, false]);
    expect(fold([on(0, 15)], 0).todayActive).toBe(true);
    expect(fold([...top, on(100, 32)], 100).todayActive).toBe(true);
    expect(describeRule(HEALTH, day.level)).toBe("at least 32 action XP (base XP less sleep)");
  });

  it.each([
    [14, 1.0],
    [15, 1.1],
    [20, 1.1],
    [21, 1.2],
    [25, 1.2],
    [26, 1.3],
  ])("picks the multiplier from %i active days of the last 30", (activeDays, multiplier) => {
    // The first days active, the rest with sleep only.
    const credit = range(0, 30).map((i) => (i < activeDays ? on(i, 80) : on(i, 20, 20)));
    const progress = fold(credit, 30);

    const w = progress.consistency;
    expect([w.days, w.coveredDays, w.successfulDays]).toEqual([30, 30, activeDays]);
    expect(progress.multiplierNow).toBe(multiplier);
    expect(progress.multiplierFor(D(30))).toBe(multiplier);
  });

  it("counts days before the start as not active", () => {
    const progress = fold(run(0, 9, 80), 9);

    const w = progress.consistency;
    expect([w.from, w.to, w.days, w.coveredDays, w.successfulDays]).toEqual([D(-21), D(8), 30, 9, 9]);
    expect(progress.multiplierNow).toBe(1);
    expect(progress.nextTier).toEqual({ minShare: 0.5, multiplier: 1.1 });
    expect(w.daysNeededFor(0.5)).toBe(6);
  });

  it("prices a day from the 30 days before it, never the day itself", () => {
    const progress = fold(run(0, 15, 80), 25);

    expect(progress.multiplierFor(D(14))).toBe(1);
    expect(progress.multiplierFor(D(15))).toBe(1.1);
    expect(progress.multiplierFor(D(0))).toBe(1);
    const w = progress.windowBefore(D(15));
    expect([w.from, w.to, w.coveredDays, w.successfulDays]).toEqual([D(-15), D(14), 15, 15]);
  });

  it("skips exception days in the multiplier and reaches back one more day for each", () => {
    const credit = run(0, 30, 80);
    const marked = fold(credit, 40, range(30, 10));
    const unmarked = fold(credit, 40);

    const w = marked.consistency;
    expect([w.from, w.to, w.coveredDays, w.successfulDays]).toEqual([D(0), D(39), 30, 30]);
    expect(marked.multiplierNow).toBe(1.3);
    expect([unmarked.consistency.successfulDays, unmarked.multiplierNow]).toEqual([20, 1.1]);

    // An exception day is never active, whatever it earned.
    const earned = fold([...credit, on(30, 500)], 31, [30]);
    expect(earned.activeOn(D(30))).toBe(false);
    expect(earned.multiplierNow).toBe(1.3);
  });

  it("prices the next day in the walker as the fold reads it", () => {
    const random = seeded(3);
    const walker = new DomainWalker(HEALTH);
    const credit: DomainDay[] = [];
    const exceptions: number[] = [];
    let started = false;
    for (let i = 0; i < 120; i++) {
      const base = random(0, 5) === 0 ? 0 : random(20, 150);
      const exception = i > 0 && random(0, 12) === 0;
      if (exception) exceptions.push(i);

      // The walk starts on the first paid day, as the fold's does.
      const paid = applyMultiplier(base, walker.nextMultiplier());
      const passive = Math.min(base, 15);
      credit.push(on(i, base, passive, paid));
      started ||= paid > 0;
      if (started) walker.step({ day: D(i), base, paid, passive }, exception, true);
    }

    const progress = fold(credit, 119, exceptions, 119);
    for (let i = 0; i < 120; i++) {
      expect(applyMultiplier(credit[i].base, progress.multiplierFor(D(i)))).toBe(credit[i].paid);
    }
    expect(progress.level).toBe(walker.level);
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

describe("a period report (§7.4.4, DomainReportTests)", () => {
  /**
   * 60 days (0–59): 80 base XP a day, 20 of it sleep, but days 40–42 sleep only; days 50–51
   * marked; 500 XP so far today (day 60).
   */
  const sixtyDays = () =>
    fold(
      [...run(0, 60, 80, 20).map((d, i) => (i >= 40 && i <= 42 ? { ...d, base: 20, paid: 20 } : d)), on(60, 500)],
      60,
      [50, 51],
    );

  it("ends a month at the last settled day and counts what counted", () => {
    const progress = sixtyDays();
    const r = progress.report(30);

    // Today hasn't settled, so the 30 days end yesterday and its 500 XP isn't in them.
    expect([r.from, r.to]).toEqual([D(30), D(59)]);
    expect([r.days, r.daysCounted, r.exceptionDays, r.activeDays]).toEqual([30, 28, 2, 25]);
    expect(r.countedBase).toBe(25 * 80 + 3 * 20);
    // Base and paid count the exception days too: XP on them still counts toward lifetime.
    expect([r.base, r.paid]).toEqual([2_220, 2_220]);

    expect(r.before).toEqual(progress.standingOn(D(29)));
    expect(r.end).toEqual(progress.standingOn(D(59)));
    expect(r.standings.map((s) => s.day)).toEqual(range(30, 30).map(D));
    expect(r.standings.filter((s) => s.exception).map((s) => s.day)).toEqual([D(50), D(51)]);
    expect(r.ranksReached).toEqual(progress.ranksReached.filter((x) => x.qualifiedOn >= D(30) && x.qualifiedOn <= D(59)));
  });

  it("lists the ranks first reached in the period and not earlier ones", () => {
    const progress = sixtyDays();
    const month = progress.report(30);
    const all = progress.report(60);

    expect(progress.ranksReached.length).toBeGreaterThan(0);
    expect(all.ranksReached).toEqual(progress.ranksReached);
    expect(month.ranksReached.length).toBeLessThan(all.ranksReached.length);
    expect([all.before.level, all.before.rank]).toEqual([1, 0]);
    expect(all.end.level).toBe(progress.standingOn(D(59)).level);
  });

  it("reports a young domain over the days it has", () => {
    const r = fold(run(0, 9, 80), 9).report(30);

    expect([r.from, r.to]).toEqual([D(-21), D(8)]);
    expect([r.days, r.daysCounted, r.activeDays, r.exceptionDays]).toEqual([30, 9, 9, 0]);
    expect(r.countedBase / r.daysCounted).toBe(80);
    expect([r.before.totalXp, r.before.level, r.before.rank]).toEqual([0, 1, 0]);
    expect(r.end.totalXp).toBe(720);
    expect([r.standings[0].level, r.standings[0].counted]).toEqual([1, false]);
  });

  it("counts nothing before the start, and refuses an empty period", () => {
    const progress = fold([], 9);
    const r = progress.report(7);

    expect([r.from, r.to]).toEqual([D(2), D(8)]);
    expect([r.daysCounted, r.activeDays, r.paid]).toEqual([0, 0, 0]);
    expect([r.before.level, r.end.level, r.standings.length]).toEqual([1, 1, 7]);
    expect(() => progress.report(0)).toThrow(RangeError);
  });

  it("reads a row as GET /api/domains/report does, and an unconfigured domain's XP only", () => {
    const credit = [...run(0, 60, 80, 20).map((d, i) => (i >= 40 && i <= 42 ? { ...d, base: 20, paid: 20 } : d)), on(60, 500)];
    const row = toDomainReportRow(HEALTH_XP, credit, D(60), 30, [D(50), D(51)]);

    // 2,400 XP by day 29 is level 12; 4,620 by day 59 is level 17. Apprentice came on day 18.
    expect(row).toMatchObject({
      name: "Health",
      configured: true,
      from: D(30),
      to: D(59),
      base: 2_220,
      paid: 2_220,
      daysCounted: 28,
      exceptionDays: 2,
      activeDays: 25,
      activeShare: 0.8929,
      averageBase: 73.6,
      levelFrom: 12,
      levelTo: 17,
      rankFrom: 1,
      rankTo: 1,
      rankTitle: "Apprentice",
      bestLevelFrom: 12,
      bestLevelTo: 17,
      bestRank: 1,
      bestRankTitle: "Apprentice",
      ranksReached: [],
    });
    expect(row.ranks).toHaveLength(30);
    // 3,900 XP by day 50: level 16.
    expect(row.ranks![20]).toEqual({ day: D(50), level: 16, rank: 1, bestRank: 1, exception: true });

    const all = toDomainReportRow(HEALTH_XP, credit, D(60), 60, [D(50), D(51)]);
    expect(all.ranksReached).toEqual([{ rank: 1, title: "Apprentice", qualifiedOn: D(18) }]);

    const social = toDomainReportRow({ key: "social_xp", label: "Social XP" }, [on(57, 15), on(50, 40), on(61, 99)], D(60), 7);
    expect(social).toMatchObject({ configured: false, from: D(54), to: D(60), paid: 15, daysCounted: null, rankTo: null });
  });
});

describe("reading it as the API does", () => {
  it("answers GET /api/domains' row", () => {
    // 90 days of 60 base XP, 10 of it sleep: lifetime XP holds the level at 18 though the
    // last 90 days support 68.
    const progress = fold(run(0, 90, 60, 10), 90);
    const row = toDomainRow(HEALTH_XP, progress.totalXp, progress);

    expect(row).toMatchObject({ name: "Health", configured: true, totalXp: 5_400 });
    expect(row.lifetime).toEqual({ start: D(0), level: 18, levelXp: 584, xpIntoLevel: 538, xpToNext: 46 });
    expect(row.consistency).toEqual({
      level: 68,
      windowDays: 90,
      from: D(0),
      to: D(89),
      averageBase: 60,
      next: {
        level: 69,
        windowDays: 90,
        average: 61,
        averageNow: 60,
        averageMet: false,
        activeMinimum: 24,
        activeDaysNeeded: 70,
        activeDays: 90,
        activeDaysMet: true,
        ageNeeded: 70,
        age: 90,
        ageMet: true,
      },
    });
    expect(row.shown).toEqual({
      level: 18,
      exact: 18,
      target: 18,
      rank: 1,
      rankTitle: "Apprentice",
      limitedBy: "lifetime",
      falling: false,
      paused: false,
      fallingSince: null,
      fallingFrom: null,
      percentPerDay: 1,
      lastChange: { day: D(81), from: 17, to: 18 },
      backOnTrack: null,
    });
    expect(row.best).toEqual({ level: 18, levelOn: D(81), rank: 1, rankTitle: "Apprentice", rankOn: D(24) });
    expect(row.current).toEqual({
      todayBase: 0,
      todayAction: 0,
      activeMinimum: 12,
      todayActive: false,
      todayException: false,
      rule: "at least 12 action XP (base XP less sleep)",
      activeShare: 1,
      activeDays: 30,
      windowDays: 30,
      multiplier: 1.3,
      nextTier: null,
      settledThrough: D(89),
    });
    expect(row.ranks).toHaveLength(11);
    expect(row.ranks![0]).toMatchObject({ title: "Novice", level: 1, lastLevel: 9, average: null, gems: 0, reachedOn: null });
    expect(row.ranks![1]).toMatchObject({ title: "Apprentice", average: 25, activeMinimum: 10, activeDays: 48, reachedOn: D(24) });
    expect(row.ranks![10]).toMatchObject({
      title: "Grandmaster",
      lastLevel: 100,
      average: 80,
      activeMinimum: 32,
      activeDays: 81,
      windowDays: 90,
      totalXp: 255_686,
      gems: 5,
      reachedOn: null,
    });

    expect(toDomainRow({ key: "social_xp", label: "Social XP" }, 40, null)).toMatchObject({
      name: "Social",
      configured: false,
      lifetime: null,
      shown: null,
      ranks: null,
    });
  });

  it("answers a falling level with its exact value and what pauses it", () => {
    const progress = fold(grandmaster, 112);
    const row = toDomainRow(HEALTH_XP, progress.totalXp, progress);

    expect(row.shown).toMatchObject({
      level: 98,
      exact: 97.03,
      falling: true,
      paused: false,
      fallingSince: D(109),
      fallingFrom: 100,
      backOnTrack: { days: 7, average: averageFor(rule, 98), activeMinimum: activeMinimumFor(rule, 98), activeDays: 7 },
    });
    expect(row.best).toMatchObject({ level: 100, rank: 10, rankTitle: "Grandmaster", rankOn: D(89) });
  });

  it("answers GET /api/domains/{key}/days, oldest first, with exception comments", () => {
    const credit = [on(5, 70, 20), on(10, 50, 10, 65)];
    const read = toDomainDays(HEALTH_XP, credit, D(10), 6, [{ day: D(7), comment: "Sick" }]);

    expect([read.configured, read.today, read.settledThrough]).toEqual([true, D(10), D(9)]);
    expect(read.days.map((d) => [d.base, d.action, d.passive, d.paid, d.exception, d.counted, d.active])).toEqual([
      [70, 50, 20, 70, null, true, true],
      [0, 0, 0, 0, null, true, false],
      [0, 0, 0, 0, "Sick", false, false],
      [0, 0, 0, 0, null, true, false],
      [0, 0, 0, 0, null, true, false],
      [50, 40, 10, 65, null, false, true],
    ]);
    expect(read.days[0]).toMatchObject({ day: D(5), activeMinimum: 10, lifetimeLevel: 2, consistencyLevel: 1, level: 1, rank: 0 });
    expect(read.days[5]).toMatchObject({ day: D(10), lifetimeLevel: 2 });

    const social = toDomainDays({ key: "social_xp", label: "Social XP" }, [on(9, 25)], D(10), 2);
    expect(social.settledThrough).toBeNull();
    expect(social.days).toEqual([
      { day: D(9), base: 25, action: 25, passive: 0, paid: 25, exception: null, counted: null, active: null, activeMinimum: null, lifetimeLevel: null, consistencyLevel: null, level: null, rank: null },
      { day: D(10), base: 0, action: 0, passive: 0, paid: 0, exception: null, counted: null, active: null, activeMinimum: null, lifetimeLevel: null, consistencyLevel: null, level: null, rank: null },
    ]);
  });

  it("says which domains a write moved, bests included, from the rank below any rank-up it paid", () => {
    const steady = run(0, 90, 60, 10);
    const before = fold(steady, 90);
    const after = fold([...steady, on(90, 100_000)], 90);
    expect(domainChanges([before], [after], range(2, 5).map((rank) => ({ currency: "health_xp", rank })))).toEqual([
      {
        domain: "health_xp",
        levelFrom: 18,
        levelTo: 68,
        rankFrom: 1,
        rankTo: 6,
        rankTitle: "Elite",
        bestLevelFrom: 18,
        bestLevelTo: 68,
        bestRankFrom: 1,
        bestRankTo: 6,
      },
    ]);

    // Settling a rank time alone reached: before and after are the same fold.
    expect(domainChanges([before], [before], [{ currency: "health_xp", rank: 1 }])).toEqual([
      {
        domain: "health_xp",
        levelFrom: 9,
        levelTo: 18,
        rankFrom: 0,
        rankTo: 1,
        rankTitle: "Apprentice",
        bestLevelFrom: 9,
        bestLevelTo: 18,
        bestRankFrom: 0,
        bestRankTo: 1,
      },
    ]);
    expect(domainChanges([before], [before], [])).toEqual([]);

    // A day settling lowers the level: the bests stay.
    const unsettled = fold(grandmaster, 109, [], 108);
    const settled = fold(grandmaster, 109, [], 109);
    expect(domainChanges([unsettled], [settled], [])).toEqual([
      {
        domain: "health_xp",
        levelFrom: 100,
        levelTo: 99,
        rankFrom: 10,
        rankTo: 9,
        rankTitle: "Legend",
        bestLevelFrom: 100,
        bestLevelTo: 100,
        bestRankFrom: 10,
        bestRankTo: 10,
      },
    ]);
  });
});
