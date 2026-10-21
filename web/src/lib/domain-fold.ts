/**
 * Life-domain progression, folded from daily credit (REWARD-6 / REWARD-7, D12, xp-domains
 * plan §3.5): a TypeScript port of the kernel's `DomainProgress` and Health's rules
 * (`LifeDomains.Health`), so the mock store reads `/api/domains` the way the API does.
 * `domain-fold.test.ts` runs it on the C# tests' examples.
 *
 * Shares and bars are compared as whole basis points, and multipliers applied in hundredths,
 * so a bar like 0.65 × 60 days never drifts the way floating point can.
 */
import type {
  DomainChange,
  DomainDaysRead,
  DomainRankRow,
  DomainRow,
} from "@/lib/production-ui-types";

export interface RankRule {
  startLevel: number;
  title: string;
  /** The successful share the window needs, 0–1. */
  minConsistency: number;
  windowDays: number;
  /** Designed, not built: a rank that lists any waits (no pilot rank does). */
  milestones?: readonly string[];
}

export interface MultiplierTier {
  minShare: number;
  multiplier: number;
}

/** `Base + PerLevel·(L−1) + RampPerLevel·max(0, L−RampFrom)` to go from level L to L+1. */
export interface RampCurve {
  base: number;
  perLevel: number;
  rampFrom: number;
  rampPerLevel: number;
  maxLevel: number;
}

export interface DomainRules {
  currency: string;
  curve: RampCurve;
  /** The success rule: a day succeeds when its base XP reaches this. */
  minimumBaseXp: number;
  ranks: readonly RankRule[];
  multiplier: { windowDays: number; tiers: readonly MultiplierTier[] };
  rankUpGems: { perRank: number; top: number };
}

/** Health, the pilot domain (xp-domains plan §2), as `LifeDomains.Health` sets it. */
export const HEALTH: DomainRules = {
  currency: "health_xp",
  curve: { base: 300, perLevel: 25, rampFrom: 45, rampPerLevel: 70, maxLevel: 100 },
  minimumBaseXp: 70,
  ranks: [
    { startLevel: 1, title: "Novice", minConsistency: 0, windowDays: 0 },
    { startLevel: 10, title: "Apprentice", minConsistency: 0.5, windowDays: 14 },
    { startLevel: 20, title: "Journeyman", minConsistency: 0.6, windowDays: 30 },
    { startLevel: 30, title: "Adept", minConsistency: 0.65, windowDays: 60 },
    { startLevel: 40, title: "Expert", minConsistency: 0.7, windowDays: 90 },
    { startLevel: 50, title: "Veteran", minConsistency: 0.75, windowDays: 120 },
    { startLevel: 60, title: "Elite", minConsistency: 0.78, windowDays: 180 },
    { startLevel: 70, title: "Master", minConsistency: 0.8, windowDays: 240 },
    { startLevel: 80, title: "Champion", minConsistency: 0.85, windowDays: 300 },
    { startLevel: 90, title: "Legend", minConsistency: 0.88, windowDays: 365 },
    { startLevel: 100, title: "Grandmaster", minConsistency: 0.9, windowDays: 365 },
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
};

/** Every configured domain. */
export const CONFIGURED_DOMAINS: readonly DomainRules[] = [HEALTH];

/** The rules for a currency, or null for a domain that only collects XP. */
export function rulesFor(currency: string): DomainRules | null {
  return CONFIGURED_DOMAINS.find((r) => r.currency === currency.trim().toLowerCase()) ?? null;
}

/** The trigger of a rank-up award (§3.7). */
export const RANK_TRIGGER = "rank";

// --- The level curve ---------------------------------------------------------

function checkLevel(curve: RampCurve, level: number) {
  if (!Number.isInteger(level) || level < 1 || level > curve.maxLevel) {
    throw new RangeError(`Levels run from 1 to ${curve.maxLevel}; got ${level}.`);
  }
}

/** The XP to go from `level` to the next; 0 at the top. */
export function costToNext(curve: RampCurve, level: number): number {
  checkLevel(curve, level);
  return level === curve.maxLevel
    ? 0
    : curve.base + curve.perLevel * (level - 1) + curve.rampPerLevel * Math.max(0, level - curve.rampFrom);
}

/** The total XP that reaches `level` (0 for level 1). */
export function totalFor(curve: RampCurve, level: number): number {
  checkLevel(curve, level);
  const steps = level - 1;
  const ramped = Math.max(0, level - 1 - curve.rampFrom);
  return curve.base * steps + (curve.perLevel * steps * (steps - 1)) / 2 + (curve.rampPerLevel * ramped * (ramped + 1)) / 2;
}

/** The level `totalXp` reaches, at most the top. */
export function levelFor(curve: RampCurve, totalXp: number): number {
  let low = 1;
  let high = curve.maxLevel;
  while (low < high) {
    const middle = Math.floor((low + high + 1) / 2);
    if (totalFor(curve, middle) <= totalXp) low = middle;
    else high = middle - 1;
  }
  return low;
}

// --- Rules helpers -----------------------------------------------------------

/** The highest level `rank` allows: 9 for Novice. */
export function topLevelOf(rules: DomainRules, rank: number): number {
  return rank + 1 < rules.ranks.length ? rules.ranks[rank + 1].startLevel - 1 : rules.curve.maxLevel;
}

/** The Gems for entering `rank`: the top one pays more. */
export function gemsFor(rules: DomainRules, rank: number): number {
  return rank === rules.ranks.length - 1 ? rules.rankUpGems.top : rules.rankUpGems.perRank;
}

/** `rank:health_xp:10`. */
export function rankAwardKey(rules: DomainRules, rank: number): string {
  return `${RANK_TRIGGER}:${rules.currency}:${rules.ranks[rank].startLevel}`;
}

/** "Health" for `health_xp`. */
export function domainName(label: string): string {
  return label.split(" ")[0];
}

const bp = (share: number) => Math.round(share * 10_000);

/** The successful days a share takes over a window: ceil(share × days), exactly. */
function requiredDays(share: number, days: number): number {
  return Math.ceil((bp(share) * days) / 10_000 - 1e-9);
}

/** The multiplier a window's successful share earns: the highest tier it reaches, else 1.0. */
function multiplierOf(rules: DomainRules, successful: number, days: number): number {
  let best = 1;
  for (const tier of rules.multiplier.tiers) {
    // successful / days ≥ minShare, in whole numbers (an empty window has share 0).
    const reaches = days === 0 ? bp(tier.minShare) <= 0 : successful * 10_000 >= bp(tier.minShare) * days;
    if (reaches && tier.multiplier > best) best = tier.multiplier;
  }
  return best;
}

function nextTierAbove(rules: DomainRules, successful: number, days: number): MultiplierTier | null {
  const above = rules.multiplier.tiers
    .filter((t) => (days === 0 ? bp(t.minShare) > 0 : successful * 10_000 < bp(t.minShare) * days))
    .sort((a, b) => a.minShare - b.minShare);
  return above[0] ?? null;
}

/** What `amount` pays at `multiplier` (§3.6): whole XP, halves rounding away from zero. */
export function applyMultiplier(amount: number, multiplier: number): number {
  const hundredths = Math.round(multiplier * 100);
  const product = Math.abs(amount) * hundredths;
  return Math.sign(amount) * Math.floor((product + 50) / 100);
}

// --- Days --------------------------------------------------------------------

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

// --- The fold ----------------------------------------------------------------

/** One day's credit: base before any multiplier, paid as paid. */
export interface DomainDay {
  day: string;
  base: number;
  paid: number;
}

export interface DomainPeriod {
  day: string;
  base: number;
  paid: number;
  succeeded: boolean;
}

/** Where the domain stood at the end of a day. */
export interface DomainStanding {
  day: string;
  totalXp: number;
  rawLevel: number;
  level: number;
  rank: number;
}

export interface RankReached {
  rank: number;
  rule: RankRule;
  qualifiedOn: string;
}

/** The successful days in a window; days before the start count as unsuccessful. */
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

  /** The successful share, 0–1. */
  get share(): number {
    return this.days === 0 ? 0 : this.successfulDays / this.days;
  }

  get covered(): boolean {
    return this.coveredDays === this.days;
  }

  required(share: number): number {
    return requiredDays(share, this.days);
  }

  reaches(share: number): boolean {
    return this.successfulDays >= this.required(share);
  }

  daysNeededFor(share: number): number {
    return Math.max(0, this.required(share) - this.successfulDays);
  }
}

/** The next rank's gate as it reads today. */
export interface NextRank {
  rank: number;
  rule: RankRule;
  window: ConsistencyWindow;
  daysOld: number;
  levelReached: boolean;
  required: number;
  daysNeeded: number;
  oldEnough: boolean;
  barMet: boolean;
}

/**
 * A life domain's progression, folded from its daily credit — `DomainProgress`, line for
 * line: each day from the start to today, whether it succeeded, the XP so far, the level it
 * reaches, and the rank held. A rank is entered on the first day its level is reached, the
 * domain is as old as its window, and the window's share is at its bar; once entered it holds.
 * The level stops at the top of the rank held; XP beyond it is banked.
 */
export class DomainProgress {
  readonly rules: DomainRules;
  /** Today, in the home zone. */
  readonly today: string;
  readonly start: string | null;
  readonly periods: DomainPeriod[];
  readonly standings: DomainStanding[];
  readonly ranksReached: RankReached[] = [];
  readonly totalXp: number;
  readonly rawLevel: number;
  readonly level: number;
  readonly rank: number;
  readonly xpIntoLevel: number;
  readonly xpToNext: number = 0;
  readonly bankedXp: number = 0;
  readonly consistency: ConsistencyWindow;
  readonly multiplierNow: number;
  readonly nextTier: MultiplierTier | null;
  readonly next: NextRank | null = null;
  private readonly successes: number[];
  private readonly startNumber: number | null;

  private constructor(rules: DomainRules, today: string, start: string | null, periods: DomainPeriod[]) {
    this.rules = rules;
    this.today = today;
    this.start = start;
    this.startNumber = start ? dayNumber(start) : null;
    this.periods = periods;
    this.successes = [0];
    periods.forEach((p, i) => this.successes.push(this.successes[i] + (p.succeeded ? 1 : 0)));

    this.consistency = this.windowBefore(today, rules.multiplier.windowDays);
    this.multiplierNow = multiplierOf(rules, this.consistency.successfulDays, this.consistency.days);
    this.nextTier = nextTierAbove(rules, this.consistency.successfulDays, this.consistency.days);

    this.standings = this.walk();
    const now = this.standings.at(-1) ?? { day: today, totalXp: 0, rawLevel: 1, level: 1, rank: 0 };
    this.totalXp = now.totalXp;
    this.rawLevel = now.rawLevel;
    this.level = now.level;
    this.rank = now.rank;

    const curve = rules.curve;
    if (this.capped) {
      this.xpIntoLevel = costToNext(curve, this.level);
      this.bankedXp = this.totalXp - totalFor(curve, this.level + 1);
    } else {
      this.xpIntoLevel = this.totalXp - totalFor(curve, this.level);
      this.xpToNext = this.level === curve.maxLevel ? 0 : totalFor(curve, this.level + 1) - this.totalXp;
    }

    if (this.rank + 1 < rules.ranks.length) {
      const rule = rules.ranks[this.rank + 1];
      const window = this.windowEndingOn(today, rule.windowDays);
      const daysOld = this.daysOld;
      this.next = {
        rank: this.rank + 1,
        rule,
        window,
        daysOld,
        levelReached: this.rawLevel >= rule.startLevel,
        required: window.required(rule.minConsistency),
        daysNeeded: window.daysNeededFor(rule.minConsistency),
        oldEnough: daysOld >= rule.windowDays,
        barMet: window.reaches(rule.minConsistency),
      };
    }
  }

  /** Folds one domain's credit up to `today` (home zone); credit after today is left out. */
  static fold(rules: DomainRules, days: Iterable<DomainDay>, today: string): DomainProgress {
    const credit = new Map<string, { base: number; paid: number }>();
    for (const d of days) {
      if (d.day > today) continue;
      const sum = credit.get(d.day) ?? { base: 0, paid: 0 };
      credit.set(d.day, { base: sum.base + d.base, paid: sum.paid + d.paid });
    }
    const paidDays = [...credit].filter(([, c]) => c.paid > 0).map(([day]) => day).sort();
    if (paidDays.length === 0) return new DomainProgress(rules, today, null, []);

    const start = paidDays[0];
    const periods: DomainPeriod[] = [];
    for (let n = dayNumber(start); n <= dayNumber(today); n++) {
      const day = dayFrom(n);
      const c = credit.get(day) ?? { base: 0, paid: 0 };
      periods.push({ day, base: c.base, paid: c.paid, succeeded: c.base >= rules.minimumBaseXp });
    }
    return new DomainProgress(rules, today, start, periods);
  }

  get todayPeriod(): DomainPeriod | null {
    return this.periods.at(-1) ?? null;
  }

  get todayBase(): number {
    return this.todayPeriod?.base ?? 0;
  }

  get todaySucceeded(): boolean {
    return this.todayPeriod?.succeeded ?? false;
  }

  /** The domain's age for rank gates, today counting once it has succeeded. */
  get daysOld(): number {
    if (this.startNumber == null) return 0;
    const through = dayNumber(this.today) - (this.todaySucceeded ? 0 : 1);
    return Math.max(0, through - this.startNumber + 1);
  }

  get rankRule(): RankRule {
    return this.rules.ranks[this.rank];
  }

  get rankQualifiedOn(): string | null {
    return this.ranksReached.at(-1)?.qualifiedOn ?? null;
  }

  get topLevel(): number {
    return topLevelOf(this.rules, this.rank);
  }

  get capped(): boolean {
    return this.rawLevel > this.level;
  }

  succeededOn(day: string): boolean {
    const i = this.indexOf(day);
    return i != null && this.periods[i].succeeded;
  }

  /** Where the domain stood at the end of `day`: level 1 Novice before the start, as now after today. */
  standingOn(day: string): DomainStanding {
    if (this.startNumber == null || day < this.start!) {
      return { day, totalXp: 0, rawLevel: 1, level: 1, rank: 0 };
    }
    const i = Math.min(dayNumber(day) - this.startNumber, this.standings.length - 1);
    return { ...this.standings[i], day };
  }

  /** The `days` complete days before `day`: the multiplier's window. */
  windowBefore(day: string, days: number): ConsistencyWindow {
    return this.window(shiftDay(day, -days), shiftDay(day, -1));
  }

  /** The `days` days ending on `end`; today counts only once it has succeeded. */
  windowEndingOn(end: string, days: number): ConsistencyWindow {
    let to = end;
    if (to >= this.today && !this.todaySucceeded) to = shiftDay(this.today, -1);
    else if (to > this.today) to = this.today;
    return this.window(shiftDay(to, 1 - days), to);
  }

  /** The multiplier for XP credited to `day`: the tier of the 30 days before it. */
  multiplierFor(day: string): number {
    const w = this.windowBefore(day, this.rules.multiplier.windowDays);
    return multiplierOf(this.rules, w.successfulDays, w.days);
  }

  private walk(): DomainStanding[] {
    const walked: DomainStanding[] = [];
    const { curve, ranks } = this.rules;
    let total = 0;
    let rank = 0;
    for (const period of this.periods) {
      total += period.paid;
      const rawLevel = levelFor(curve, total);
      while (rank + 1 < ranks.length && this.qualifies(ranks[rank + 1], rawLevel, period.day)) {
        rank++;
        this.ranksReached.push({ rank, rule: ranks[rank], qualifiedOn: period.day });
      }
      walked.push({ day: period.day, totalXp: total, rawLevel, level: Math.min(rawLevel, topLevelOf(this.rules, rank)), rank });
    }
    return walked;
  }

  private qualifies(rule: RankRule, rawLevel: number, day: string): boolean {
    if (rawLevel < rule.startLevel || (rule.milestones?.length ?? 0) > 0) return false;
    const window = this.windowEndingOn(day, rule.windowDays);
    return window.covered && window.reaches(rule.minConsistency);
  }

  private indexOf(day: string): number | null {
    if (this.startNumber == null) return null;
    const i = dayNumber(day) - this.startNumber;
    return i >= 0 && i < this.periods.length ? i : null;
  }

  private window(from: string, to: string): ConsistencyWindow {
    const fromN = dayNumber(from);
    const toN = dayNumber(to);
    const days = Math.max(0, toN - fromN + 1);
    if (days === 0 || this.startNumber == null) return new ConsistencyWindow(from, to, days, 0, 0);
    const covered = Math.max(0, toN - Math.max(fromN, this.startNumber) + 1);
    const clamp = (n: number) => Math.min(Math.max(n, 0), this.periods.length);
    const low = clamp(fromN - this.startNumber);
    const high = clamp(toN - this.startNumber + 1);
    const successful = high > low ? this.successes[high] - this.successes[low] : 0;
    return new ConsistencyWindow(from, to, days, covered, successful);
  }
}

// --- Reading it as the API does ---------------------------------------------

const round4 = (n: number) => Math.round(n * 10_000) / 10_000;

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
    nextRank: null,
    current: null,
    ranks: null,
  };
  if (!progress) return row;
  const p = progress;
  const rules = p.rules;
  row.lifetime = {
    start: p.start,
    level: p.level,
    rawLevel: p.rawLevel,
    topLevel: p.topLevel,
    capped: p.capped,
    rank: p.rank,
    rankTitle: p.rankRule.title,
    qualifiedOn: p.rankQualifiedOn,
    levelXp: costToNext(rules.curve, p.level),
    xpIntoLevel: p.xpIntoLevel,
    xpToNext: p.xpToNext,
    bankedXp: p.bankedXp,
  };
  if (p.next) {
    const n = p.next;
    row.nextRank = {
      rank: n.rank,
      title: n.rule.title,
      level: n.rule.startLevel,
      bar: n.rule.minConsistency,
      windowDays: n.rule.windowDays,
      consistency: round4(n.window.share),
      successfulDays: n.window.successfulDays,
      requiredDays: n.required,
      daysNeeded: n.daysNeeded,
      daysOld: n.daysOld,
      levelReached: n.levelReached,
      oldEnough: n.oldEnough,
      barMet: n.barMet,
    };
  }
  row.current = {
    todayBase: p.todayBase,
    todaySucceeded: p.todaySucceeded,
    rule: `at least ${rules.minimumBaseXp} base XP`,
    target: rules.minimumBaseXp,
    consistency: round4(p.consistency.share),
    successfulDays: p.consistency.successfulDays,
    windowDays: p.consistency.days,
    multiplier: p.multiplierNow,
    nextTier: p.nextTier
      ? {
          minShare: p.nextTier.minShare,
          multiplier: p.nextTier.multiplier,
          daysNeeded: p.consistency.daysNeededFor(p.nextTier.minShare),
        }
      : null,
  };
  const qualified = new Map(p.ranksReached.map((r) => [r.rank, r.qualifiedOn]));
  row.ranks = rules.ranks.map(
    (rank, i): DomainRankRow => ({
      rank: i,
      title: rank.title,
      level: rank.startLevel,
      topLevel: topLevelOf(rules, i),
      bar: rank.minConsistency,
      windowDays: rank.windowDays,
      gems: i === 0 ? 0 : gemsFor(rules, i),
      qualifiedOn: qualified.get(i) ?? null,
    }),
  );
  return row;
}

/** `DomainService.GetDaysAsync` + `DomainRows.From`: the last `count` days, today included. */
export function toDomainDays(
  currency: { key: string; label: string },
  credit: readonly DomainDay[],
  today: string,
  count: number,
): DomainDaysRead {
  const rules = rulesFor(currency.key);
  const progress = rules ? DomainProgress.fold(rules, credit, today) : null;
  const byDay = new Map<string, { base: number; paid: number }>();
  for (const d of credit) {
    const sum = byDay.get(d.day) ?? { base: 0, paid: 0 };
    byDay.set(d.day, { base: sum.base + d.base, paid: sum.paid + d.paid });
  }
  const periods = new Map((progress?.periods ?? []).map((p) => [p.day, p]));
  const days = [];
  for (let ago = count - 1; ago >= 0; ago--) {
    const day = shiftDay(today, -ago);
    if (progress) {
      const p = periods.get(day);
      days.push({ day, base: p?.base ?? 0, paid: p?.paid ?? 0, succeeded: p?.succeeded ?? false });
    } else {
      const c = byDay.get(day);
      days.push({ day, base: c?.base ?? 0, paid: c?.paid ?? 0, succeeded: null });
    }
  }
  return {
    key: currency.key,
    label: currency.label,
    name: domainName(currency.label),
    configured: !!progress,
    today,
    target: rules?.minimumBaseXp ?? null,
    days,
  };
}

/**
 * `RewardService.DomainChanges`: each domain whose level or rank moved from `before` to
 * `after`. A rank-up paid in this write counts as reached now, even when time alone opened
 * its gate earlier: its domain moves from the rank below it, at the level that rank allowed.
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
    const paid = rankUps.filter((r) => r.currency === now.rules.currency).map((r) => r.rank);
    if (paid.length > 0 && Math.min(...paid) - 1 < rankFrom) {
      rankFrom = Math.min(...paid) - 1;
      levelFrom = Math.min(then.rawLevel, topLevelOf(now.rules, rankFrom));
    }
    if (levelFrom !== now.level || rankFrom !== now.rank) {
      changes.push({
        domain: now.rules.currency,
        levelFrom,
        levelTo: now.level,
        rankFrom,
        rankTo: now.rank,
        rankTitle: now.rankRule.title,
      });
    }
  }
  return changes;
}
