/**
 * Bounties (REWARD-2): what completing an item, or a habit day, streak or graduation,
 * pays. An item's `attributes.bounty` maps a trigger to its amounts; a trigger it doesn't
 * name pays the type default, and `{}` pays nothing. The kernel prices every earn; the UI
 * shows prices and edits them.
 *
 * `BOUNTY_TRIGGERS` and `BOUNTY_DEFAULTS` mirror `BountyTriggers` / `BountyDefaults` in
 * LifeOs.Domain for mock mode only: live mode reads the defaults from
 * `GET /api/bounty/defaults`, so retuning them never needs a web change.
 */
import type {
  Amounts,
  BountyLine,
  BountyRequest,
  BountyTypeDefaults,
  SubjectType,
} from "@/lib/production-ui-types";
import {
  CURRENCIES,
  amountsFrom,
  currency,
  draftFrom,
  orderedAmounts,
  sameAmounts,
  type AmountDraft,
} from "@/lib/rewards";

export const STREAK_MILESTONES = [7, 30, 100] as const;

export const BOUNTY_TRIGGERS: Partial<Record<SubjectType, readonly string[]>> = {
  Task: ["completed"],
  Project: ["completed"],
  Goal: ["completed"],
  Habit: ["followed", ...STREAK_MILESTONES.map((n) => `streak_${n}`), "established"],
};

const DEFAULTS: Record<string, Record<string, Amounts>> = {
  Task: { completed: { coins: 2, freedom: 10, xp: 10 } },
  Project: { completed: { coins: 10, freedom: 30, gems: 1, xp: 50 } },
  Goal: { completed: { coins: 25, freedom: 60, gems: 3, xp: 150 } },
  Habit: {
    followed: { coins: 1, freedom: 5, xp: 5 },
    streak_7: { coins: 5, freedom: 15, xp: 25 },
    streak_30: { coins: 15, freedom: 30, xp: 75 },
    streak_100: { coins: 40, freedom: 90, gems: 1, xp: 200 },
    established: { coins: 25, freedom: 60, gems: 1, xp: 100 },
  },
};

export function earns(type: SubjectType): boolean {
  return (BOUNTY_TRIGGERS[type]?.length ?? 0) > 0;
}

export function defaultBounty(type: SubjectType, trigger: string): Amounts {
  return { ...(DEFAULTS[type]?.[trigger] ?? {}) };
}

/** The mock's `GET /api/bounty/defaults`. */
export const BOUNTY_DEFAULTS: BountyTypeDefaults[] = (["Goal", "Habit", "Project", "Task"] as const).map(
  (type) => ({ type, lines: linesFor(type, {}) }),
);

/** The stored bounty, read leniently as the kernel does (`Bounty.Parse`). */
export function parseBounty(raw: unknown): Record<string, Amounts> {
  const out: Record<string, Amounts> = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return out;
  for (const [trigger, value] of Object.entries(raw as Record<string, unknown>)) {
    if (!value || typeof value !== "object" || Array.isArray(value)) continue;
    const amounts: Amounts = {};
    for (const [key, n] of Object.entries(value as Record<string, unknown>)) {
      const c = currency(key);
      if (c && typeof n === "number" && Number.isInteger(n) && n > 0) amounts[c.key] = n;
    }
    out[trigger.toLowerCase()] = Object.fromEntries(orderedAmounts(amounts));
  }
  return out;
}

/** What a trigger pays (`Bounty.Price`): a partial day is half a followed one, rounded down. */
export function priceOf(type: SubjectType, trigger: string, stored: Record<string, Amounts>): Amounts {
  if (trigger === "partial") {
    const followed = priceOf(type, "followed", stored);
    const out: Amounts = {};
    for (const [k, n] of Object.entries(followed)) if (Math.floor(n / 2) > 0) out[k] = Math.floor(n / 2);
    return out;
  }
  return stored[trigger] ? { ...stored[trigger] } : defaultBounty(type, trigger);
}

/** Every trigger of the type, priced from a stored bounty. */
export function linesFor(type: SubjectType, stored: Record<string, Amounts>): BountyLine[] {
  return (BOUNTY_TRIGGERS[type] ?? []).map((trigger) => ({
    trigger,
    amounts: priceOf(type, trigger, stored),
    isCustom: !!stored[trigger],
    default: defaultBounty(type, trigger),
  }));
}

/** A trigger as the editor heads it: "When completed", "Each day followed", "7-day streak". */
export function bountyTriggerTitle(trigger: string): string {
  if (trigger === "completed") return "When completed";
  if (trigger === "followed") return "Each day followed";
  if (trigger === "established") return "When established";
  const streak = /^streak_(\d+)$/.exec(trigger);
  return streak ? `${streak[1]}-day streak` : trigger;
}

/** The lines a create form starts from: the type's defaults, from the API's list. */
export function defaultLines(type: SubjectType, defaults: BountyTypeDefaults[] | undefined): BountyLine[] {
  return defaults?.find((d) => d.type === type)?.lines ?? [];
}

/** What's typed per trigger — only the triggers the editor touched. */
export type BountyDraft = Record<string, AmountDraft>;

export function draftForLine(line: BountyLine, draft: BountyDraft): AmountDraft {
  return draft[line.trigger] ?? draftFrom(line.amounts, CURRENCIES);
}

/** The first reason a bounty draft would be refused (`BountyService`), or null. */
export function bountyProblem(draft: BountyDraft): string | null {
  for (const amounts of Object.values(draft)) {
    for (const [key, text] of Object.entries(amounts)) {
      if (!text.trim()) continue;
      const n = Number(text);
      if (!Number.isInteger(n) || n < 0) {
        return `A bounty pays a whole number of ${currency(key)?.label ?? key} from zero up.`;
      }
    }
  }
  return null;
}

/**
 * The writes a draft needs, saving only what changed: an untouched or unchanged trigger
 * writes nothing; one edited back to its default is cleared, so later retuning of the
 * default still reaches it; one emptied pays nothing; anything else stores its whole price.
 */
export function bountyWrites(subject: string, lines: BountyLine[], draft: BountyDraft): BountyRequest[] {
  const out: BountyRequest[] = [];
  for (const line of lines) {
    const typed = draft[line.trigger];
    if (!typed) continue;
    const amounts = amountsFrom(typed);
    if (sameAmounts(amounts, line.amounts)) continue;
    if (sameAmounts(amounts, line.default)) {
      out.push({ subject, trigger: line.trigger, clear: true });
    } else if (Object.keys(amounts).length === 0) {
      out.push({ subject, trigger: line.trigger, nothing: true });
    } else {
      // Every currency named, zeros included, so the stored price is exactly the draft.
      const whole: Amounts = Object.fromEntries(CURRENCIES.map((c) => [c.key, amounts[c.key] ?? 0]));
      out.push({ subject, trigger: line.trigger, amounts: whole });
    }
  }
  return out;
}

/** True while the draft would write something. */
export function bountyDirty(lines: BountyLine[], draft: BountyDraft): boolean {
  return bountyWrites("", lines, draft).length > 0;
}
