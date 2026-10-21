/**
 * Life domains on the web (REWARD-6 / REWARD-7, xp-domains plan §3.8, §3.12): reading
 * `/api/domains` leniently, and the words the Character page and the level-up toasts use.
 */
import type {
  DomainChange,
  DomainDaysRead,
  DomainNextRank,
  DomainRow,
  DomainsRead,
  Earned,
} from "@/lib/production-ui-types";
import { currency, normalizeAward } from "@/lib/rewards";

const num = (v: unknown) => Number(v ?? 0);
const numOrNull = (v: unknown) => (v == null ? null : Number(v));

// --- Normalizing (int64 and decimal fields may arrive as strings) -------------

export function normalizeDomainRow(raw: DomainRow): DomainRow {
  const l = raw.lifetime;
  const n = raw.nextRank;
  const c = raw.current;
  return {
    ...raw,
    totalXp: num(raw.totalXp),
    lifetime: l
      ? {
          ...l,
          level: num(l.level),
          rawLevel: num(l.rawLevel),
          topLevel: num(l.topLevel),
          rank: num(l.rank),
          levelXp: num(l.levelXp),
          xpIntoLevel: num(l.xpIntoLevel),
          xpToNext: num(l.xpToNext),
          bankedXp: num(l.bankedXp),
          start: l.start ?? null,
          qualifiedOn: l.qualifiedOn ?? null,
        }
      : null,
    nextRank: n
      ? {
          ...n,
          rank: num(n.rank),
          level: num(n.level),
          bar: num(n.bar),
          windowDays: num(n.windowDays),
          consistency: num(n.consistency),
          successfulDays: num(n.successfulDays),
          requiredDays: num(n.requiredDays),
          daysNeeded: num(n.daysNeeded),
          daysOld: num(n.daysOld),
        }
      : null,
    current: c
      ? {
          ...c,
          todayBase: num(c.todayBase),
          target: numOrNull(c.target),
          consistency: num(c.consistency),
          successfulDays: num(c.successfulDays),
          windowDays: num(c.windowDays),
          multiplier: num(c.multiplier),
          nextTier: c.nextTier
            ? {
                minShare: num(c.nextTier.minShare),
                multiplier: num(c.nextTier.multiplier),
                daysNeeded: num(c.nextTier.daysNeeded),
              }
            : null,
        }
      : null,
    ranks: raw.ranks
      ? raw.ranks.map((r) => ({
          ...r,
          rank: num(r.rank),
          level: num(r.level),
          topLevel: num(r.topLevel),
          bar: num(r.bar),
          windowDays: num(r.windowDays),
          gems: num(r.gems),
          qualifiedOn: r.qualifiedOn ?? null,
        }))
      : null,
  };
}

export function normalizeDomains(raw: DomainsRead): DomainsRead {
  return { today: raw.today, domains: (raw.domains ?? []).map(normalizeDomainRow) };
}

export function normalizeDomainDays(raw: DomainDaysRead): DomainDaysRead {
  return {
    ...raw,
    target: numOrNull(raw.target),
    days: (raw.days ?? []).map((d) => ({
      day: d.day,
      base: num(d.base),
      paid: num(d.paid),
      succeeded: d.succeeded ?? null,
    })),
  };
}

export function normalizeProgress(raw: DomainChange[] | null | undefined): DomainChange[] {
  return (raw ?? []).map((c) => ({
    ...c,
    levelFrom: num(c.levelFrom),
    levelTo: num(c.levelTo),
    rankFrom: num(c.rankFrom),
    rankTo: num(c.rankTo),
  }));
}

/** An earning write's answer: `{ awards, progress }`. */
export function normalizeEarned(raw: unknown): Earned {
  const r = (raw ?? {}) as Partial<Earned>;
  return {
    awards: Array.isArray(r.awards) ? r.awards.map(normalizeAward) : [],
    progress: normalizeProgress(r.progress),
  };
}

// --- Words ----------------------------------------------------------------------

/** "❤️" for `health_xp`. */
export function domainSymbol(key: string): string {
  return currency(key)?.symbol ?? "";
}

/** "Health" for `health_xp`. */
export function domainNameOf(key: string): string {
  return currency(key)?.label.split(" ")[0] ?? key;
}

/** "60%". */
export function percent(share: number): string {
  return `${Math.round(share * 100)}%`;
}

/** "×1.2". */
export function multiplierLabel(multiplier: number): string {
  return `×${multiplier.toFixed(1)}`;
}

/** "a successful day on 60% of days over 30 days". */
export function rankRequirement(rank: { bar: number; windowDays: number }): string {
  return rank.windowDays === 0
    ? "where every domain starts"
    : `a successful day on ${percent(rank.bar)} of days over ${rank.windowDays} days`;
}

/**
 * What still stands between the domain and its next rank (§3.12). While the level is
 * there (capped): "Journeyman needs a successful day on 60% of days over 30 days: 14 of 18
 * so far". Otherwise the level leads: "Apprentice at level 10, with a successful day on 50%
 * of days over 14 days: met". A domain younger than the window says how old it is.
 */
export function nextRankText(next: DomainNextRank): string {
  const tally = next.barMet ? "met" : `${next.successfulDays} of ${next.requiredDays} so far`;
  const age = next.oldEnough ? "" : ` · ${next.daysOld} of ${next.windowDays} days old`;
  return next.levelReached
    ? `${next.title} needs ${rankRequirement(next)}: ${tally}${age}`
    : `${next.title} at level ${next.level}, with ${rankRequirement(next)}: ${tally}${age}`;
}

/**
 * The toast for a domain that moved (§3.8): a rank-up names the title, a level-up the level,
 * and a level lost says so quietly.
 */
export function progressMessage(change: DomainChange): { kind: "rank" | "level" | "lost"; text: string } {
  const name = domainNameOf(change.domain);
  if (change.rankTo > change.rankFrom) {
    return { kind: "rank", text: `You're now a ${name} ${change.rankTitle}.` };
  }
  if (change.levelTo > change.levelFrom) {
    return { kind: "level", text: `${domainSymbol(change.domain)} ${name} level ${change.levelTo}!`.trim() };
  }
  return { kind: "lost", text: `${name} back to level ${change.levelTo}` };
}
