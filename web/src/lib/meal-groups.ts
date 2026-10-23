/**
 * Combined entries (FOOD-7, food-log plan §7.4), display only: on one day, entries with the same
 * name (ignoring case and surrounding spaces), score, calories, protein, carbs, fat and alcohol
 * flag show as one card, "Beer ×3", with the calories and nutrients summed and the time as a
 * range. The meal type and the photo don't count, since Log again re-guesses the meal type from
 * the time. Every copy stays its own entry; the XP is the same either way.
 */
import { formatClock } from "@/lib/food";
import type { MealRow } from "@/lib/production-ui-types";

export interface MealGroup {
  key: string;
  /** The copies, oldest first. */
  meals: MealRow[];
  first: MealRow;
  last: MealRow;
  count: number;
  calories: number;
  /** Summed, or null when the copies don't carry it. */
  proteinG: number | null;
  carbsG: number | null;
  fatG: number | null;
  /** The first copy's photo that has one. */
  photo: { artifactId: string; meal: MealRow } | null;
  /** Any copy eaten inside a recorded fast. */
  duringFast: boolean;
}

/** What makes two entries on a day the same entry. */
export function mealGroupKey(meal: MealRow): string {
  return JSON.stringify([
    meal.name.trim().toLowerCase(),
    meal.score,
    meal.calories,
    meal.proteinG,
    meal.carbsG,
    meal.fatG,
    meal.alcoholic,
  ]);
}

const times = (n: number, grams: number | null) => (grams === null ? null : n * grams);

/** A day's entries as cards, oldest first: each group sits at its first copy's time. */
export function groupMeals(meals: readonly MealRow[]): MealGroup[] {
  const ordered = [...meals].sort((a, b) => a.eatenAt.localeCompare(b.eatenAt) || a.id.localeCompare(b.id));
  const byKey = new Map<string, MealRow[]>();
  for (const meal of ordered) {
    const key = mealGroupKey(meal);
    byKey.set(key, [...(byKey.get(key) ?? []), meal]);
  }
  return [...byKey].map(([key, copies]) => {
    const first = copies[0];
    const withPhoto = copies.find((m) => m.photoArtifactId);
    return {
      key,
      meals: copies,
      first,
      last: copies[copies.length - 1],
      count: copies.length,
      calories: copies.length * first.calories,
      proteinG: times(copies.length, first.proteinG),
      carbsG: times(copies.length, first.carbsG),
      fatG: times(copies.length, first.fatG),
      photo: withPhoto?.photoArtifactId ? { artifactId: withPhoto.photoArtifactId, meal: withPhoto } : null,
      duringFast: copies.some((m) => m.duringFast !== null),
    };
  });
}

/** "9:00 PM", or "7:10 PM–9:30 PM" when the copies span more than one minute on the clock. */
export function groupTimeRange(group: MealGroup): string {
  const from = formatClock(group.first.eatenAt);
  const to = formatClock(group.last.eatenAt);
  return from === to ? from : `${from}–${to}`;
}

/** "Beer", or "Beer ×3". */
export function groupName(group: MealGroup): string {
  return group.count > 1 ? `${group.first.name} ×${group.count}` : group.first.name;
}

/**
 * What Have another starts from: the last copy, with the card's photo when the last copy has
 * none of its own, so the new entry shows the photo the card does.
 */
export function anotherFrom(group: MealGroup): MealRow {
  const last = group.last;
  return last.photoArtifactId || !group.photo ? last : { ...last, photoArtifactId: group.photo.artifactId };
}
