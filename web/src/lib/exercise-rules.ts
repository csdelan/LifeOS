/**
 * What counts as exercise (ACTIVITY-3, activity plan §2, §3.4): a port of the kernel's
 * `ExerciseRules`, tested on the same table so the mock and the API agree. Only workouts Chris
 * records himself count — entered in the Oura app, or recorded live — and never a walk, whose
 * steps already count. Workouts Oura detected, confirmed or not, don't count, and nor does a
 * source Oura adds later until it's classified here.
 */

export const WALK_REASON = "A walk: it's in your steps";
export const DETECTED_REASON = "Detected by Oura";
export const UNKNOWN_SOURCE_REASON = "Unknown source";

/** Workouts Chris recorded himself: entered in the app, or recorded live. */
export const COUNTED_SOURCES: ReadonlySet<string> = new Set([
  "manual",
  "workout_heart_rate",
  "live_oura_heart_rate",
  "live_third_party_heart_rate",
]);

/** Workouts Oura found on its own, whether or not Chris confirmed them. */
export const DETECTED_SOURCES: ReadonlySet<string> = new Set(["autodetected", "confirmed"]);

/** Activities that never count: a walk is already in the day's steps. */
export const EXCLUDED_ACTIVITIES: ReadonlySet<string> = new Set(["walking"]);

export interface ExerciseVerdict {
  counted: boolean;
  reason: string | null;
}

/** Whether a workout counts as exercise, and if not, why (in words). Case and spaces don't matter. */
export function exerciseVerdict(source: string | null | undefined, activity: string | null | undefined): ExerciseVerdict {
  if (activity != null && EXCLUDED_ACTIVITIES.has(activity.trim().toLowerCase())) {
    return { counted: false, reason: WALK_REASON };
  }

  const from = (source ?? "").trim().toLowerCase();
  if (COUNTED_SOURCES.has(from)) return { counted: true, reason: null };
  return { counted: false, reason: DETECTED_SOURCES.has(from) ? DETECTED_REASON : UNKNOWN_SOURCE_REASON };
}
