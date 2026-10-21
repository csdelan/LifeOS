/**
 * What an earning write says back (REWARD-4): a toast with what it paid and a small chime
 * on every earn, and the full celebration (confetti and the fanfare) when it pays Gems,
 * reaches a streak milestone or establishes a habit — a rank-up pays Gems. A reversal — reopening a completed
 * Task, correcting a day — gets a quiet toast and no sound.
 */
import { toast } from "sonner";
import { celebrate, chime } from "@/lib/celebrate";
import type { Amounts, Award } from "@/lib/production-ui-types";
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
  if (!awards?.length) return;
  const summary = summarizeAwards(awards);
  if (Object.keys(summary.earned).length > 0) {
    toast(`Earned ${formatAmounts(summary.earned, { signed: true })}`, {
      icon: summary.big ? "🎉" : "🪙",
      description: summary.earnedFor,
    });
    if (summary.big) celebrate();
    else chime();
  }
  if (Object.keys(summary.givenBack).length > 0) {
    toast(`Gave back ${formatAmounts(summary.givenBack, { signed: true })}`, {
      icon: "↩",
      description: summary.givenBackFor,
    });
  }
}
