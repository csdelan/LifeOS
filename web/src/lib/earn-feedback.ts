/**
 * What an earning write says back (REWARD-4): a toast with what it paid and a small chime
 * on every earn, and the full celebration (confetti and the fanfare) when it pays Gems,
 * reaches a streak milestone or establishes a habit. A reversal — reopening a completed
 * Task, correcting a day — gets a quiet toast and no sound.
 *
 * A life domain that moved (xp-domains plan §7.4.7) gets its own toast: a new best level the
 * full celebration ("❤️ Health level 12!"), a new best rank the grand one with its title
 * ("You're now a Health Journeyman."), getting back to a level below the best a quiet note,
 * and a level lost a quiet toast saying why. One write celebrates once, at the size of the
 * biggest thing it did.
 *
 * A food day re-scored in one write (given back, then earned at its new score) is one toast:
 * "Food day Tue Oct 20 re-scored: 38 → 35 Health XP". Activity XP from Oura (activity plan
 * §3.8) toasts the same way, its note saying the part and the day: "Steps Thu Oct 22",
 * "Steps Thu Oct 22 re-scored: 20 → 25 Health XP".
 */
import { toast } from "sonner";
import { celebrate, chime } from "@/lib/celebrate";
import { keepBest } from "@/lib/domain-best";
import { progressMessage } from "@/lib/domains";
import type { Amounts, Award, DomainChange } from "@/lib/production-ui-types";
import { formatAmounts, multipliedLabel, triggerLabel } from "@/lib/rewards";

export interface AwardSummary {
  earned: Amounts;
  givenBack: Amounts;
  /**
   * "Morning mobility — day followed, 7-day streak", one part per item; domain XP paid at a
   * multiplier adds its working: "day followed (❤️ 60 × 1.3)".
   */
  earnedFor: string;
  givenBackFor: string;
  /** Gems, a streak milestone or establishing a habit: the full celebration. */
  big: boolean;
}

function add(into: Amounts, amounts: Amounts) {
  for (const [k, n] of Object.entries(amounts)) into[k] = (into[k] ?? 0) + n;
}

/** Activity XP from Oura: its note already says what it was for. */
const ACTIVITY_TRIGGERS = new Set(["steps", "exercise", "sleep"]);

function forWhat(awards: Award[]): string {
  const byItem = new Map<string, string[]>();
  for (const a of awards) {
    const title = a.subject?.title ?? a.note ?? "Reward";
    const multiplied = multipliedLabel(a);
    const why = ACTIVITY_TRIGGERS.has(a.trigger)
      ? multiplied
      : multiplied
        ? `${triggerLabel(a.trigger)} (${multiplied})`
        : triggerLabel(a.trigger);
    byItem.set(title, [...(byItem.get(title) ?? []), why]);
  }
  return [...byItem]
    .map(([title, why]) => {
      const reasons = why.filter(Boolean);
      return reasons.length > 0 ? `${title} — ${reasons.join(", ")}` : title;
    })
    .join(" · ");
}

/**
 * The toast's icon for awards that belong to no item: a plate for diet XP, a moon for sleep,
 * a shoe for the rest of activity XP; null when anything else is among them.
 */
function itemlessIcon(awards: Award[]): string | null {
  if (awards.length === 0) return null;
  if (awards.every((a) => a.trigger === "diet" || a.trigger === "fast")) return "🍽️";
  if (awards.every((a) => a.trigger === "sleep")) return "🌙";
  if (awards.every((a) => ACTIVITY_TRIGGERS.has(a.trigger))) return "👟";
  return null;
}

/**
 * An award re-priced in one write (food-log plan §3.3): the reversal of what its key paid and
 * the earn at its new price, shown as one change — "Food day Tue Oct 20 re-scored: 38 → 35
 * Health XP" — not as a give-back and an earn.
 */
export interface Rescore {
  title: string;
  /** The award's trigger: `diet`, `fast`, `steps`… */
  trigger: string;
  from: Amounts;
  to: Amounts;
}

function negated(amounts: Amounts): Amounts {
  return Object.fromEntries(Object.entries(amounts).map(([k, n]) => [k, -n]));
}

/** Pairs each reversal with an earn of the same key in the same write; the rest stay as they are. */
export function splitRescores(awards: Award[]): { rescores: Rescore[]; rest: Award[] } {
  const paired = new Set<Award>();
  const rescores: Rescore[] = [];
  for (const reversal of awards.filter((a) => a.entry === "reversal")) {
    const earn = awards.find((a) => a.entry === "earn" && a.awardKey === reversal.awardKey && !paired.has(a));
    if (!earn) continue;
    paired.add(reversal);
    paired.add(earn);
    rescores.push({
      title: earn.subject?.title ?? earn.note ?? reversal.note ?? "Reward",
      trigger: earn.trigger,
      from: negated(reversal.amounts),
      to: earn.amounts,
    });
  }
  return { rescores, rest: awards.filter((a) => !paired.has(a)) };
}

/** "Food day Tue Oct 20 re-scored: 38 → 35 Health XP". */
export function rescoreMessage(r: Rescore): string {
  const from = Object.entries(r.from);
  const to = Object.entries(r.to);
  const sameOne = from.length === 1 && to.length === 1 && from[0][0] === to[0][0];
  const change = sameOne
    ? `${from[0][1].toLocaleString()} → ${formatAmounts(r.to)}`
    : `${formatAmounts(r.from)} → ${formatAmounts(r.to)}`;
  return `${r.title} re-scored: ${change}`;
}

function sum(amounts: Amounts): number {
  return Object.values(amounts).reduce((total, n) => total + n, 0);
}

export function summarizeAwards(awards: Award[]): AwardSummary {
  const earns = awards.filter((a) => a.entry === "earn");
  const reversals = awards.filter((a) => a.entry === "reversal");
  const earned: Amounts = {};
  const givenBack: Amounts = {};
  earns.forEach((a) => add(earned, a.amounts));
  reversals.forEach((a) => add(givenBack, a.amounts));
  return {
    earned,
    givenBack,
    earnedFor: forWhat(earns),
    givenBackFor: forWhat(reversals),
    big: earns.some(
      (a) => (a.amounts.gems ?? 0) > 0 || a.trigger.startsWith("streak_") || a.trigger === "established",
    ),
  };
}

/** Toast what the write earned and gave back; chime, or celebrate the big ones. */
export function announceAwards(awards: Award[] | undefined) {
  announceEarned({ awards });
}

/**
 * Toast what an earning write earned, gave back and moved, then celebrate once: the grand
 * celebration for a new best rank, the full one for a new best level or a big earn, else a
 * chime for any earn. Losing a level, getting back to one or giving back is quiet. `lostBecause`
 * says why a level was lost when the write wasn't a reversal (an exception day's change).
 */
export function announceEarned(
  earned: { awards?: Award[]; progress?: DomainChange[] } | undefined,
  options: { lostBecause?: string } = {},
) {
  const awards = earned?.awards ?? [];
  const progress = earned?.progress ?? [];
  const moved = progress.map(progressMessage);
  // A best this write celebrates is kept, so the Character page doesn't celebrate it again.
  for (const change of progress) keepBest(change.domain, { level: change.bestLevelTo, rank: change.bestRankTo });
  let celebration: "none" | "chime" | "full" | "grand" = "none";

  if (awards.length > 0) {
    const { rescores, rest } = splitRescores(awards);
    for (const r of rescores) {
      toast(rescoreMessage(r), { icon: r.trigger === "sleep" ? "🌙" : ACTIVITY_TRIGGERS.has(r.trigger) ? "👟" : "🍽️" });
      if (sum(r.to) > sum(r.from)) celebration = "chime";
    }
    const summary = summarizeAwards(rest);
    if (Object.keys(summary.earned).length > 0) {
      // Diet and activity XP belong to no item: a plate, a moon or a shoe says what it was for.
      const itemless = itemlessIcon(rest.filter((a) => a.entry === "earn"));
      toast(`Earned ${formatAmounts(summary.earned, { signed: true })}`, {
        icon: summary.big ? "🎉" : (itemless ?? "🪙"),
        description: summary.earnedFor,
      });
      celebration = summary.big ? "full" : "chime";
    }
    if (Object.keys(summary.givenBack).length > 0) {
      toast(`Gave back ${formatAmounts(summary.givenBack, { signed: true })}`, {
        icon: "↩",
        description: summary.givenBackFor,
      });
    }
  }

  const reversed = awards.some((a) => a.entry === "reversal");
  for (const m of moved) {
    if (m.kind === "rank") {
      toast(m.text, { icon: "🏅", description: "A new best rank: see it on your Character page." });
      celebration = "grand";
    } else if (m.kind === "level") {
      toast(m.text, { icon: "⬆️", description: "A new best level." });
      if (celebration !== "grand") celebration = "full";
    } else if (m.kind === "back") {
      toast(m.text, { icon: "↗" });
    } else {
      toast(m.text, {
        icon: "↩",
        description:
          options.lostBecause ??
          (reversed ? "A reversal took back the XP that level needed." : "Your last 90 days support a lower level now."),
      });
    }
  }

  if (celebration === "grand") celebrate("grand");
  else if (celebration === "full") celebrate();
  else if (celebration === "chime") chime();
}
