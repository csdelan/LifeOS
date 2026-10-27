/**
 * Life domains on the web (REWARD-6 / REWARD-7 / REWARD-8, xp-domains plan §7.4.7–§7.4.9):
 * reading `/api/domains` leniently, and the words the Character page and the toasts use.
 */
import type {
  DomainChange,
  DomainConsistencyNeed,
  DomainCurrent,
  DomainDaysRead,
  DomainRankRow,
  DomainReportDay,
  DomainReportRead,
  DomainReportRow,
  DomainRow,
  DomainsRead,
  Earned,
  ExceptionDay,
  ExceptionDayResult,
  ExceptionDaysRead,
} from "@/lib/production-ui-types";
import { formatDay } from "@/lib/dates";
import { barText } from "@/lib/domain-fold";
import { shownPoints } from "@/lib/home-score";
import { currency, normalizeAward } from "@/lib/rewards";

const num = (v: unknown) => Number(v ?? 0);
const numOrNull = (v: unknown) => (v == null ? null : Number(v));
const boolOrNull = (v: unknown) => (v == null ? null : Boolean(v));

// --- Normalizing (int64 and decimal fields may arrive as strings) -------------

function normalizeNeed(n: DomainConsistencyNeed): DomainConsistencyNeed {
  return {
    ...n,
    level: num(n.level),
    windowDays: num(n.windowDays),
    average: num(n.average),
    averageNow: num(n.averageNow),
    averageMet: !!n.averageMet,
    activeMinimum: num(n.activeMinimum),
    activeDaysNeeded: num(n.activeDaysNeeded),
    activeDays: num(n.activeDays),
    activeDaysMet: !!n.activeDaysMet,
    ageNeeded: num(n.ageNeeded),
    age: num(n.age),
    ageMet: !!n.ageMet,
    bar: numOrNull(n.bar),
  };
}

/** `"home"` for a domain held by the home; anything else (an older API sent none) is `"xp"`. */
const kindOf = (kind: unknown): "xp" | "home" => (kind === "home" ? "home" : "xp");

function normalizeCurrent(c: DomainCurrent): DomainCurrent {
  return {
    ...c,
    todayBase: num(c.todayBase),
    todayAction: num(c.todayAction),
    activeMinimum: num(c.activeMinimum),
    todayHomeScore: numOrNull(c.todayHomeScore),
    bar: numOrNull(c.bar),
    todayActive: !!c.todayActive,
    todayException: !!c.todayException,
    activeShare: num(c.activeShare),
    activeDays: num(c.activeDays),
    windowDays: num(c.windowDays),
    multiplier: num(c.multiplier),
    nextTier: c.nextTier
      ? {
          minShare: num(c.nextTier.minShare),
          multiplier: num(c.nextTier.multiplier),
          daysNeeded: num(c.nextTier.daysNeeded),
        }
      : null,
  };
}

function normalizeRank(r: DomainRankRow): DomainRankRow {
  return {
    ...r,
    rank: num(r.rank),
    level: num(r.level),
    lastLevel: num(r.lastLevel),
    average: numOrNull(r.average),
    activeMinimum: numOrNull(r.activeMinimum),
    activeDays: numOrNull(r.activeDays),
    bar: numOrNull(r.bar),
    windowDays: num(r.windowDays),
    totalXp: num(r.totalXp),
    gems: num(r.gems),
    reachedOn: r.reachedOn ?? null,
  };
}

export function normalizeDomainRow(raw: DomainRow): DomainRow {
  const l = raw.lifetime;
  const c = raw.consistency;
  const s = raw.shown;
  const b = raw.best;
  return {
    ...raw,
    totalXp: num(raw.totalXp),
    lifetime: l
      ? {
          start: l.start ?? null,
          level: num(l.level),
          levelXp: num(l.levelXp),
          xpIntoLevel: num(l.xpIntoLevel),
          xpToNext: num(l.xpToNext),
        }
      : null,
    consistency: c
      ? {
          kind: kindOf(c.kind),
          level: num(c.level),
          windowDays: num(c.windowDays),
          from: c.from ?? null,
          to: c.to ?? null,
          averageBase: num(c.averageBase),
          averageHomeScore: numOrNull(c.averageHomeScore),
          held: c.held ? normalizeNeed(c.held) : null,
          next: c.next ? normalizeNeed(c.next) : null,
        }
      : null,
    shown: s
      ? {
          ...s,
          level: num(s.level),
          exact: num(s.exact),
          target: num(s.target),
          rank: num(s.rank),
          limitedBy: s.limitedBy ?? null,
          falling: !!s.falling,
          paused: !!s.paused,
          fallingSince: s.fallingSince ?? null,
          fallingFrom: numOrNull(s.fallingFrom),
          percentPerDay: num(s.percentPerDay),
          lastChange: s.lastChange
            ? { day: s.lastChange.day, from: num(s.lastChange.from), to: num(s.lastChange.to) }
            : null,
          backOnTrack: s.backOnTrack
            ? {
                days: num(s.backOnTrack.days),
                average: num(s.backOnTrack.average),
                activeMinimum: num(s.backOnTrack.activeMinimum),
                activeDays: num(s.backOnTrack.activeDays),
                bar: numOrNull(s.backOnTrack.bar),
              }
            : null,
          bar: numOrNull(s.bar),
        }
      : null,
    best: b
      ? { ...b, level: num(b.level), levelOn: b.levelOn ?? null, rank: num(b.rank), rankOn: b.rankOn ?? null }
      : null,
    current: raw.current ? normalizeCurrent(raw.current) : null,
    ranks: raw.ranks ? raw.ranks.map(normalizeRank) : null,
  };
}

export function normalizeDomains(raw: DomainsRead): DomainsRead {
  return { today: raw.today, domains: (raw.domains ?? []).map(normalizeDomainRow) };
}

export function normalizeDomainDays(raw: DomainDaysRead): DomainDaysRead {
  return {
    ...raw,
    settledThrough: raw.settledThrough ?? null,
    kind: raw.kind == null ? null : kindOf(raw.kind),
    days: (raw.days ?? []).map((d) => ({
      day: d.day,
      base: num(d.base),
      action: num(d.action),
      passive: num(d.passive),
      paid: num(d.paid),
      exception: d.exception ?? null,
      counted: boolOrNull(d.counted),
      active: boolOrNull(d.active),
      activeMinimum: numOrNull(d.activeMinimum),
      lifetimeLevel: numOrNull(d.lifetimeLevel),
      consistencyLevel: numOrNull(d.consistencyLevel),
      level: numOrNull(d.level),
      rank: numOrNull(d.rank),
      homeScore: numOrNull(d.homeScore),
      bar: numOrNull(d.bar),
    })),
  };
}

export function normalizeDomainReport(raw: DomainReportRead): DomainReportRead {
  return {
    today: raw.today,
    days: num(raw.days),
    domains: (raw.domains ?? []).map((d) => ({
      ...d,
      base: num(d.base),
      paid: num(d.paid),
      daysCounted: numOrNull(d.daysCounted),
      exceptionDays: numOrNull(d.exceptionDays),
      activeDays: numOrNull(d.activeDays),
      activeShare: numOrNull(d.activeShare),
      averageBase: numOrNull(d.averageBase),
      kind: d.kind == null ? null : kindOf(d.kind),
      averageHomeScore: numOrNull(d.averageHomeScore),
      levelFrom: numOrNull(d.levelFrom),
      levelTo: numOrNull(d.levelTo),
      rankFrom: numOrNull(d.rankFrom),
      rankTo: numOrNull(d.rankTo),
      rankTitle: d.rankTitle ?? null,
      bestLevelFrom: numOrNull(d.bestLevelFrom),
      bestLevelTo: numOrNull(d.bestLevelTo),
      bestRank: numOrNull(d.bestRank),
      bestRankTitle: d.bestRankTitle ?? null,
      ranksReached: d.ranksReached ? d.ranksReached.map((r) => ({ ...r, rank: num(r.rank) })) : null,
      ranks: d.ranks
        ? d.ranks.map((r) => ({
            day: r.day,
            level: num(r.level),
            rank: num(r.rank),
            bestRank: num(r.bestRank),
            exception: !!r.exception,
          }))
        : null,
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
    // An older API had no bests: read them as the level and rank.
    bestLevelFrom: num(c.bestLevelFrom ?? c.levelFrom),
    bestLevelTo: num(c.bestLevelTo ?? c.levelTo),
    bestRankFrom: num(c.bestRankFrom ?? c.rankFrom),
    bestRankTo: num(c.bestRankTo ?? c.rankTo),
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

function normalizeException(e: ExceptionDay): ExceptionDay {
  return { day: e.day, comment: e.comment ?? "", markedAt: e.markedAt ?? "" };
}

export function normalizeExceptionDays(raw: ExceptionDaysRead): ExceptionDaysRead {
  return { today: raw.today, from: raw.from, exceptions: (raw.exceptions ?? []).map(normalizeException) };
}

export function normalizeExceptionResult(raw: unknown): ExceptionDayResult {
  const r = (raw ?? {}) as Partial<ExceptionDayResult>;
  return { exception: r.exception ? normalizeException(r.exception) : null, ...normalizeEarned(raw) };
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

/** A report period in words: "7 days", "30 days", "3 months", "12 months". */
export function periodLabel(days: number): string {
  const months: Record<number, string> = { 90: "3 months", 180: "6 months", 365: "12 months" };
  return months[days] ?? `${days} ${days === 1 ? "day" : "days"}`;
}

/** "×1.2". */
export function multiplierLabel(multiplier: number): string {
  return `×${multiplier.toFixed(1)}`;
}

/** "Oct 24": a home-zone day, short. */
export function shortDay(day: string): string {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

/** "Best: level 52, Veteran". */
export function bestText(best: { level: number; rankTitle: string }): string {
  return `Best: level ${best.level}, ${best.rankTitle}`;
}

/** How far a 1%-a-day fall moves from `exact` in a day, in words: "about half a level a day". */
export function fallSpeedText(exact: number, percentPerDay: number): string {
  const perDay = (exact * percentPerDay) / 100;
  if (perDay >= 1.5) return `about ${Math.round(perDay)} levels a day`;
  const words: [number, string][] = [
    [0.1, "a tenth of a level"],
    [0.2, "a fifth of a level"],
    [0.25, "a quarter of a level"],
    [1 / 3, "a third of a level"],
    [0.5, "half a level"],
    [2 / 3, "two-thirds of a level"],
    [0.75, "three-quarters of a level"],
    [1, "a level"],
  ];
  const nearest = words.reduce((best, w) => (Math.abs(w[0] - perDay) < Math.abs(best[0] - perDay) ? w : best));
  return `about ${nearest[1]} a day`;
}

/** Whether the domain's level is held by the home (environment plan §4.8): its consistency is a Home Score against a bar. */
export function isHomeHeld(row: Pick<DomainRow, "consistency">): boolean {
  return row.consistency?.kind === "home";
}

/** A bar as a threshold to reach: "68.2+". */
export function barPlus(bar: number | null | undefined): string {
  return `${barText(bar ?? 0)}+`;
}

/**
 * The card's one state line (§7.4.9): falling with what pauses it, paused, or which level sets
 * the shown level now. For a domain held by the home, a lifetime level above the condition
 * level reads as headroom: "Your effort has earned level 39. Your home holds you at 28."
 */
export function stateLine(row: DomainRow): string {
  const shown = row.shown!;
  const lifetime = row.lifetime!;
  const home = isHomeHeld(row);
  if (shown.paused) {
    return row.current?.todayException
      ? `Paused: today is an exception day, so the fall toward ${shown.target} waits.`
      : `Paused: your last ${shown.backOnTrack?.days ?? 7} days are back on track.`;
  }
  if (shown.falling) {
    const fall = `Falling ${shown.percentPerDay}% a day toward ${shown.target} (${fallSpeedText(shown.exact, shown.percentPerDay)}).`;
    const b = shown.backOnTrack;
    if (!b) return fall;
    return home
      ? `${fall} ${b.days} days back on track pauses it: a Home Score of ${barPlus(b.bar)} on ${b.activeDays} of them.`
      : `${fall} ${b.days} days back on track pauses it: ${b.average}+ base XP a day on average, with ${b.activeDays} active.`;
  }
  if (shown.limitedBy === "lifetime") {
    return lifetime.xpToNext > 0
      ? `Lifetime XP sets your level: ${lifetime.xpToNext.toLocaleString()} XP to level ${lifetime.level + 1}.`
      : "Lifetime XP sets your level.";
  }
  const days = row.consistency?.windowDays ?? 0;
  const level = row.consistency?.level ?? 1;
  if (home) {
    if (days === 0) return "Your level starts once you rate your spaces and a day settles.";
    return lifetime.level > level
      ? `Your effort has earned level ${lifetime.level}. Your home holds you at ${level}.`
      : `Your home supports level ${level}.`;
  }
  if (days === 0) return "Your level starts counting once a day settles.";
  return `Your last ${days === 90 ? "90 days" : `${days} ${days === 1 ? "day" : "days"}`} support level ${level}.`;
}

/**
 * What the home supports now, for a domain held by it (environment plan §4.8): "Your home
 * supports level 67: a Home Score of 66.5+ on 69 of the last 90 days."
 */
export function conditionText(row: DomainRow): string {
  const c = row.consistency!;
  const held = c.held;
  if (!held || held.windowDays === 0) {
    return c.windowDays === 0
      ? "Your home holds level 1 until you rate your spaces and a day settles."
      : `Your home supports level ${c.level}.`;
  }
  return `Your home supports level ${c.level}: a Home Score of ${barPlus(held.bar)} on ${held.activeDays} of the last ${held.windowDays} days.`;
}

/** "Down from 96 on Oct 24 · falling 1% a day toward 90", or null while nothing has fallen. */
export function fallHistoryText(row: DomainRow): string | null {
  const shown = row.shown!;
  if (!shown.fallingSince || shown.fallingFrom == null || shown.level >= shown.fallingFrom) return null;
  const since = `Down from ${shown.fallingFrom} on ${shortDay(shown.fallingSince)}`;
  return shown.falling ? `${since} · falling ${shown.percentPerDay}% a day toward ${shown.target}` : since;
}

/** One part of what the next consistency level needs, and whether it's met. */
export interface NeedPart {
  key: "average" | "active" | "age";
  text: string;
  met: boolean;
}

/**
 * What the next consistency level needs (§7.4.9), part by part: "a 90-day average of 49 base
 * XP (you're at 47)", "62 active days of 90 (you have 60)", and, while the domain is young,
 * "49 active days so far (you have 30)".
 */
export function needParts(next: DomainConsistencyNeed): NeedPart[] {
  const days = next.windowDays;
  if (next.bar != null) {
    // Held by the home: kept-up days at the level's bar, and how many so far.
    const home: NeedPart[] = [
      {
        key: "active",
        text: `a Home Score of ${barPlus(next.bar)} on ${next.activeDaysNeeded} of the last ${days} days (you have ${next.activeDays})`,
        met: next.activeDaysMet,
      },
    ];
    if (!next.ageMet) {
      home.push({ key: "age", text: `${next.ageNeeded} kept-up days so far (you have ${next.age})`, met: false });
    }
    return home;
  }
  const parts: NeedPart[] = [
    {
      key: "average",
      text: `a ${days}-day average of ${next.average} base XP (you're at ${next.averageNow.toLocaleString()})`,
      met: next.averageMet,
    },
    {
      key: "active",
      text: `${next.activeDaysNeeded} active ${next.activeDaysNeeded === 1 ? "day" : "days"} of ${days} (you have ${next.activeDays})`,
      met: next.activeDaysMet,
    },
  ];
  if (!next.ageMet) {
    parts.push({
      key: "age",
      text: `${next.ageNeeded} active days so far (you have ${next.age})`,
      met: false,
    });
  }
  return parts;
}

/** "Level 48 needs a 90-day average of 49 base XP (you're at 47) and 62 active days of 90 (you have 60)." */
export function needText(next: DomainConsistencyNeed): string {
  if (next.windowDays === 0) return `Level ${next.level} needs days to settle first.`;
  const parts = needParts(next).map((p) => p.text);
  const list = parts.length > 1 ? `${parts.slice(0, -1).join(", ")} and ${parts.at(-1)}` : parts[0];
  return `Level ${next.level} needs ${list}.`;
}

/**
 * Today against the active minimum, in words (§7.4.9); for a domain held by the home, today's
 * Home Score against the bar of the level going into today.
 */
export function todayText(current: DomainCurrent, kind: "xp" | "home" = "xp"): string {
  if (current.todayException) return "Today is an exception day: it doesn't count for or against you.";
  if (kind === "home") {
    if (current.todayHomeScore == null) return "Today: no Home Score yet. A walk-through starts it.";
    const score = shownPoints(current.todayHomeScore);
    return current.todayActive
      ? `Today: Home Score ${score}, kept up (${barPlus(current.bar)} counts)`
      : `Today: Home Score ${score}, under the ${barPlus(current.bar)} that counts as kept up`;
  }
  return current.todayActive
    ? `Today: ${current.todayAction} of ${current.activeMinimum} action XP, active`
    : `Today: ${current.todayAction} of ${current.activeMinimum} action XP to count as active`;
}

/** What a successful day is called: "active", or "kept up" for a domain held by the home. */
export function successWord(kind: "xp" | "home" = "xp"): string {
  return kind === "home" ? "kept up" : "active";
}

/** "×1.2 · 24 of the last 30 days active" (or "kept up"). */
export function multiplierChipText(current: DomainCurrent, kind: "xp" | "home" = "xp"): string {
  return `${multiplierLabel(current.multiplier)} · ${current.activeDays} of the last ${current.windowDays} days ${successWord(kind)}`;
}

/**
 * What holding a rank's first level takes: "a 90-day average of 34 base XP, 48 active days of
 * 90 (20+ action XP)", or for a domain held by the home "a Home Score of 34.5+ on 48 of 90 days".
 */
export function rankHoldText(rank: DomainRankRow): string {
  if (rank.rank === 0 || (rank.average == null && rank.bar == null)) return "where every domain starts";
  if (rank.bar != null) return `a Home Score of ${barPlus(rank.bar)} on ${rank.activeDays} of ${rank.windowDays} days`;
  return `a ${rank.windowDays}-day average of ${rank.average} base XP, ${rank.activeDays} active days of ${rank.windowDays} (${rank.activeMinimum}+ action XP)`;
}

/** "Marked Oct 24 as an exception": the confirmation a mark answers. */
export function exceptionMarkedMessage(day: string | undefined): string {
  return day ? `Marked ${shortDay(day)} as an exception` : "Marked today as an exception";
}

/** "Unmarked Oct 24: it counts again". */
export function exceptionUnmarkedMessage(day: string): string {
  return `Unmarked ${shortDay(day)}: it counts again`;
}

/** What moved, as a toast says it (§7.4.7). */
export type ProgressKind = "rank" | "level" | "back" | "lost";

/**
 * The toast for a domain that moved: a new best rank names the title (grand), a new best level
 * the level (full), getting back to a level below the best is a quiet note, and a level lost
 * says so quietly.
 */
export function progressMessage(change: DomainChange): { kind: ProgressKind; text: string } {
  const name = domainNameOf(change.domain);
  if (change.bestRankTo > change.bestRankFrom) {
    return { kind: "rank", text: `You're now a ${name} ${change.rankTitle}.` };
  }
  if (change.bestLevelTo > change.bestLevelFrom) {
    return { kind: "level", text: `${domainSymbol(change.domain)} ${name} level ${change.bestLevelTo}!`.trim() };
  }
  if (change.levelTo > change.levelFrom) {
    return { kind: "back", text: `${name} back to level ${change.levelTo}` };
  }
  return { kind: "lost", text: `${name} down to level ${change.levelTo}` };
}

// --- Report words ---------------------------------------------------------------

/** One configured domain's rank over a period, as the rank chart plots it. */
export interface RankSeries {
  key: string;
  name: string;
  days: DomainReportDay[];
  /** Rank titles, Novice first. */
  titles: string[];
}

/** "Health: Novice at the start, Apprentice at the end, best Apprentice." */
export function rankSummary(s: RankSeries): string {
  const first = s.days[0];
  const last = s.days.at(-1);
  if (!first || !last) return `${s.name}: no days yet.`;
  const title = (rank: number) => s.titles[rank] ?? `rank ${rank}`;
  const start = title(first.rank);
  const end = title(last.rank);
  const trend = start === end ? `${end} throughout` : `${start} at the start, ${end} at the end`;
  return `${s.name}: ${trend}, best ${title(last.bestRank)}.`;
}

/** "Level 12 (from 9) · Apprentice Oct 24": the bests a period reached. */
export function newBestsText(d: DomainReportRow): string | null {
  const parts: string[] = [];
  if (d.bestLevelFrom != null && d.bestLevelTo != null && d.bestLevelTo > d.bestLevelFrom) {
    parts.push(`Level ${d.bestLevelTo} (from ${d.bestLevelFrom})`);
  }
  for (const r of d.ranksReached ?? []) parts.push(`${r.title} ${formatDay(r.qualifiedOn)}`);
  return parts.length > 0 ? parts.join(" · ") : null;
}
