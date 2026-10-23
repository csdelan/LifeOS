/**
 * The diet score (food-log plan §2), ported from `DietRules` in the kernel's Domain so the
 * mock pays exactly what the kernel would and the page can show the working. Up to 40 base
 * Health XP a day: food quality 28, calories 4, fasting 8.
 *
 * - Quality is a straight line on the calorie-weighted average score: 1 pays 0, 5 pays 28, and a
 *   half point (3.5, plan §8) pays halfway between its neighbours.
 * - Calories pay 4 from 500 to 2,000, fading evenly to 0 at 2,500.
 * - Fasting pays nothing up to 12 hours, then evenly up to 8 at 24 hours, by the second.
 * - Drinks (FOOD-6, plan §7.2): an alcoholic entry scores 1, and each drink above two cuts the
 *   day's diet XP by 10%, both awards, so twelve leave nothing.
 *
 * The food award (quality + calories) and the fast award are each cut, then rounded, halves up.
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
  /** Drinks a day takes before the cut starts. */
  drinksFree: 2,
  /** What each drink above `drinksFree` takes off the day's diet XP, in percent. */
  drinkCutPercent: 10,
} as const;

export const SCORE_MIN = 1;
/** A drink's score: always the lowest. */
export const DRINK_SCORE = SCORE_MIN;
export const SCORE_MAX = 5;
/** The step a score moves in (food-log plan §8): whole and half points, so 3.5 sits between 3 and 4. */
export const SCORE_STEP = 0.5;

/** Whether `score` is a health score: 1 to 5 in half steps, as `FoodEntries.IsScore`. */
export function isScore(score: number): boolean {
  return Number.isFinite(score) && score >= SCORE_MIN && score <= SCORE_MAX && Number.isInteger(score / SCORE_STEP);
}
export const CALORIES_MAX = 5000;
export const GRAMS_MAX = 1000;
export const NAME_MAX = 80;

/** The most base Health XP a day's diet can pay. */
export const DIET_CAP = DIET_RULES.qualityMax + DIET_RULES.caloriesMax + DIET_RULES.fastingMax;

/** What the score reads of a day: its calories, Σ score × calories, its longest fast and its drinks. */
export interface DietFacts {
  calories: number;
  scoreCalories: number;
  longestFastSeconds: number | null;
  drinks?: number;
}

/** A day's score: the three parts unrounded, the two awards cut and rounded, and the average. */
export interface DietScore {
  quality: number;
  caloriesPart: number;
  fasting: number;
  foodAward: number;
  fastAward: number;
  total: number;
  /** The calorie-weighted average score, or null when the day has no calories. */
  averageScore: number | null;
  drinks: number;
  /** What's left after the drinks cut: 1 up to two drinks, 0 from twelve. */
  drinkFactor: number;
  /** The fast award before the cut: what a fast pays while its day is open. */
  fastAwardUncut: number;
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

/** The drinks at which the cut leaves nothing (12). */
export const DRINKS_TO_NOTHING =
  DIET_RULES.drinksFree + Math.ceil(100 / DIET_RULES.drinkCutPercent);

/** The percent of the day's diet XP `drinks` leave: 100 up to two, 10 less for each one more. */
function percentLeft(drinks: number): number {
  return Math.max(0, 100 - DIET_RULES.drinkCutPercent * Math.max(0, drinks - DIET_RULES.drinksFree));
}

/** What's left of the day's diet XP after `drinks`: 1, 0.9, … 0. */
export function drinkFactorFor(drinks: number): number {
  return percentLeft(drinks) / 100;
}

/** Scores one day (food-log plan §2.1, the cut in §7.2). */
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
  const drinks = Math.max(0, facts.drinks ?? 0);
  const left = percentLeft(drinks);
  // Cut, then rounded: as one division by 100, so 90% of 7.33 is 6.6 and rounds to 7.
  const foodAward = roundHalfUp(((quality + caloriesPart) * left) / 100);
  const fastAward = roundHalfUp((fasting * left) / 100);
  return {
    quality,
    caloriesPart,
    fasting,
    foodAward,
    fastAward,
    total: foodAward + fastAward,
    averageScore,
    drinks,
    drinkFactor: left / 100,
    fastAwardUncut: roundHalfUp(fasting),
  };
}

/** "1 drink", "4 drinks". */
export function drinksText(drinks: number): string {
  return `${drinks} drink${drinks === 1 ? "" : "s"}`;
}

/**
 * A day's drinks in words: "2 drinks · the cut starts above 2", "4 drinks · 20% off the day's
 * diet XP", or null with none.
 */
export function drinksLine(drinks: number): string | null {
  if (drinks <= 0) return null;
  const left = percentLeft(drinks);
  return left === 100
    ? `${drinksText(drinks)} · the cut starts above ${DIET_RULES.drinksFree}`
    : `${drinksText(drinks)} · ${100 - left}% off the day's diet XP`;
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

/**
 * The half points between the whole ones (food-log plan §8), each named by its neighbours:
 * 3.5 is "average to good".
 */
export const HALF_SCORES: readonly { score: number; word: string; between: readonly [number, number] }[] =
  SCORES.slice(0, -1).map((low, i) => {
    const high = SCORES[i + 1];
    return { score: low.score + SCORE_STEP, word: `${low.word} to ${high.word}`, between: [low.score, high.score] as const };
  });

/** The word for a score, whole or half: "very healthy", "average to good"; undefined for anything else. */
export function scoreWord(score: number): string | undefined {
  return (SCORES.find((s) => s.score === score) ?? HALF_SCORES.find((s) => s.score === score))?.word;
}

/** "5 · very healthy", "3.5 · average to good". */
export function scoreLabel(score: number): string {
  const word = scoreWord(score);
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
