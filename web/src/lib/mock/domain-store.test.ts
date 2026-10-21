import { beforeEach, describe, expect, it } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import {
  earnDay,
  earnStatus,
  getState,
  mockDomainDays,
  mockDomains,
  mockLedger,
  mockSetBounty,
  mockSettleDomains,
  mockWallet,
  resetStore,
} from "@/lib/mock/store";
import { balanceOf } from "@/lib/rewards";

const today = () => todayIso();
const health = () => mockDomains().domains.find((d) => d.key === "health_xp")!;

describe("mock life domains (DomainService, RewardService's domain half)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("reads the seeded Health history: a level 9 Novice, 40 XP short of Apprentice", () => {
    const read = mockDomains();
    expect(read.today).toBe(today());
    expect(read.domains.map((d) => [d.key, d.configured])).toEqual([
      ["health_xp", true],
      ["social_xp", false],
      ["environment_xp", false],
      ["finance_xp", false],
    ]);
    const h = health();
    expect(h.totalXp).toBe(3_560);
    expect(h.lifetime).toMatchObject({ level: 9, rank: 0, rankTitle: "Novice", capped: false, xpToNext: 40 });
    // Apprentice's window and age are met; only the level is short.
    expect(h.nextRank).toMatchObject({ title: "Apprentice", levelReached: false, oldEnough: true, barMet: true });
    expect(h.current).toMatchObject({ todayBase: 0, successfulDays: 24, windowDays: 30, multiplier: 1.2 });
    expect(h.current!.nextTier).toEqual({ minShare: 0.85, multiplier: 1.3, daysNeeded: 2 });
    expect(read.domains[1]).toMatchObject({ name: "Social", totalXp: 0, lifetime: null });
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
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);
    expect(health().lifetime).toMatchObject({ level: 10, rank: 1, qualifiedOn: today() });
    expect(health().current).toMatchObject({ todayBase: 90, todaySucceeded: true });
    expect(mockLedger({ limit: 1 })[0]).toMatchObject({ trigger: "rank", note: "Health Apprentice", subject: null });
    const entry = getState().ledger.find((e) => e.awardKey === `day:habit-mobility:${today()}:followed`)!;
    expect(entry).toMatchObject({ creditedOn: today(), base: { health_xp: 90 }, multipliers: { health_xp: 1.2 } });
  });

  it("takes the level back with a reversal but never the rank's Gem, and never pays it twice", () => {
    earnDay("habit-mobility", "followed", today());
    const undone = earnDay("habit-mobility", "missed", today());

    expect(undone.awards.map((a) => [a.entry, a.trigger, a.amounts.health_xp])).toEqual([["reversal", "followed", -108]]);
    expect(undone.progress).toEqual([
      { domain: "health_xp", levelFrom: 10, levelTo: 9, rankFrom: 1, rankTo: 0, rankTitle: "Novice" },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);

    const again = earnDay("habit-mobility", "followed", today());
    expect(again.awards.map((a) => a.trigger)).toEqual(["followed"]);
    expect(again.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    expect(balanceOf(mockWallet(), "gems")).toBe(1);
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
    expect(read).toMatchObject({ key: "health_xp", name: "Health", configured: true, today: today(), target: 70 });
    expect(read.days).toHaveLength(30);
    expect(read.days[0].day).toBe(addDays(today(), -29));
    expect(read.days.at(-1)).toEqual({ day: today(), base: 90, paid: 108, succeeded: true });
    // Morning mobility was missed 14 days ago and partial 13 days ago.
    expect(read.days.at(-15)).toMatchObject({ base: 0, succeeded: false });
    expect(read.days.at(-14)).toMatchObject({ base: 45, succeeded: false });
    expect(mockDomainDays("Health XP", 7).days).toHaveLength(7);
    expect(mockDomainDays("social_xp", 3).days.every((d) => d.succeeded === null)).toBe(true);
    expect(() => mockDomainDays("health", 0)).toThrow("Ask for at least 1 day; got 0.");
    expect(() => mockDomainDays("health", 732)).toThrow("Ask for at most 731 days; got 732.");
    expect(() => mockDomainDays("coins")).toThrow("'coins' isn't a life domain.");
  });

  it("settles a rank time alone reached, once", () => {
    expect(mockSettleDomains()).toEqual({ awards: [], progress: [] });

    // Health XP filed on yesterday without a reconcile (as an import would): level 10 then,
    // with Apprentice's window long since met — so the rank waits for a settle.
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
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);
    expect(health().lifetime!.qualifiedOn).toBe(yesterday);
    expect(mockSettleDomains()).toEqual({ awards: [], progress: [] });
  });
});
