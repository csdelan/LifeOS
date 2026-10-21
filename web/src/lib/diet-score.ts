/**
 * The diet score (food-log plan §2), ported from `DietRules` in the kernel's Domain so the
 * mock pays exactly what the kernel would and the page can show the working. Up to 40 base
 * Health XP a day: food quality 28, calories 4, fasting 8.
 *
 * - Quality is a straight line on the calorie-weighted average score: 1 pays 0, 5 pays 28.
 * - Calories pay 4 from 500 to 2,000, fading evenly to 0 at 2,500.
 * - Fasting pays nothing up to 12 hours, then evenly up to 8 at 24 hours, by the second.
 *
 * The food award (quality + calories) and the fast award are each rounded, halves up.
 */
import type { FoodMealType } from "@/lib/production-ui-types";

export const DIET_RULES = {
  currency: "health_xp",
  qualityMax: 28,
  caloriesMax: 4,
  fastingMax: 8,
  calorieFloor: 500,
  calorieFullTo: 2000,
  calorieZeroAt: 2500,
  fastFromHours: 12,
  fastFullAtHours: 24,
} as const;

export const SCORE_MIN = 1;
export const SCORE_MAX = 5;
export const CALORIES_MAX = 5000;
export const GRAMS_MAX = 1000;
export const NAME_MAX = 80;

/** The most base Health XP a day's diet can pay. */
export const DIET_CAP = DIET_RULES.qualityMax + DIET_RULES.caloriesMax + DIET_RULES.fastingMax;

/** What the score reads of a day: its calories, Σ score × calories, and its longest fast. */
export interface DietFacts {
  calories: number;
  scoreCalories: number;
  longestFastSeconds: number | null;
}

/** A day's score: the three parts unrounded, the two awards rounded, and the average. */
export interface DietScore {
  quality: number;
  caloriesPart: number;
  fasting: number;
  foodAward: number;
  fastAward: number;
  total: number;
  /** The calorie-weighted average score, or null when the day has no calories. */
  averageScore: number | null;
}

/**
 * Whole XP, halves up. The parts are fractions of whole numbers, so a midpoint can land a
 * hair under .5 in floating point; the nudge keeps it a midpoint, as the kernel's decimals do.
 */
export function roundHalfUp(value: number): number {
  return Math.floor(value + 0.5 + 1e-9);
}

/** The calorie part: 0 under 500, 4 from 500 to 2,000, then fading evenly to 0 at 2,500. */
export function caloriesPartFor(calories: number): number {
  const r = DIET_RULES;
  if (calories < r.calorieFloor || calories >= r.calorieZeroAt) return 0;
  return calories <= r.calorieFullTo
    ? r.caloriesMax
    : (r.caloriesMax * (r.calorieZeroAt - calories)) / (r.calorieZeroAt - r.calorieFullTo);
}

/** The fasting part for a fast of `seconds`: nothing up to 12 h, then up to 8 at 24 h. */
export function fastingFor(seconds: number): number {
  const r = DIET_RULES;
  const from = r.fastFromHours * 3600;
  const span = (r.fastFullAtHours - r.fastFromHours) * 3600;
  const over = Math.min(Math.max(seconds - from, 0), span);
  return (r.fastingMax * over) / span;
}

/** Scores one day (food-log plan §2.1). */
export function scoreDay(facts: DietFacts): DietScore {
  const r = DIET_RULES;
  const steps = SCORE_MAX - SCORE_MIN;
  const averageScore = facts.calories > 0 ? facts.scoreCalories / facts.calories : null;
  // Σ(score × calories) / C − 1, as one division so the line is exact.
  const quality =
    facts.calories > 0
      ? (r.qualityMax * Math.min(Math.max(facts.scoreCalories - SCORE_MIN * facts.calories, 0), steps * facts.calories)) /
        (steps * facts.calories)
      : 0;
  const caloriesPart = caloriesPartFor(facts.calories);
  const fasting = fastingFor(facts.longestFastSeconds ?? 0);
  const foodAward = roundHalfUp(quality + caloriesPart);
  const fastAward = roundHalfUp(fasting);
  return { quality, caloriesPart, fasting, foodAward, fastAward, total: foodAward + fastAward, averageScore };
}

// ---------------------------------------------------------------------------
// Entries: meal types and score words
// ---------------------------------------------------------------------------

export const MEAL_TYPES: readonly FoodMealType[] = ["breakfast", "lunch", "dinner", "snack", "drink"];

/** The meal type a local time suggests: 04–10 breakfast, 11–15 lunch, 16–21 dinner, else a snack. */
export function guessMealType(local: Date): FoodMealType {
  const hour = local.getHours();
  if (hour >= 4 && hour < 11) return "breakfast";
  if (hour >= 11 && hour < 16) return "lunch";
  if (hour >= 16 && hour < 22) return "dinner";
  return "snack";
}

/** The score picker's words and examples (food-log plan §1). */
export const SCORES: readonly { score: number; word: string; examples: string }[] = [
  { score: 1, word: "junk", examples: "ice cream, fries, candy" },
  { score: 2, word: "indulgent", examples: "pizza, burger, pastry" },
  { score: 3, word: "average", examples: "a sandwich, pasta" },
  { score: 4, word: "good", examples: "grilled chicken and vegetables, oatmeal and fruit" },
  { score: 5, word: "very healthy", examples: "a salmon salad" },
];

/** "5 · very healthy". */
export function scoreLabel(score: number): string {
  const word = SCORES.find((s) => s.score === score)?.word;
  return word ? `${score} · ${word}` : String(score);
}

/** Quick calorie sizes to start from. */
export const CALORIE_SIZES: readonly { label: string; calories: number }[] = [
  { label: "Snack", calories: 200 },
  { label: "Light", calories: 400 },
  { label: "Meal", calories: 700 },
  { label: "Big", calories: 1000 },
];

/** A length of time: "23 h 05 m", or "45 m" under an hour (the kernel's `FoodText.Duration`). */
export function formatDuration(seconds: number): string {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  return minutes < 60 ? `${minutes} m` : `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, "0")} m`;
}

/** One decimal, without a trailing ".0": 23.7, 4, 2.7. */
export function formatPart(value: number): string {
  const rounded = Math.round(value * 10) / 10;
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1);
}
