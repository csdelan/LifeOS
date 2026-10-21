/**
 * What an earning write says back (REWARD-4): a toast with what it paid and a small chime
 * on every earn, and the full celebration (confetti and the fanfare) when it pays Gems,
 * reaches a streak milestone or establishes a habit. A reversal — reopening a completed
 * Task, correcting a day — gets a quiet toast and no sound.
 *
 * A life domain that moved (xp-domains plan §3.8) gets its own toast: a level-up the full
 * celebration ("❤️ Health level 12!"), a rank-up the grand one with its title ("You're now a
 * Health Journeyman."), and a level lost to a reversal a quiet one. One write celebrates once,
 * at the size of the biggest thing it did.
 */
import { toast } from "sonner";
import { celebrate, chime } from "@/lib/celebrate";
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

function forWhat(awards: Award[]): string {
  const byItem = new Map<string, string[]>();
  for (const a of awards) {
    const title = a.subject?.title ?? a.note ?? "Reward";
    const multiplied = multipliedLabel(a);
    const why = multiplied ? `${triggerLabel(a.trigger)} (${multiplied})` : triggerLabel(a.trigger);
    byItem.set(title, [...(byItem.get(title) ?? []), why]);
  }
  return [...byItem].map(([title, why]) => `${title} — ${why.join(", ")}`).join(" · ");
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
 * celebration for a rank-up, the full one for a level-up or a big earn, else a chime for any
 * earn. Losing a level or giving back is quiet.
 */
export function announceEarned(earned: { awards?: Award[]; progress?: DomainChange[] } | undefined) {
  const awards = earned?.awards ?? [];
  const moved = (earned?.progress ?? []).map(progressMessage);
  let celebration: "none" | "chime" | "full" | "grand" = "none";

  if (awards.length > 0) {
    const summary = summarizeAwards(awards);
    if (Object.keys(summary.earned).length > 0) {
      toast(`Earned ${formatAmounts(summary.earned, { signed: true })}`, {
        icon: summary.big ? "🎉" : "🪙",
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

  for (const m of moved) {
    if (m.kind === "rank") {
      toast(m.text, { icon: "🏅", description: "A new rank: see it on your Character page." });
      celebration = "grand";
    } else if (m.kind === "level") {
      toast(m.text, { icon: "⬆️" });
      if (celebration !== "grand") celebration = "full";
    } else {
      toast(m.text, { icon: "↩", description: "A reversal took back the XP that level needed." });
    }
  }

  if (celebration === "grand") celebrate("grand");
  else if (celebration === "full") celebrate();
  else if (celebration === "chime") chime();
}
