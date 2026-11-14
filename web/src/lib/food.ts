/**
 * The Food Log's words (FOOD-1..4): what a food write says when it lands, and how a day's
 * score reads.
 */
import { DIET_RULES, fastingFor, formatDuration, formatPart } from "@/lib/diet-score";
import { multiplierLabel } from "@/lib/domains";
import type {
  FoodDaySummary,
  FoodMealType,
  FoodWriteResult,
} from "@/lib/production-ui-types";

/** What a food write says when it lands: "Logged Salmon salad", "Fast ended · 23 h 05 m". */
export function foodWriteMessage(result: FoodWriteResult): string {
  const fastLength = result.fast?.seconds != null ? ` · ${formatDuration(result.fast.seconds)}` : "";
  switch (result.action) {
    case "logged":
      return result.fast ? `Logged ${result.meal?.name ?? "food"} and ended your fast${fastLength}` : `Logged ${result.meal?.name ?? "food"}`;
    case "edited":
      return `Saved ${result.meal?.name ?? "the entry"}`;
    case "deleted":
      return `Deleted ${result.meal?.name ?? "the entry"}`;
    case "fast_begun":
      return "Fast begun";
    case "fast_ended":
      return `Fast ended${fastLength}`;
    case "fast_recorded":
      return `Fast recorded${fastLength}`;
    case "fast_edited":
      return "Fast saved";
    case "fast_deleted":
      return "Fast deleted";
    case "closed":
      return "Day closed";
    case "already_closed":
      return "That day is already closed";
    case "photo_set":
      return `Photo added to ${result.meal?.name ?? "the entry"}`;
    case "photo_removed":
      return `Photo removed from ${result.meal?.name ?? "the entry"}`;
    default:
      return "Saved";
  }
}

// ---------------------------------------------------------------------------
// Times on the page: `<input type="datetime-local">` speaks local wall time.
// ---------------------------------------------------------------------------

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** "2026-10-20T12:30" for an instant, in local time. */
export function toLocalInput(iso: string | number | Date): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** The instant a `datetime-local` value names, or null when it's incomplete. */
export function fromLocalInput(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) return null;
  const ms = new Date(value).getTime();
  return Number.isNaN(ms) ? null : new Date(ms).toISOString();
}

/** "12:30 pm". */
export function formatClock(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

/** "Mon 8:30 pm" — a time on another day than `day`, else just the clock. */
export function formatClockOn(iso: string, day: string): string {
  const d = new Date(iso);
  const local = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  return local === day ? formatClock(iso) : `${d.toLocaleDateString(undefined, { weekday: "short" })} ${formatClock(iso)}`;
}

/**
 * The score's fill alone, for a swatch, whole and half points (food-log plan §8). The ramp's
 * middle steps (3 light, 2 dark) reach only about 4:1 against either ink: enough for an icon,
 * not for small text, so text never sits on it.
 */
export const SCORE_FILL: Record<number, string> = {
  1: "bg-score-1",
  1.5: "bg-score-1-5",
  2: "bg-score-2",
  2.5: "bg-score-2-5",
  3: "bg-score-3",
  3.5: "bg-score-3-5",
  4: "bg-score-4",
  4.5: "bg-score-4-5",
  5: "bg-score-5",
};

/**
 * The score tile's fill and the ink an icon on it wears (the ramp in `index.css`): the light
 * ink below 3, the dark one from 3, so a half's icon reaches 3.4:1 or more in both themes.
 */
export const SCORE_TILE: Record<number, string> = {
  1: "bg-score-1 text-score-ink-low",
  1.5: "bg-score-1-5 text-score-ink-low",
  2: "bg-score-2 text-score-ink-low",
  2.5: "bg-score-2-5 text-score-ink-low",
  3: "bg-score-3 text-score-ink-high",
  3.5: "bg-score-3-5 text-score-ink-high",
  4: "bg-score-4 text-score-ink-high",
  4.5: "bg-score-4-5 text-score-ink-high",
  5: "bg-score-5 text-score-ink-high",
};

const MEAL_WORDS: Record<FoodMealType, string> = {
  breakfast: "Breakfast",
  lunch: "Lunch",
  dinner: "Dinner",
  snack: "Snack",
  drink: "Drink",
};

export function mealTypeWord(type: FoodMealType): string {
  return MEAL_WORDS[type] ?? type;
}

/** "×1.2" when either award was paid at a multiplier. */
function paidMultiplier(summary: FoodDaySummary): number | null {
  return summary.paid.diet?.multiplier ?? summary.paid.fast?.multiplier ?? null;
}

/** The day's standing in words: on track, closed by hand, or closed because it's over. */
export function summaryHeadline(summary: FoodDaySummary): string {
  if (!summary.closed) return `On track for ${summary.total} of ${summary.cap}`;
  const multiplier = paidMultiplier(summary);
  if (summary.paid.total === 0) return summary.meals > 0 ? "Closed · nothing to pay" : "Nothing logged";
  return `${summary.paid.baseTotal} base → ${summary.paid.total} Health XP${multiplier ? ` (${multiplierLabel(multiplier)})` : ""}`;
}

/** What a fast of `seconds` is earning: nothing yet, so far, or the full 8. */
export function fastEarningText(seconds: number): string {
  const from = DIET_RULES.fastFromHours * 3600;
  if (seconds < from) return `Pays from ${DIET_RULES.fastFromHours} h · ${formatDuration(from - seconds)} to go`;
  if (seconds >= DIET_RULES.fastFullAtHours * 3600) return `The full ${DIET_RULES.fastingMax} XP`;
  return `Earning since ${DIET_RULES.fastFromHours} h · ${formatPart(fastingFor(seconds))} of ${DIET_RULES.fastingMax} XP`;
}
