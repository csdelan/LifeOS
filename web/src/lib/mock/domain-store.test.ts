import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import { clearMockActivity } from "@/lib/mock/activity-store";
import { clearMockEnvironment, mockAddSpace, mockRateSpaces } from "@/lib/mock/environment-store";
import {
  earnDay,
  earnStatus,
  getState,
  mockDomainDays,
  mockDomainReport,
  mockDomains,
  mockExceptionDays,
  mockHomeWrite,
  mockLedger,
  mockMarkException,
  mockSetBounty,
  mockSettleDomains,
  mockUnmarkException,
  mockWallet,
  resetStore,
} from "@/lib/mock/store";
import { balanceOf } from "@/lib/rewards";

const today = () => todayIso();
const health = () => mockDomains().domains.find((d) => d.key === "health_xp")!;
const SICK = () => addDays(today(), -14);

describe("mock life domains (DomainService, RewardService's domain half)", () => {
  beforeEach(() => {
    // Midday, so yesterday has settled (days settle at 4 a.m.).
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
    resetStore();
    clearMockActivity();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reads the seeded Health history: a level 9 Novice, 33 XP short of Apprentice", () => {
    const read = mockDomains();
    expect(read.today).toBe(today());
    expect(read.domains.map((d) => [d.key, d.configured])).toEqual([
      ["health_xp", true],
      ["social_xp", false],
      ["environment_xp", true],
      ["finance_xp", false],
    ]);
    const h = health();
    expect(h.totalXp).toBe(1_443);
    expect(h.lifetime).toMatchObject({ level: 9, xpToNext: 33 });
    // The last 30 counted days (the sick day left out) support 23: lifetime XP holds the level.
    expect(h.consistency).toMatchObject({ level: 23, windowDays: 30, to: addDays(today(), -1) });
    expect(h.shown).toMatchObject({ level: 9, rank: 0, rankTitle: "Novice", limitedBy: "lifetime", falling: false });
    expect(h.best).toMatchObject({ level: 9, rank: 0, rankTitle: "Novice", rankOn: null });
    expect(h.current).toMatchObject({ todayBase: 0, activeMinimum: 20, activeDays: 24, windowDays: 30, multiplier: 1.2 });
    expect(h.current!.nextTier).toEqual({ minShare: 0.85, multiplier: 1.3, daysNeeded: 2 });
    expect(h.current!.settledThrough).toBe(addDays(today(), -1));
    expect(read.domains[1]).toMatchObject({ name: "Social", totalXp: 0, lifetime: null, shown: null });
  });

  it("pays today's Health XP at the multiplier, then the rank it reaches, and says what moved", () => {
    const earned = earnDay("habit-mobility", "followed", today());

    const [day, rank] = earned.awards;
    expect(day).toMatchObject({
      trigger: "followed",
      amounts: { coins: 1, freedom: 5, xp: 5, health_xp: 108 },
      base: { health_xp: 90 },
      multipliers: { health_xp: 1.2 },
    });
    expect(rank).toMatchObject({ trigger: "rank", awardKey: "rank:health_xp:10", note: "Health Apprentice", subject: null, amounts: { gems: 1 } });
    expect(earned.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 9, bestLevelTo: 10, bestRankFrom: 0, bestRankTo: 1 },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);
    expect(health().shown).toMatchObject({ level: 10, rank: 1, rankTitle: "Apprentice" });
    expect(health().best).toMatchObject({ level: 10, rank: 1, rankOn: today() });
    expect(health().current).toMatchObject({ todayBase: 90, todayAction: 90, todayActive: true });
    expect(mockLedger({ limit: 1 })[0]).toMatchObject({ trigger: "rank", note: "Health Apprentice", subject: null });
    const entry = getState().ledger.find((e) => e.awardKey === `day:habit-mobility:${today()}:followed`)!;
    expect(entry).toMatchObject({ creditedOn: today(), base: { health_xp: 90 }, multipliers: { health_xp: 1.2 } });
  });

  it("takes the level back with a reversal but never the rank's Gem, and never pays it twice", () => {
    earnDay("habit-mobility", "followed", today());
    const undone = earnDay("habit-mobility", "missed", today());

    expect(undone.awards.map((a) => [a.entry, a.trigger, a.amounts.health_xp])).toEqual([["reversal", "followed", -108]]);
    // The reversal takes back the XP that reached level 10, so the fold never reached it.
    expect(undone.progress).toEqual([
      { domain: "health_xp", levelFrom: 10, levelTo: 9, rankFrom: 1, rankTo: 0, rankTitle: "Novice", bestLevelFrom: 10, bestLevelTo: 9, bestRankFrom: 1, bestRankTo: 0 },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);

    const again = earnDay("habit-mobility", "followed", today());
    expect(again.awards.map((a) => a.trigger)).toEqual(["followed"]);
    expect(again.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 9, bestLevelTo: 10, bestRankFrom: 0, bestRankTo: 1 },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);
  });

  it("folds the home's ratings into Environment: a backdated walk-through answers the level it moves (environment plan §4.5)", () => {
    // 6,000 Environment XP and a home with no ratings: the level waits at 1 for a Home Score.
    getState().ledger.push({
      id: "cleaned",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { environment_xp: 6_000 },
      trigger: "completed",
      awardKey: "completed:task-garage",
      subjectId: "task-garage",
      creditedOn: addDays(today(), -10),
    });
    clearMockEnvironment();
    mockAddSpace({ name: "Office", weight: 5, pace: 5 });
    const environment = () => mockDomains().domains.find((d) => d.key === "environment_xp")!;
    expect(environment().shown).toMatchObject({ level: 1, limitedBy: "consistency" });

    // Rated well five days ago: five settled days kept up hold level 4 (it needs 5 so far).
    const rated = mockHomeWrite(() =>
      mockRateSpaces([{ space: "Office", tidiness: 4.5, cleanliness: 4.5, aesthetics: 4.5 }], addDays(today(), -5)),
    );
    expect(rated.ratings).toHaveLength(1);
    expect(rated.progress).toEqual([
      { domain: "environment_xp", levelFrom: 1, levelTo: 4, rankFrom: 0, rankTo: 0, rankTitle: "Novice", bestLevelFrom: 1, bestLevelTo: 4, bestRankFrom: 0, bestRankTo: 0 },
    ]);
    expect(environment().consistency).toMatchObject({ kind: "home", level: 4, windowDays: 5 });
    expect(mockDomainDays("environment").days.at(-1)).toMatchObject({ day: today(), bar: 31.1225 });

    // Rating today moves nothing yet: today hasn't settled.
    const again = mockHomeWrite(() => mockRateSpaces([{ space: "Office", same: true }]));
    expect(again.progress).toEqual([]);
  });

  it("answers no progress for a write that pays no domain XP", () => {
    expect(earnStatus("task-shoes", "Completed")).toEqual({ awards: [expect.objectContaining({ trigger: "completed" })], progress: [] });
  });

  it("prices a habit's Health XP from its stored bounty, and only configured domains multiply", () => {
    mockSetBounty({ subject: "habit-stretch", trigger: "followed", amounts: { health_xp: 30, social_xp: 20 } });
    const [award] = earnDay("habit-stretch", "followed", today()).awards;
    expect([award.amounts.health_xp, award.amounts.social_xp, award.amounts.freedom]).toEqual([36, 20, 5]);
    expect(award.base).toEqual({ health_xp: 30 });
    expect(award.multipliers).toEqual({ health_xp: 1.2 });
    expect(mockDomains().domains.find((d) => d.key === "social_xp")!.totalXp).toBe(20);
  });

  it("reads one domain's days for the chart, by key, label or name", () => {
    earnDay("habit-mobility", "followed", today());
    const read = mockDomainDays("health");
    expect(read).toMatchObject({ key: "health_xp", name: "Health", configured: true, today: today(), settledThrough: addDays(today(), -1) });
    expect(read.days).toHaveLength(30);
    expect(read.days[0].day).toBe(addDays(today(), -29));
    expect(read.days.at(-1)).toEqual({
      day: today(),
      base: 90,
      action: 90,
      passive: 0,
      paid: 108,
      exception: null,
      counted: false,
      active: true,
      activeMinimum: 20,
      lifetimeLevel: 10,
      consistencyLevel: 23,
      level: 10,
      rank: 1,
      homeScore: null,
      bar: null,
    });
    // Sick 14 days ago (marked), and partial 13 days ago: 15 action XP is short of 20.
    expect(read.days.at(-15)).toMatchObject({ day: SICK(), base: 0, exception: "Sick with a cold", counted: false, active: false });
    expect(read.days.at(-14)).toMatchObject({ base: 15, counted: true, active: false });
    expect(mockDomainDays("Health XP", 7).days).toHaveLength(7);
    expect(mockDomainDays("social_xp", 3).days.every((d) => d.active === null && d.counted === null)).toBe(true);
    expect(() => mockDomainDays("health", 0)).toThrow("Ask for at least 1 day; got 0.");
    expect(() => mockDomainDays("health", 732)).toThrow("Ask for at most 731 days; got 732.");
    expect(() => mockDomainDays("coins")).toThrow("'coins' isn't a life domain.");
  });

  it("reports every domain over a period ending at the last settled day: Health in full, the others their XP", () => {
    const report = mockDomainReport(30);
    expect([report.today, report.days]).toEqual([today(), 30]);
    expect(report.domains.map((d) => d.key)).toEqual(["health_xp", "social_xp", "environment_xp", "finance_xp"]);
    expect(report.domains[0]).toMatchObject({
      configured: true,
      from: addDays(today(), -30),
      to: addDays(today(), -1),
      daysCounted: 29,
      exceptionDays: 1,
      activeDays: 24,
      activeShare: 0.8276,
      averageBase: 45.5,
      levelFrom: 1,
      levelTo: 9,
      rankTitle: "Novice",
      bestRankTitle: "Novice",
      ranksReached: [],
    });
    expect(report.domains[0].ranks).toHaveLength(30);
    expect(report.domains[0].ranks!.filter((r) => r.exception).map((r) => r.day)).toEqual([SICK()]);
    // Health's 31 days of history, inside a 90-day period.
    expect(mockDomainReport(90).domains[0]).toMatchObject({ daysCounted: 30, activeDays: 24, levelFrom: 1 });
    expect(report.domains[1]).toMatchObject({ configured: false, paid: 0, daysCounted: null, activeShare: null });
    expect(() => mockDomainReport(0)).toThrow("Ask for at least 1 day; got 0.");
    expect(() => mockDomainReport(732)).toThrow("Ask for at most 731 days; got 732.");
  });

  it("settles a rank time alone reached, once", () => {
    expect(mockSettleDomains()).toEqual({ awards: [], progress: [] });

    // Health XP filed on yesterday without a reconcile (as an import would): level 10 then,
    // so the rank waits for a settle.
    const yesterday = addDays(today(), -1);
    getState().ledger.push({
      id: "imported",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { health_xp: 100 },
      trigger: "completed",
      awardKey: "completed:task-longrun",
      subjectId: "task-longrun",
      creditedOn: yesterday,
    });

    const settled = mockSettleDomains();
    expect(settled.awards).toEqual([expect.objectContaining({ awardKey: "rank:health_xp:10", amounts: { gems: 1 } })]);
    expect(settled.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 9, bestLevelTo: 10, bestRankFrom: 0, bestRankTo: 1 },
    ]);
    expect(health().best!.rankOn).toBe(yesterday);
    expect(mockSettleDomains()).toEqual({ awards: [], progress: [] });
  });
});

describe("mock exception days (ExceptionDayService)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
    resetStore();
    clearMockActivity();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("lists the last 90 days' marks, newest first", () => {
    const read = mockExceptionDays();
    expect([read.today, read.from]).toEqual([today(), addDays(today(), -89)]);
    expect(read.exceptions).toEqual([{ day: SICK(), comment: "Sick with a cold", markedAt: expect.any(String) }]);
    expect(mockExceptionDays(7).exceptions).toEqual([]);
    expect(() => mockExceptionDays(0)).toThrow("Ask for 1 to 731 days; got 0.");
  });

  it("marks today with a trimmed comment, and today then neither counts nor is active", () => {
    earnDay("habit-mobility", "followed", today());
    const marked = mockMarkException("  Flying home  ");

    expect(marked.exception).toMatchObject({ day: today(), comment: "Flying home" });
    // Lifetime XP still counts, so the level and the rank stay.
    expect(marked.progress).toEqual([]);
    expect(marked.awards).toEqual([]);
    expect(mockExceptionDays().exceptions.map((e) => e.day)).toEqual([today(), SICK()]);
    expect(health().current).toMatchObject({ todayException: true, todayActive: false, todayBase: 90 });
    expect(health().shown).toMatchObject({ level: 10, rank: 1 });
    expect(mockDomainDays("health").days.at(-1)).toMatchObject({ exception: "Flying home", counted: false, active: false });
  });

  it("writes nothing for the same comment again, and replaces a different one", () => {
    const first = mockMarkException("Sick with a cold", SICK());
    expect(first).toEqual({ exception: mockExceptionDays().exceptions[0], awards: [], progress: [] });

    const changed = mockMarkException("Flu, not a cold", SICK());
    expect(changed.exception).toMatchObject({ day: SICK(), comment: "Flu, not a cold" });
    expect(mockExceptionDays().exceptions).toEqual([changed.exception]);
  });

  it("refuses an empty, multi-line, too long or future comment's day", () => {
    expect(() => mockMarkException("   ")).toThrow("the comment is required");
    expect(() => mockMarkException("Sick\nall day")).toThrow("one line");
    expect(() => mockMarkException("Sick all day")).toThrow("one line");
    expect(() => mockMarkException("x".repeat(141))).toThrow("up to 140 characters; that one is 141");
    expect(mockMarkException("x".repeat(140), addDays(today(), -3)).exception!.comment).toHaveLength(140);
    expect(() => mockMarkException("Trip", addDays(today(), 1))).toThrow("hasn't happened yet");
    expect(() => mockMarkException("Trip", "next week")).toThrow("isn't a date");
  });

  it("unmarks a day so it counts again, and an unmarked day writes nothing", () => {
    const unmarked = mockUnmarkException(SICK());
    expect(unmarked).toEqual({ exception: null, awards: [], progress: [] });
    expect(mockExceptionDays().exceptions).toEqual([]);
    expect(mockDomainDays("health").days.at(-15)).toMatchObject({ day: SICK(), exception: null, counted: true, active: false });
    // The sick day now counts against the window: 31 days, a lower average.
    expect(health().consistency).toMatchObject({ windowDays: 31 });
    expect(health().consistency!.averageBase).toBeLessThan(44.5);

    expect(mockUnmarkException(SICK())).toEqual({ exception: null, awards: [], progress: [] });
  });
});
