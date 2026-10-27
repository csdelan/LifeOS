/**
 * Life-domain progression, levels v2 (REWARD-6 / REWARD-7 / REWARD-8, D12, D17, xp-domains
 * plan §7): a TypeScript port of the kernel's `DomainWalker`, `DomainProgress` and Health's
 * rules (`LifeDomains.Health`), so the mock store reads `/api/domains` the way the API does.
 * `domain-fold.test.ts` runs it on the C# tests' examples.
 *
 * Everything is worked in whole numbers (shares in basis points, the shown level in
 * ten-thousandths, multipliers in hundredths), so it rounds exactly as the C# decimals do.
 */
import type {
  DomainChange,
  DomainDayRow,
  DomainDaysRead,
  DomainRankRow,
  DomainReportRow,
  DomainRow,
} from "@/lib/production-ui-types";

// --- Rules -------------------------------------------------------------------

/** The total XP that reaches level L: `⌈Linear·n + Quadratic·C(n,2) + Cubic·C(n,3)⌉`, n = L − 1. */
export interface CubicCurve {
  linear: number;
  quadratic: number;
  /** Two places at most (0.85). */
  cubic: number;
  maxLevel: number;
}

/**
 * What the last days must show for a level to hold (§7.2): an average base XP running from
 * `averageFrom` at level 2 to `averageTo` at `maxLevel`, an active day taking `activeShare` of
 * it in action XP (at least `activeFloor`), and a share of active days from `shareFrom` to
 * `shareTo`. Shares are 0–1, four places at most.
 */
export interface ConsistencyRule {
  windowDays: number;
  averageFrom: number;
  averageTo: number;
  activeShare: number;
  activeFloor: number;
  shareFrom: number;
  shareTo: number;
  maxLevel: number;
}

/** How the shown level falls: `percentPerDay` of itself a day, after `holdDays`, paused while back on track. */
export interface FallRule {
  percentPerDay: number;
  holdDays: number;
  backOnTrackDays: number;
}

export interface RankRule {
  startLevel: number;
  title: string;
}

export interface MultiplierTier {
  minShare: number;
  multiplier: number;
}

export interface DomainRules {
  currency: string;
  curve: CubicCurve;
  consistency: ConsistencyRule;
  fall: FallRule;
  ranks: readonly RankRule[];
  multiplier: { windowDays: number; tiers: readonly MultiplierTier[] };
  rankUpGems: { perRank: number; top: number };
  /** The award triggers whose XP is passive (sleep): a day's action XP is its base less these. */
  passiveTriggers: readonly string[];
}

/** Health, the pilot domain (xp-domains plan §7.2), as `LifeDomains.Health` sets it. */
export const HEALTH: DomainRules = {
  currency: "health_xp",
  curve: { linear: 60, quadratic: 24, cubic: 0.85, maxLevel: 100 },
  consistency: {
    windowDays: 90,
    averageFrom: 30,
    averageTo: 80,
    activeShare: 0.4,
    activeFloor: 20,
    shareFrom: 0.5,
    shareTo: 0.9,
    maxLevel: 100,
  },
  fall: { percentPerDay: 1, holdDays: 0, backOnTrackDays: 7 },
  ranks: [
    { startLevel: 1, title: "Novice" },
    { startLevel: 10, title: "Apprentice" },
    { startLevel: 20, title: "Journeyman" },
    { startLevel: 30, title: "Adept" },
    { startLevel: 40, title: "Expert" },
    { startLevel: 50, title: "Veteran" },
    { startLevel: 60, title: "Elite" },
    { startLevel: 70, title: "Master" },
    { startLevel: 80, title: "Champion" },
    { startLevel: 90, title: "Legend" },
    { startLevel: 100, title: "Grandmaster" },
  ],
  multiplier: {
    windowDays: 30,
    tiers: [
      { minShare: 0.85, multiplier: 1.3 },
      { minShare: 0.7, multiplier: 1.2 },
      { minShare: 0.5, multiplier: 1.1 },
    ],
  },
  rankUpGems: { perRank: 1, top: 5 },
  passiveTriggers: ["sleep"],
};

/** Every configured domain. */
export const CONFIGURED_DOMAINS: readonly DomainRules[] = [HEALTH];

/** Every configured domain's passive triggers, once each: what the credit read splits out. */
export const PASSIVE_TRIGGERS: readonly string[] = [...new Set(CONFIGURED_DOMAINS.flatMap((r) => r.passiveTriggers))];

/** The rules for a currency, or null for a domain that only collects XP. */
export function rulesFor(currency: string): DomainRules | null {
  return CONFIGURED_DOMAINS.find((r) => r.currency === currency.trim().toLowerCase()) ?? null;
}

/** The trigger of a rank-up award (§3.7). */
export const RANK_TRIGGER = "rank";

/** A level in ten-thousandths: level 47 is 470,000. */
export const LEVEL_SCALE = 10_000;

// --- Whole-number arithmetic ---------------------------------------------------

const bp = (share: number) => Math.round(share * 10_000);

/** `num / den` rounded half away from zero (den > 0). */
function roundHalfAway(num: number, den: number): number {
  const q = Math.floor((2 * Math.abs(num) + den) / (2 * den));
  return num < 0 ? -q : q;
}

/** `⌈num / den⌉` for num ≥ 0, den > 0. */
function ceilDiv(num: number, den: number): number {
  return Math.floor((num + den - 1) / den);
}

/** `num / den` (both ≥ 0, den > 0) to `places`, halves away from zero or to even. */
function roundRatio(num: number, den: number, places: number, toEven = false): number {
  if (den === 0) return 0;
  const scale = 10 ** places;
  const scaled = num * scale;
  let q = Math.floor(scaled / den);
  const twice = 2 * (scaled - q * den);
  if (twice > den || (twice === den && (!toEven || q % 2 === 1))) q++;
  return q / scale;
}

/** `Math.Round(x, 1, AwayFromZero)` of a ratio. */
const round1 = (num: number, den: number) => roundRatio(num, den, 1);
/** `Math.Round(x, 4)` (to even) of a ratio, as the API rounds shares. */
const round4 = (num: number, den: number) => roundRatio(num, den, 4, true);

// --- The level curve -----------------------------------------------------------

function checkLevel(curve: CubicCurve, level: number) {
  if (!Number.isInteger(level) || level < 1 || level > curve.maxLevel) {
    throw new RangeError(`Levels run from 1 to ${curve.maxLevel}; got ${level}.`);
  }
}

/** The total XP that reaches `level` (0 for level 1). */
export function totalFor(curve: CubicCurve, level: number): number {
  checkLevel(curve, level);
  const n = level - 1;
  const pairs = (n * (n - 1)) / 2;
  const triples = (n * (n - 1) * (n - 2)) / 6;
  return curve.linear * n + curve.quadratic * pairs + ceilDiv(Math.round(curve.cubic * 100) * triples, 100);
}

/** The XP to go from `level` to the next; 0 at the top. */
export function costToNext(curve: CubicCurve, level: number): number {
  checkLevel(curve, level);
  return level === curve.maxLevel ? 0 : totalFor(curve, level + 1) - totalFor(curve, level);
}

/** The level `totalXp` reaches, at most the top. */
export function levelFor(curve: CubicCurve, totalXp: number): number {
  let low = 1;
  let high = curve.maxLevel;
  while (low < high) {
    const middle = Math.floor((low + high + 1) / 2);
    if (totalFor(curve, middle) <= totalXp) low = middle;
    else high = middle - 1;
  }
  return low;
}

// --- Consistency and the fall ----------------------------------------------------

/** The average base XP a day `level` needs: 30 at level 2, 80 at 100 for Health. */
export function averageFor(rule: ConsistencyRule, level: number): number {
  return rule.averageFrom + roundHalfAway((rule.averageTo - rule.averageFrom) * (level - 2), rule.maxLevel - 2);
}

/** The action XP that makes a day active at `level`: 20 to 32 for Health. */
export function activeMinimumFor(rule: ConsistencyRule, level: number): number {
  return Math.max(rule.activeFloor, roundHalfAway(bp(rule.activeShare) * averageFor(rule, level), 10_000));
}

/** The active days `level` needs in a window of `days`: `⌈P(L) · N⌉`, exactly. */
export function activeDaysNeededFor(rule: ConsistencyRule, level: number, days: number): number {
  const span = rule.maxLevel - 2;
  const from = bp(rule.shareFrom);
  const to = bp(rule.shareTo);
  return ceilDiv(days * (from * span + (to - from) * (level - 2)), span * 10_000);
}

/** The active days so far `level` needs: `min(WindowDays, L + 1)`. */
export function ageNeededFor(rule: ConsistencyRule, level: number): number {
  return Math.min(rule.windowDays, level + 1);
}

function qualifies(
  rule: ConsistencyRule,
  level: number,
  activeSoFar: number,
  days: number,
  windowBase: number,
  windowActive: number,
): boolean {
  return (
    days > 0 &&
    activeSoFar >= ageNeededFor(rule, level) &&
    windowBase >= averageFor(rule, level) * days &&
    windowActive >= activeDaysNeededFor(rule, level, days)
  );
}

/** The shown level a day's fall leaves, before the target's floor: `⌊Ŝ · (100 − p) / 100⌋`. */
export function fallNext(rule: FallRule, exact: number): number {
  return Math.floor((exact * (10_000 - Math.round(rule.percentPerDay * 100))) / 10_000);
}

/** The whole level `exact` (ten-thousandths) shows: rounded up. */
export function levelOf(exact: number): number {
  return Math.floor((exact + LEVEL_SCALE - 1) / LEVEL_SCALE);
}

/** The active day in words at `level`: "at least 17 action XP (base XP less sleep)". */
export function describeRule(rules: DomainRules, level: number): string {
  return `at least ${activeMinimumFor(rules.consistency, level)} action XP (base XP less sleep)`;
}

// --- Rules helpers -------------------------------------------------------------

/** The rank `level` falls in: an index into the ranks. */
export function rankFor(rules: DomainRules, level: number): number {
  let rank = 0;
  for (let i = 1; i < rules.ranks.length && rules.ranks[i].startLevel <= level; i++) rank = i;
  return rank;
}

/** The last level of `rank`: 9 for Novice. */
export function topLevelOf(rules: DomainRules, rank: number): number {
  return rank + 1 < rules.ranks.length ? rules.ranks[rank + 1].startLevel - 1 : rules.curve.maxLevel;
}

/** The Gems for the best rank first reaching `rank`: the top one pays more. */
export function gemsFor(rules: DomainRules, rank: number): number {
  return rank === rules.ranks.length - 1 ? rules.rankUpGems.top : rules.rankUpGems.perRank;
}

/** `rank:health_xp:10`. */
export function rankAwardKey(rules: DomainRules, rank: number): string {
  return `${RANK_TRIGGER}:${rules.currency}:${rules.ranks[rank].startLevel}`;
}

/** "Health" for `Health XP`. */
export function domainName(label: string): string {
  return label.split(" ")[0];
}

/** The multiplier a window's active share earns: the highest tier it reaches, else 1.0. */
function multiplierOf(rules: DomainRules, active: number, days: number): number {
  let best = 1;
  for (const tier of rules.multiplier.tiers) {
    // active / days ≥ minShare, in whole numbers (an empty window has share 0).
    const reaches = days === 0 ? bp(tier.minShare) <= 0 : active * 10_000 >= bp(tier.minShare) * days;
    if (reaches && tier.multiplier > best) best = tier.multiplier;
  }
  return best;
}

function nextTierAbove(rules: DomainRules, active: number, days: number): MultiplierTier | null {
  const above = rules.multiplier.tiers
    .filter((t) => (days === 0 ? bp(t.minShare) > 0 : active * 10_000 < bp(t.minShare) * days))
    .sort((a, b) => a.minShare - b.minShare);
  return above[0] ?? null;
}

/** What `amount` pays at `multiplier` (§3.6): whole XP, halves rounding away from zero. */
export function applyMultiplier(amount: number, multiplier: number): number {
  const hundredths = Math.round(multiplier * 100);
  const product = Math.abs(amount) * hundredths;
  return Math.sign(amount) * Math.floor((product + 50) / 100);
}

// --- Days ----------------------------------------------------------------------

const DAY_MS = 86_400_000;

/** Days since 1970-01-01 for a `yyyy-MM-dd` date. */
export function dayNumber(day: string): number {
  const [y, m, d] = day.split("-").map(Number);
  return Math.round(Date.UTC(y, m - 1, d) / DAY_MS);
}

export function dayFrom(n: number): string {
  return new Date(n * DAY_MS).toISOString().slice(0, 10);
}

export function shiftDay(day: string, days: number): string {
  return dayFrom(dayNumber(day) + days);
}

// --- The walk ------------------------------------------------------------------

/** One day's credit: base before any multiplier, paid as paid, and the passive (sleep) part of the base. */
export interface DomainDay {
  day: string;
  base: number;
  paid: number;
  passive?: number;
}

/** One calendar day of a domain's walk and its credit; its action XP is `base − passive`. */
export interface DomainPeriod {
  day: string;
  base: number;
  paid: number;
  passive: number;
}

/** Where a domain stood at the end of a day (`DomainStanding`). */
export interface DomainStanding {
  day: string;
  totalXp: number;
  lifetimeLevel: number;
  consistencyLevel: number;
  target: number;
  /** The shown level in ten-thousandths. */
  shownExact: number;
  level: number;
  rank: number;
  bestLevel: number;
  bestRank: number;
  counted: boolean;
  exception: boolean;
  active: boolean;
  falling: boolean;
  paused: boolean;
  activeMinimum: number;
}

/** A rank the best rank reached (never the first), and the first day it did. */
export interface RankReached {
  rank: number;
  rule: RankRule;
  qualifiedOn: string;
}

/** The counted days the consistency level reads: the last `days` (at most 90) and their base XP. */
export interface CountedWindow {
  days: number;
  from: string | null;
  to: string | null;
  base: number;
}

/** What `level` needs over the window, against where each test stands. */
export interface ConsistencyNeed {
  level: number;
  windowDays: number;
  average: number;
  baseNeeded: number;
  baseNow: number;
  activeMinimum: number;
  activeDaysNeeded: number;
  activeDaysNow: number;
  ageNeeded: number;
  ageNow: number;
  averageMet: boolean;
  activeDaysMet: boolean;
  ageMet: boolean;
}

/**
 * The day-by-day walk (`DomainWalker`, xp-domains plan §7.4.4): it steps one calendar day at
 * a time from the domain's start, carrying lifetime XP, the running sums the consistency tests
 * read, the shown level and the bests.
 */
export class DomainWalker {
  readonly rules: DomainRules;
  readonly ranksReached: RankReached[] = [];
  private readonly maxLevel: number;
  private readonly minimums: number[];
  private readonly minimumOf: number[];
  private readonly counted: string[] = [];
  private readonly baseSums: number[] = [0];
  private readonly activeSums: number[][];
  private readonly successSums: number[] = [0];
  private total = 0;
  private shown = LEVEL_SCALE;
  private consistency = 1;
  private below = 0;
  private bestLevel = 1;
  private bestRank = 0;

  constructor(rules: DomainRules) {
    this.rules = rules;
    this.maxLevel = Math.min(rules.curve.maxLevel, rules.consistency.maxLevel);
    const all: number[] = [];
    for (let level = 1; level <= this.maxLevel; level++) all.push(activeMinimumFor(rules.consistency, level));
    this.minimums = [...new Set(all)].sort((a, b) => a - b);
    this.minimumOf = [0, ...all.map((m) => this.minimums.indexOf(m))];
    this.activeSums = this.minimums.map(() => [0]);
  }

  /** The shown level so far, as a whole level. */
  get level(): number {
    return levelOf(this.shown);
  }

  /** The multiplier XP credited to the next day would be paid at: the active share of the last 30 non-exception days walked. */
  nextMultiplier(): number {
    const window = this.rules.multiplier.windowDays;
    if (window === 0) return multiplierOf(this.rules, 0, 0);
    const walked = this.successSums.length - 1;
    const active = this.successSums[walked] - this.successSums[Math.max(0, walked - window)];
    return multiplierOf(this.rules, active, window);
  }

  /** Walks one calendar day; days must be stepped in order from the domain's start. */
  step(period: DomainPeriod, exception: boolean, settled: boolean): DomainStanding {
    const { rules } = this;
    const day = period.day;
    const levelBefore = this.level;
    this.total += period.paid;
    const lifetime = levelFor(rules.curve, this.total);
    const action = period.base - period.passive;

    const activeMinimum = activeMinimumFor(rules.consistency, levelBefore);
    const active = !exception && action >= activeMinimum;
    if (!exception) this.successSums.push(this.successSums.at(-1)! + (active ? 1 : 0));

    const isCounted = !exception && settled;
    if (isCounted) {
      this.counted.push(day);
      this.baseSums.push(this.baseSums.at(-1)! + period.base);
      this.minimums.forEach((minimum, i) => {
        const sums = this.activeSums[i];
        sums.push(sums.at(-1)! + (action >= minimum ? 1 : 0));
      });
      this.consistency = this.consistencyLevelNow();
    }

    const target = Math.min(lifetime, this.consistency);
    let falling = false;
    let paused = false;
    if (target >= levelBefore) {
      this.shown = target * LEVEL_SCALE;
      this.below = 0;
    } else if (isCounted) {
      this.below++;
      if (this.backOnTrack(levelBefore)) {
        paused = true;
      } else if (this.below > rules.fall.holdDays) {
        const next = Math.max(target * LEVEL_SCALE, fallNext(rules.fall, this.shown));
        falling = next < this.shown;
        this.shown = next;
      }
    } else if (exception) {
      paused = true;
    }

    const level = this.level;
    const rank = rankFor(rules, level);
    this.bestLevel = Math.max(this.bestLevel, level);
    while (this.bestRank < rank) {
      this.bestRank++;
      this.ranksReached.push({ rank: this.bestRank, rule: rules.ranks[this.bestRank], qualifiedOn: day });
    }

    return {
      day,
      totalXp: this.total,
      lifetimeLevel: lifetime,
      consistencyLevel: this.consistency,
      target,
      shownExact: this.shown,
      level,
      rank,
      bestLevel: this.bestLevel,
      bestRank: this.bestRank,
      counted: isCounted,
      exception,
      active,
      falling,
      paused,
      activeMinimum,
    };
  }

  /** Whether the last 7 counted days pass `level`'s average and active-day tests (no age test). */
  backOnTrack(level: number): boolean {
    const days = this.rules.fall.backOnTrackDays;
    const k = this.counted.length;
    if (days <= 0 || k < days) return false;
    const sums = this.activeSums[this.minimumOf[this.clamp(level)]];
    const rule = this.rules.consistency;
    return (
      this.baseSums[k] - this.baseSums[k - days] >= averageFor(rule, level) * days &&
      sums[k] - sums[k - days] >= activeDaysNeededFor(rule, level, days)
    );
  }

  /** The counted days the consistency level reads now: the last `min(90, counted days)`. */
  window(): CountedWindow {
    const k = this.counted.length;
    const n = Math.min(this.rules.consistency.windowDays, k);
    return n === 0
      ? { days: 0, from: null, to: null, base: 0 }
      : { days: n, from: this.counted[k - n], to: this.counted[k - 1], base: this.baseSums[k] - this.baseSums[k - n] };
  }

  /** What `level` needs over the window now, against where each test stands. */
  needFor(level: number): ConsistencyNeed {
    const rule = this.rules.consistency;
    const k = this.counted.length;
    const n = Math.min(rule.windowDays, k);
    const sums = this.activeSums[this.minimumOf[this.clamp(level)]];
    const average = averageFor(rule, level);
    const baseNeeded = average * n;
    const baseNow = this.baseSums[k] - this.baseSums[k - n];
    const activeDaysNeeded = activeDaysNeededFor(rule, level, n);
    const activeDaysNow = sums[k] - sums[k - n];
    const ageNeeded = ageNeededFor(rule, level);
    const ageNow = sums[k];
    return {
      level,
      windowDays: n,
      average,
      baseNeeded,
      baseNow,
      activeMinimum: activeMinimumFor(rule, level),
      activeDaysNeeded,
      activeDaysNow,
      ageNeeded,
      ageNow,
      averageMet: n > 0 && baseNow >= baseNeeded,
      activeDaysMet: n > 0 && activeDaysNow >= activeDaysNeeded,
      ageMet: ageNow >= ageNeeded,
    };
  }

  private clamp(level: number): number {
    return Math.min(Math.max(level, 1), this.maxLevel);
  }

  /** Levels are tested upward from 2, and the first that fails ends the walk. */
  private consistencyLevelNow(): number {
    const rule = this.rules.consistency;
    const k = this.counted.length;
    const n = Math.min(rule.windowDays, k);
    if (n === 0) return 1;
    const windowBase = this.baseSums[k] - this.baseSums[k - n];
    let level = 1;
    for (let candidate = 2; candidate <= this.maxLevel; candidate++) {
      const sums = this.activeSums[this.minimumOf[candidate]];
      if (!qualifies(rule, candidate, sums[k], n, windowBase, sums[k] - sums[k - n])) break;
      level = candidate;
    }
    return level;
  }
}

// --- The fold ------------------------------------------------------------------

/** The multiplier's window: the `days` non-exception days before a day; days before the start count as not active. */
export class ConsistencyWindow {
  readonly from: string;
  readonly to: string;
  readonly days: number;
  readonly coveredDays: number;
  readonly successfulDays: number;

  constructor(from: string, to: string, days: number, coveredDays: number, successfulDays: number) {
    this.from = from;
    this.to = to;
    this.days = days;
    this.coveredDays = coveredDays;
    this.successfulDays = successfulDays;
  }

  /** The active share, 0–1. */
  get share(): number {
    return this.days === 0 ? 0 : this.successfulDays / this.days;
  }

  get covered(): boolean {
    return this.coveredDays === this.days;
  }

  /** The active days a share takes in this window: `⌈share × days⌉`, exactly. */
  required(share: number): number {
    return ceilDiv(bp(share) * this.days, 10_000);
  }

  reaches(share: number): boolean {
    return this.successfulDays >= this.required(share);
  }

  daysNeededFor(share: number): number {
    return Math.max(0, this.required(share) - this.successfulDays);
  }
}

/** A day the shown level moved. */
export interface LevelChange {
  day: string;
  from: number;
  to: number;
}

/** `DomainPeriodReport`: a configured domain over `days` calendar days ending at the last settled day. */
export interface DomainPeriodReport {
  days: number;
  from: string;
  to: string;
  daysCounted: number;
  exceptionDays: number;
  activeDays: number;
  countedBase: number;
  base: number;
  paid: number;
  before: DomainStanding;
  end: DomainStanding;
  ranksReached: RankReached[];
  standings: DomainStanding[];
}

/**
 * A life domain's progression, folded from its daily credit (`DomainProgress`, D12, D17): the
 * lifetime level, the consistency level the last 90 counted days support, the shown level that
 * follows the lower of the two (rising at once, falling 1% a day), its rank, and the bests.
 */
export class DomainProgress {
  readonly rules: DomainRules;
  readonly today: string;
  /** The last settled day: yesterday from 4 a.m., the day before until then. */
  readonly settledThrough: string;
  readonly start: string | null;
  readonly periods: DomainPeriod[];
  readonly standings: DomainStanding[];
  readonly exceptionDays: ReadonlySet<string>;
  readonly ranksReached: RankReached[];
  readonly now: DomainStanding;
  readonly xpIntoLevel: number;
  readonly xpToNext: number;
  readonly window: CountedWindow;
  readonly nextConsistency: ConsistencyNeed | null;
  readonly limitedBy: "lifetime" | "consistency" | null;
  readonly falling: boolean;
  readonly paused: boolean;
  readonly fallingSince: string | null = null;
  readonly fallingFrom: number | null = null;
  readonly lastChange: LevelChange | null = null;
  readonly bestLevelOn: string | null;
  readonly consistency: ConsistencyWindow;
  readonly multiplierNow: number;
  readonly nextTier: MultiplierTier | null;
  private readonly startNumber: number | null;

  private constructor(
    rules: DomainRules,
    today: string,
    settledThrough: string,
    start: string | null,
    periods: DomainPeriod[],
    exceptions: Set<string>,
  ) {
    this.rules = rules;
    this.today = today;
    this.settledThrough = settledThrough;
    this.start = start;
    this.startNumber = start ? dayNumber(start) : null;
    this.periods = periods;
    this.exceptionDays = exceptions;

    const walker = new DomainWalker(rules);
    this.standings = periods.map((p) => walker.step(p, exceptions.has(p.day), p.day <= settledThrough));
    this.ranksReached = [...walker.ranksReached];
    const now = this.standingOn(today);
    this.now = now;

    const curve = rules.curve;
    this.xpIntoLevel = now.totalXp - totalFor(curve, now.lifetimeLevel);
    this.xpToNext = now.lifetimeLevel === curve.maxLevel ? 0 : totalFor(curve, now.lifetimeLevel + 1) - now.totalXp;

    this.window = walker.window();
    this.nextConsistency =
      now.consistencyLevel < Math.min(curve.maxLevel, rules.consistency.maxLevel)
        ? walker.needFor(now.consistencyLevel + 1)
        : null;

    this.limitedBy =
      now.lifetimeLevel < now.consistencyLevel
        ? "lifetime"
        : now.consistencyLevel < now.lifetimeLevel
          ? "consistency"
          : null;
    const above = now.level > now.target;
    this.paused = above && (this.todayException || walker.backOnTrack(now.level));
    this.falling = above && !this.paused;
    const s = this.standings;
    if (above) {
      let i = s.length - 1;
      while (i > 0 && s[i - 1].level > s[i - 1].target) i--;
      this.fallingSince = s[i].day;
      this.fallingFrom = i > 0 ? s[i - 1].level : 1;
    }

    for (let i = s.length - 1; i >= 0; i--) {
      const before = i > 0 ? s[i - 1].level : 1;
      if (s[i].level !== before) {
        this.lastChange = { day: s[i].day, from: before, to: s[i].level };
        break;
      }
    }

    this.bestLevelOn = s.find((x) => x.level === now.bestLevel)?.day ?? null;

    this.consistency = this.windowBefore(today);
    this.multiplierNow = multiplierOf(rules, this.consistency.successfulDays, this.consistency.days);
    this.nextTier = nextTierAbove(rules, this.consistency.successfulDays, this.consistency.days);
  }

  /**
   * Folds one domain's credit up to `today` (home zone); credit after today is left out.
   * `exceptions` are the marked days, and `settledThrough` the last settled day (yesterday
   * when not given); days after it don't count yet.
   */
  static fold(
    rules: DomainRules,
    days: Iterable<DomainDay>,
    today: string,
    exceptions: Iterable<string> = [],
    settledThrough?: string,
  ): DomainProgress {
    let settled = settledThrough ?? shiftDay(today, -1);
    if (settled > today) settled = today;
    const marked = new Set([...exceptions].filter((d) => d <= today));

    const credit = new Map<string, { base: number; paid: number; passive: number }>();
    for (const d of days) {
      if (d.day > today) continue;
      const sum = credit.get(d.day) ?? { base: 0, paid: 0, passive: 0 };
      credit.set(d.day, { base: sum.base + d.base, paid: sum.paid + d.paid, passive: sum.passive + (d.passive ?? 0) });
    }
    const paidDays = [...credit].filter(([, c]) => c.paid > 0).map(([day]) => day).sort();
    if (paidDays.length === 0) return new DomainProgress(rules, today, settled, null, [], marked);

    const start = paidDays[0];
    const periods: DomainPeriod[] = [];
    for (let n = dayNumber(start); n <= dayNumber(today); n++) {
      const day = dayFrom(n);
      const c = credit.get(day) ?? { base: 0, paid: 0, passive: 0 };
      periods.push({ day, ...c });
    }
    return new DomainProgress(rules, today, settled, start, periods, marked);
  }

  get totalXp(): number {
    return this.now.totalXp;
  }

  get lifetimeLevel(): number {
    return this.now.lifetimeLevel;
  }

  get consistencyLevel(): number {
    return this.now.consistencyLevel;
  }

  /** The shown level. */
  get level(): number {
    return this.now.level;
  }

  get shownExact(): number {
    return this.now.shownExact;
  }

  get target(): number {
    return this.now.target;
  }

  get rank(): number {
    return this.now.rank;
  }

  get rankRule(): RankRule {
    return this.rules.ranks[this.rank];
  }

  get bestLevel(): number {
    return this.now.bestLevel;
  }

  get bestRank(): number {
    return this.now.bestRank;
  }

  get bestRankRule(): RankRule {
    return this.rules.ranks[this.bestRank];
  }

  get todayException(): boolean {
    return this.exceptionDays.has(this.today);
  }

  get todayPeriod(): DomainPeriod | null {
    const last = this.periods.at(-1);
    return last && last.day === this.today ? last : null;
  }

  get todayBase(): number {
    return this.todayPeriod?.base ?? 0;
  }

  get todayAction(): number {
    const p = this.todayPeriod;
    return p ? p.base - p.passive : 0;
  }

  /** The action XP that makes today active: the minimum of the shown level going into today. */
  get activeMinimum(): number {
    return this.now.activeMinimum;
  }

  get todayActive(): boolean {
    return this.now.active;
  }

  /** Whether `day` was active; days before the start and after today never were. */
  activeOn(day: string): boolean {
    const i = this.indexOf(day);
    return i != null && this.standings[i].active;
  }

  /** Where the domain stood at the end of `day`: level 1 before the start, as today for any later day. */
  standingOn(day: string): DomainStanding {
    if (this.startNumber == null || day < this.start!) {
      return {
        day,
        totalXp: 0,
        lifetimeLevel: 1,
        consistencyLevel: 1,
        target: 1,
        shownExact: LEVEL_SCALE,
        level: 1,
        rank: 0,
        bestLevel: 1,
        bestRank: 0,
        counted: false,
        exception: this.exceptionDays.has(day),
        active: false,
        falling: false,
        paused: false,
        activeMinimum: activeMinimumFor(this.rules.consistency, 1),
      };
    }
    const i = Math.min(dayNumber(day) - this.startNumber, this.standings.length - 1);
    return { ...this.standings[i], day };
  }

  /** The multiplier's window for XP credited to `day`: the 30 complete non-exception days before it. */
  windowBefore(day: string): ConsistencyWindow {
    const size = this.rules.multiplier.windowDays;
    const to = shiftDay(day, -1);
    let from = day;
    let taken = 0;
    let covered = 0;
    let active = 0;
    for (let n = dayNumber(to); taken < size; n--) {
      const d = dayFrom(n);
      if (this.exceptionDays.has(d)) continue;
      const i = this.indexOf(d);
      if (i != null) {
        covered++;
        active += this.standings[i].active ? 1 : 0;
      }
      taken++;
      from = d;
    }
    return new ConsistencyWindow(from, to, size, covered, active);
  }

  /** The multiplier for XP credited to `day`: the tier of the window before it. */
  multiplierFor(day: string): number {
    const w = this.windowBefore(day);
    return multiplierOf(this.rules, w.successfulDays, w.days);
  }

  /** The domain over the last `days` calendar days ending at the last settled day. */
  report(days: number): DomainPeriodReport {
    if (days < 1) throw new RangeError(`A report covers at least one day; got ${days}.`);
    const to = this.settledThrough;
    const from = shiftDay(to, 1 - days);
    let counted = 0;
    let exceptionDays = 0;
    let active = 0;
    let countedBase = 0;
    let base = 0;
    let paid = 0;
    const standings: DomainStanding[] = [];
    for (let n = dayNumber(from); n <= dayNumber(to); n++) {
      const day = dayFrom(n);
      const standing = this.standingOn(day);
      standings.push(standing);
      if (this.exceptionDays.has(day)) exceptionDays++;
      const i = this.indexOf(day);
      if (i == null) continue;
      const period = this.periods[i];
      base += period.base;
      paid += period.paid;
      if (standing.counted) {
        counted++;
        countedBase += period.base;
        if (standing.active) active++;
      }
    }
    return {
      days,
      from,
      to,
      daysCounted: counted,
      exceptionDays,
      activeDays: active,
      countedBase,
      base,
      paid,
      before: this.standingOn(shiftDay(from, -1)),
      end: this.standingOn(to),
      ranksReached: this.ranksReached.filter((r) => r.qualifiedOn >= from && r.qualifiedOn <= to),
      standings,
    };
  }

  private indexOf(day: string): number | null {
    if (this.startNumber == null) return null;
    const i = dayNumber(day) - this.startNumber;
    return i >= 0 && i < this.periods.length ? i : null;
  }
}

// --- Reading it as the API does --------------------------------------------------

/** `DomainRows.From`: one domain as `GET /api/domains` answers it. */
export function toDomainRow(
  currency: { key: string; label: string },
  totalXp: number,
  progress: DomainProgress | null,
): DomainRow {
  const row: DomainRow = {
    key: currency.key,
    label: currency.label,
    name: domainName(currency.label),
    totalXp,
    configured: !!progress,
    lifetime: null,
    consistency: null,
    shown: null,
    best: null,
    current: null,
    ranks: null,
  };
  if (!progress) return row;
  const p = progress;
  const rules = p.rules;
  const rule = rules.consistency;
  row.lifetime = {
    start: p.start,
    level: p.lifetimeLevel,
    levelXp: costToNext(rules.curve, p.lifetimeLevel),
    xpIntoLevel: p.xpIntoLevel,
    xpToNext: p.xpToNext,
  };
  const next = p.nextConsistency;
  row.consistency = {
    level: p.consistencyLevel,
    windowDays: p.window.days,
    from: p.window.from,
    to: p.window.to,
    averageBase: round1(p.window.base, p.window.days),
    next: next
      ? {
          level: next.level,
          windowDays: next.windowDays,
          average: next.average,
          averageNow: round1(next.baseNow, next.windowDays),
          averageMet: next.averageMet,
          activeMinimum: next.activeMinimum,
          activeDaysNeeded: next.activeDaysNeeded,
          activeDays: next.activeDaysNow,
          activeDaysMet: next.activeDaysMet,
          ageNeeded: next.ageNeeded,
          age: next.ageNow,
          ageMet: next.ageMet,
        }
      : null,
  };
  const backOnTrackDays = rules.fall.backOnTrackDays;
  row.shown = {
    level: p.level,
    exact: roundHalfAway(p.shownExact, 100) / 100,
    target: p.target,
    rank: p.rank,
    rankTitle: p.rankRule.title,
    limitedBy: p.limitedBy,
    falling: p.falling,
    paused: p.paused,
    fallingSince: p.fallingSince,
    fallingFrom: p.fallingFrom,
    percentPerDay: rules.fall.percentPerDay,
    lastChange: p.lastChange ? { ...p.lastChange } : null,
    backOnTrack:
      p.level > p.target
        ? {
            days: backOnTrackDays,
            average: averageFor(rule, p.level),
            activeMinimum: activeMinimumFor(rule, p.level),
            activeDays: activeDaysNeededFor(rule, p.level, backOnTrackDays),
          }
        : null,
  };
  const reached = new Map(p.ranksReached.map((r) => [r.rank, r.qualifiedOn]));
  row.best = {
    level: p.bestLevel,
    levelOn: p.bestLevelOn,
    rank: p.bestRank,
    rankTitle: p.bestRankRule.title,
    rankOn: reached.get(p.bestRank) ?? null,
  };
  row.current = {
    todayBase: p.todayBase,
    todayAction: p.todayAction,
    activeMinimum: p.activeMinimum,
    todayActive: p.todayActive,
    todayException: p.todayException,
    rule: describeRule(rules, p.level),
    activeShare: round4(p.consistency.successfulDays, p.consistency.days),
    activeDays: p.consistency.successfulDays,
    windowDays: p.consistency.days,
    multiplier: p.multiplierNow,
    nextTier: p.nextTier
      ? {
          minShare: p.nextTier.minShare,
          multiplier: p.nextTier.multiplier,
          daysNeeded: p.consistency.daysNeededFor(p.nextTier.minShare),
        }
      : null,
    settledThrough: p.settledThrough,
  };
  row.ranks = rules.ranks.map(
    (rank, i): DomainRankRow => ({
      rank: i,
      title: rank.title,
      level: rank.startLevel,
      lastLevel: topLevelOf(rules, i),
      average: i === 0 ? null : averageFor(rule, rank.startLevel),
      activeMinimum: i === 0 ? null : activeMinimumFor(rule, rank.startLevel),
      activeDays: i === 0 ? null : activeDaysNeededFor(rule, rank.startLevel, rule.windowDays),
      windowDays: rule.windowDays,
      totalXp: totalFor(rules.curve, rank.startLevel),
      gems: i === 0 ? 0 : gemsFor(rules, i),
      reachedOn: reached.get(i) ?? null,
    }),
  );
  return row;
}

/** A marked day as the reads see it. */
export interface MarkedDay {
  day: string;
  comment: string;
}

/** `DomainService.GetDaysAsync` + `DomainRows.From`: the last `count` days, today included. */
export function toDomainDays(
  currency: { key: string; label: string },
  credit: readonly DomainDay[],
  today: string,
  count: number,
  exceptions: readonly MarkedDay[] = [],
  settledThrough?: string,
): DomainDaysRead {
  const rules = rulesFor(currency.key);
  const progress = rules
    ? DomainProgress.fold(rules, credit, today, exceptions.map((e) => e.day), settledThrough)
    : null;
  const comments = new Map(exceptions.map((e) => [e.day, e.comment]));
  const byDay = new Map<string, { base: number; paid: number; passive: number }>();
  for (const d of credit) {
    const sum = byDay.get(d.day) ?? { base: 0, paid: 0, passive: 0 };
    byDay.set(d.day, { base: sum.base + d.base, paid: sum.paid + d.paid, passive: sum.passive + (d.passive ?? 0) });
  }
  const periods = new Map((progress?.periods ?? []).map((p) => [p.day, p]));
  const days: DomainDayRow[] = [];
  for (let ago = count - 1; ago >= 0; ago--) {
    const day = shiftDay(today, -ago);
    if (progress) {
      const p = periods.get(day);
      const s = progress.standingOn(day);
      const base = p?.base ?? 0;
      const passive = p?.passive ?? 0;
      days.push({
        day,
        base,
        action: base - passive,
        passive,
        paid: p?.paid ?? 0,
        exception: comments.get(day) ?? null,
        counted: s.counted,
        active: s.active,
        activeMinimum: s.activeMinimum,
        lifetimeLevel: s.lifetimeLevel,
        consistencyLevel: s.consistencyLevel,
        level: s.level,
        rank: s.rank,
      });
    } else {
      const c = byDay.get(day) ?? { base: 0, paid: 0, passive: 0 };
      days.push({
        day,
        base: c.base,
        action: c.base - c.passive,
        passive: c.passive,
        paid: c.paid,
        exception: null,
        counted: null,
        active: null,
        activeMinimum: null,
        lifetimeLevel: null,
        consistencyLevel: null,
        level: null,
        rank: null,
      });
    }
  }
  return {
    key: currency.key,
    label: currency.label,
    name: domainName(currency.label),
    configured: !!progress,
    today,
    settledThrough: progress?.settledThrough ?? null,
    days,
  };
}

/**
 * `DomainService.GetReportAsync` + `DomainRows.From`: one domain over its last `days` days. A
 * configured domain reports its period, ending at the last settled day; one without rules, the
 * XP credited to the `days` days ending today.
 */
export function toDomainReportRow(
  currency: { key: string; label: string },
  credit: readonly DomainDay[],
  today: string,
  days: number,
  exceptions: readonly string[] = [],
  settledThrough?: string,
): DomainReportRow {
  const rules = rulesFor(currency.key);
  const row: DomainReportRow = {
    key: currency.key,
    label: currency.label,
    name: domainName(currency.label),
    configured: !!rules,
    from: shiftDay(today, 1 - days),
    to: today,
    base: 0,
    paid: 0,
    daysCounted: null,
    exceptionDays: null,
    activeDays: null,
    activeShare: null,
    averageBase: null,
    levelFrom: null,
    levelTo: null,
    rankFrom: null,
    rankTo: null,
    rankTitle: null,
    bestLevelFrom: null,
    bestLevelTo: null,
    bestRank: null,
    bestRankTitle: null,
    ranksReached: null,
    ranks: null,
  };
  if (!rules) {
    for (const d of credit) {
      if (d.day >= row.from && d.day <= today) {
        row.base += d.base;
        row.paid += d.paid;
      }
    }
    return row;
  }
  const r = DomainProgress.fold(rules, credit, today, exceptions, settledThrough).report(days);
  return {
    ...row,
    from: r.from,
    to: r.to,
    base: r.base,
    paid: r.paid,
    daysCounted: r.daysCounted,
    exceptionDays: r.exceptionDays,
    activeDays: r.activeDays,
    activeShare: round4(r.activeDays, r.daysCounted),
    averageBase: round1(r.countedBase, r.daysCounted),
    levelFrom: r.before.level,
    levelTo: r.end.level,
    rankFrom: r.before.rank,
    rankTo: r.end.rank,
    rankTitle: rules.ranks[r.end.rank].title,
    bestLevelFrom: r.before.bestLevel,
    bestLevelTo: r.end.bestLevel,
    bestRank: r.end.bestRank,
    bestRankTitle: rules.ranks[r.end.bestRank].title,
    ranksReached: r.ranksReached.map((x) => ({ rank: x.rank, title: x.rule.title, qualifiedOn: x.qualifiedOn })),
    ranks: r.standings.map((s) => ({
      day: s.day,
      level: s.level,
      rank: s.rank,
      bestRank: s.bestRank,
      exception: s.exception,
    })),
  };
}

/**
 * `RewardService.DomainChanges`: each domain whose level, rank or bests moved from `before` to
 * `after`. A rank-up paid in this write counts as reached now, even when time alone reached it
 * earlier: its domain moves from the rank below it, at that rank's last level at most.
 */
export function domainChanges(
  before: readonly DomainProgress[],
  after: readonly DomainProgress[],
  rankUps: readonly { currency: string; rank: number }[],
): DomainChange[] {
  const changes: DomainChange[] = [];
  for (const now of after) {
    const then = before.find((b) => b.rules.currency === now.rules.currency)!;
    let levelFrom = then.level;
    let rankFrom = then.rank;
    let bestLevelFrom = then.bestLevel;
    let bestRankFrom = then.bestRank;
    const paid = rankUps.filter((r) => r.currency === now.rules.currency).map((r) => r.rank);
    if (paid.length > 0) {
      const lowest = Math.min(...paid);
      const below = lowest - 1;
      const belowTop = now.rules.ranks[lowest].startLevel - 1;
      rankFrom = Math.min(rankFrom, below);
      levelFrom = Math.min(levelFrom, belowTop);
      bestRankFrom = Math.min(bestRankFrom, below);
      bestLevelFrom = Math.min(bestLevelFrom, belowTop);
    }
    if (
      levelFrom !== now.level ||
      rankFrom !== now.rank ||
      bestLevelFrom !== now.bestLevel ||
      bestRankFrom !== now.bestRank
    ) {
      changes.push({
        domain: now.rules.currency,
        levelFrom,
        levelTo: now.level,
        rankFrom,
        rankTo: now.rank,
        rankTitle: now.rankRule.title,
        bestLevelFrom,
        bestLevelTo: now.bestLevel,
        bestRankFrom,
        bestRankTo: now.bestRank,
      });
    }
  }
  return changes;
}
