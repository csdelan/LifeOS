/**
 * Bounties (REWARD-2): what completing an item, or a habit day, streak or graduation,
 * pays. An item's `attributes.bounty` maps a trigger to its amounts; a trigger it doesn't
 * name pays the type default, and `{}` pays nothing. The kernel prices every earn; the UI
 * shows prices and edits them.
 *
 * A Task's `timed` trigger pays its price once for every `every_minutes` (stored inside
 * it; default 1) the timer recorded before the task was completed, on top of `completed`.
 * It is opt-in: no default, and nothing for a task done without the timer.
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
  Task: ["completed", "timed"],
  Project: ["completed"],
  Goal: ["completed"],
  Habit: ["followed", ...STREAK_MILESTONES.map((n) => `streak_${n}`), "established"],
};

// No default pays Coins (real money), Karma or Resolve: Chris awards those per item.
const DEFAULTS: Record<string, Record<string, Amounts>> = {
  Task: { completed: { freedom: 10, xp: 10 } },
  Project: { completed: { freedom: 30, gems: 1, xp: 50 } },
  Goal: { completed: { freedom: 60, gems: 3, xp: 150 } },
  Habit: {
    followed: { freedom: 5, xp: 5 },
    streak_7: { freedom: 15, xp: 25 },
    streak_30: { freedom: 30, xp: 75 },
    streak_100: { freedom: 90, gems: 1, xp: 200 },
    established: { freedom: 60, gems: 1, xp: 100 },
  },
};

/** The trigger that pays for time on a Task's timer. */
export const TIMED = "timed";
/** The key inside a stored `timed` price (and its draft) that holds the minutes per payment. */
export const EVERY_MINUTES = "every_minutes";
export const DEFAULT_EVERY_MINUTES = 1;

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

/** The minutes each `timed` payment takes, as `Bounty.ParseEveryMinutes` reads them; null when unset. */
export function parseEveryMinutes(raw: unknown): number | null {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const timed = Object.entries(raw as Record<string, unknown>).find(([k]) => k.toLowerCase() === TIMED)?.[1];
  if (!timed || typeof timed !== "object" || Array.isArray(timed)) return null;
  const n = (timed as Record<string, unknown>)[EVERY_MINUTES];
  return typeof n === "number" && Number.isInteger(n) && n >= 1 ? n : null;
}

/** The stored bounty as the kernel writes it (`Bounty.ToJson`): the minutes live inside `timed`, when not 1. */
export function bountyJson(stored: Record<string, Amounts>, everyMinutes: number | null): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [trigger, amounts] of Object.entries(stored)) {
    out[trigger] =
      trigger === TIMED && everyMinutes != null && everyMinutes !== DEFAULT_EVERY_MINUTES
        ? { ...amounts, [EVERY_MINUTES]: everyMinutes }
        : amounts;
  }
  return out;
}

/**
 * What the time on the timer pays (`Bounty.PriceTimed`): the per-payment price once for
 * every whole `everyMinutes` of `seconds`, rounded down; nothing under one.
 */
export function priceTimed(perPayment: Amounts, seconds: number, everyMinutes: number): Amounts {
  const payments = Math.floor(seconds / (60 * Math.max(everyMinutes, 1)));
  if (payments <= 0) return {};
  return Object.fromEntries(orderedAmounts(perPayment).map(([k, n]) => [k, n * payments]));
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

/**
 * Every trigger of the type, priced from a stored bounty. `timed` also carries its minutes
 * per payment, which only count while it is stored (clearing it puts them back on 1).
 */
export function linesFor(
  type: SubjectType,
  stored: Record<string, Amounts>,
  everyMinutes: number | null = null,
): BountyLine[] {
  return (BOUNTY_TRIGGERS[type] ?? []).map((trigger) => ({
    trigger,
    amounts: priceOf(type, trigger, stored),
    isCustom: !!stored[trigger],
    default: defaultBounty(type, trigger),
    ...(trigger === TIMED
      ? { everyMinutes: (stored[TIMED] ? everyMinutes : null) ?? DEFAULT_EVERY_MINUTES }
      : {}),
  }));
}

/** "per minute", "per 15 min": how often the time on the timer pays. */
export function perMinutes(everyMinutes: number | null | undefined): string {
  const n = everyMinutes ?? DEFAULT_EVERY_MINUTES;
  return n === 1 ? "per minute" : `per ${n} min`;
}

/** A trigger as the editor heads it: "When completed", "Each day followed", "7-day streak". */
export function bountyTriggerTitle(trigger: string): string {
  if (trigger === "completed") return "When completed";
  if (trigger === "followed") return "Each day followed";
  if (trigger === "established") return "When established";
  if (trigger === TIMED) return "For time on the timer";
  const streak = /^streak_(\d+)$/.exec(trigger);
  return streak ? `${streak[1]}-day streak` : trigger;
}

/** The lines a create form starts from: the type's defaults, from the API's list. */
export function defaultLines(type: SubjectType, defaults: BountyTypeDefaults[] | undefined): BountyLine[] {
  return defaults?.find((d) => d.type === type)?.lines ?? [];
}

/**
 * What's typed per trigger — only the triggers the editor touched. The `timed` draft also
 * holds its minutes per payment, under `EVERY_MINUTES`, beside the currencies.
 */
export type BountyDraft = Record<string, AmountDraft>;

/** What a line's editor starts from: a price, and for `timed` its minutes. */
export function lineDraft(
  line: BountyLine,
  amounts: Amounts = line.amounts,
  everyMinutes: number | null | undefined = line.everyMinutes,
): AmountDraft {
  const typed = draftFrom(amounts, CURRENCIES);
  return line.trigger === TIMED
    ? { ...typed, [EVERY_MINUTES]: String(everyMinutes ?? DEFAULT_EVERY_MINUTES) }
    : typed;
}

export function draftForLine(line: BountyLine, draft: BountyDraft): AmountDraft {
  return draft[line.trigger] ?? lineDraft(line);
}

/** The currencies a line's draft names (its minutes left out). */
export function draftAmounts(typed: AmountDraft): Amounts {
  const { [EVERY_MINUTES]: _minutes, ...currencies } = typed;
  return amountsFrom(currencies);
}

/** The minutes a `timed` draft pays for; a blank box is the default, 1. */
export function draftMinutes(typed: AmountDraft): number {
  const text = typed[EVERY_MINUTES]?.trim() ?? "";
  return text ? Number(text) : DEFAULT_EVERY_MINUTES;
}

/** True when a line's draft is its type default (for `timed`: pays nothing, every minute). */
export function draftOnDefault(line: BountyLine, typed: AmountDraft): boolean {
  return (
    sameAmounts(draftAmounts(typed), line.default) &&
    (line.trigger !== TIMED || draftMinutes(typed) === DEFAULT_EVERY_MINUTES)
  );
}

/** The first reason a bounty draft would be refused (`BountyService`), or null. */
export function bountyProblem(draft: BountyDraft): string | null {
  for (const amounts of Object.values(draft)) {
    for (const [key, text] of Object.entries(amounts)) {
      if (!text.trim()) continue;
      const n = Number(text);
      if (key === EVERY_MINUTES) {
        if (!Number.isInteger(n) || n < 1) return "Pay for every whole number of minutes on the timer from 1 up.";
        continue;
      }
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
 * A `timed` write carries its minutes.
 */
export function bountyWrites(subject: string, lines: BountyLine[], draft: BountyDraft): BountyRequest[] {
  const out: BountyRequest[] = [];
  for (const line of lines) {
    const typed = draft[line.trigger];
    if (!typed) continue;
    const amounts = draftAmounts(typed);
    const timed = line.trigger === TIMED;
    const minutes = timed ? { everyMinutes: draftMinutes(typed) } : {};
    const sameMinutes = !timed || draftMinutes(typed) === (line.everyMinutes ?? DEFAULT_EVERY_MINUTES);
    if (sameAmounts(amounts, line.amounts) && sameMinutes) continue;
    if (draftOnDefault(line, typed)) {
      out.push({ subject, trigger: line.trigger, clear: true });
    } else if (Object.keys(amounts).length === 0) {
      out.push({ subject, trigger: line.trigger, nothing: true, ...minutes });
    } else {
      // Every currency named, zeros included, so the stored price is exactly the draft.
      const whole: Amounts = Object.fromEntries(CURRENCIES.map((c) => [c.key, amounts[c.key] ?? 0]));
      out.push({ subject, trigger: line.trigger, amounts: whole, ...minutes });
    }
  }
  return out;
}

/** True while the draft would write something. */
export function bountyDirty(lines: BountyLine[], draft: BountyDraft): boolean {
  return bountyWrites("", lines, draft).length > 0;
}
