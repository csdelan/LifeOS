/**
 * Activity XP (ACTIVITY-5, activity plan §3.8), ported from `ActivityRules` in the kernel's
 * Domain so the mock pays exactly what the kernel would and the page can project a day that
 * hasn't paid yet. Up to 80 base Health XP a day:
 *
 * - Steps pay up to 25 on a straight line: 0 → 0, 2,500 → 5, 5,000 → 12, 7,500 → 20, 10,000 → 25.
 * - Counted exercise pays up to 35 in tiers: 10 minutes 8, 20 → 15, 30 → 25, 45 → 35. A day's
 *   counted workouts add up to the second, with overlapping times merged.
 * - The sleep score pays up to 20 on a straight line: 50 → 0, 60 → 5, 70 → 10, 80 → 15, 85 → 20.
 * - Readiness and Oura's Activity Score pay nothing.
 *
 * Each part is rounded, halves up, and pays as its own award. Missing data pays nothing and is
 * never read as 0. Days before 2026-10-22 never pay.
 */
import { roundHalfUp } from "@/lib/diet-score";

export interface CurvePoint {
  at: number;
  xp: number;
}

export interface ExerciseTier {
  fromMinutes: number;
  xp: number;
}

export const ACTIVITY_RULES = {
  currency: "health_xp",
  stepsCurve: [
    { at: 0, xp: 0 },
    { at: 2_500, xp: 5 },
    { at: 5_000, xp: 12 },
    { at: 7_500, xp: 20 },
    { at: 10_000, xp: 25 },
  ] as readonly CurvePoint[],
  exerciseTiers: [
    { fromMinutes: 10, xp: 8 },
    { fromMinutes: 20, xp: 15 },
    { fromMinutes: 30, xp: 25 },
    { fromMinutes: 45, xp: 35 },
  ] as readonly ExerciseTier[],
  sleepCurve: [
    { at: 50, xp: 0 },
    { at: 60, xp: 5 },
    { at: 70, xp: 10 },
    { at: 80, xp: 15 },
    { at: 85, xp: 20 },
  ] as readonly CurvePoint[],
  /** The first day activity pays (question 7). */
  xpFrom: "2026-10-22",
} as const;

const last = <T>(items: readonly T[]): T => items[items.length - 1];

export const STEPS_MAX = last(ACTIVITY_RULES.stepsCurve).xp;
export const EXERCISE_MAX = last(ACTIVITY_RULES.exerciseTiers).xp;
export const SLEEP_MAX = last(ACTIVITY_RULES.sleepCurve).xp;

/** The most base Health XP a day's activity can pay. */
export const ACTIVITY_CAP = STEPS_MAX + EXERCISE_MAX + SLEEP_MAX;

/** What the score reads of one Oura day; a null is data Oura doesn't have. */
export interface ActivityFacts {
  steps: number | null;
  exerciseSeconds: number;
  sleepScore: number | null;
}

/** A day's activity XP before the multiplier; steps and sleep are null when Oura has no data. */
export interface ActivityScore {
  steps: number | null;
  exercise: number;
  sleep: number | null;
  total: number;
}

/** A straight line through the curve's points, flat before the first and after the last. */
function along(curve: readonly CurvePoint[], x: number): number {
  if (x <= curve[0].at) return curve[0].xp;
  for (let i = 1; i < curve.length; i++) {
    const low = curve[i - 1];
    const high = curve[i];
    if (x <= high.at) return low.xp + ((high.xp - low.xp) * (x - low.at)) / (high.at - low.at);
  }
  return last(curve).xp;
}

/** Whether a day (`yyyy-MM-dd`) can pay at all: on or after 2026-10-22. */
export function activityPays(day: string): boolean {
  return day >= ACTIVITY_RULES.xpFrom;
}

/** The steps part, unrounded. */
export function stepsPart(steps: number): number {
  return along(ACTIVITY_RULES.stepsCurve, steps);
}

/** What a day's steps pay: the steps part, halves up. */
export function stepsXp(steps: number): number {
  return roundHalfUp(stepsPart(steps));
}

/** The sleep part, unrounded. */
export function sleepPart(score: number): number {
  return along(ACTIVITY_RULES.sleepCurve, score);
}

/** What a sleep score pays: the sleep part, halves up. */
export function sleepXp(score: number): number {
  return roundHalfUp(sleepPart(score));
}

/** What a day's counted exercise pays: the highest tier its seconds reach (10 minutes is 600 seconds), else 0. */
export function exerciseXp(seconds: number): number {
  let xp = 0;
  for (const tier of ACTIVITY_RULES.exerciseTiers) {
    if (seconds >= tier.fromMinutes * 60) xp = tier.xp;
  }
  return xp;
}

/** A workout as the exercise count reads it: whether it counts, and its span (ISO times). */
export interface ExerciseSpan {
  counted: boolean;
  startedAt: string;
  endedAt: string;
}

/**
 * One Oura day's counted exercise in seconds: the workouts that count, with any times they
 * overlap merged, so a workout split in two pays no more and two overlapping records count once.
 */
export function exerciseSeconds(workouts: readonly ExerciseSpan[]): number {
  const spans = workouts
    .filter((w) => w.counted)
    .map((w) => [Date.parse(w.startedAt), Date.parse(w.endedAt)] as const)
    .filter(([start, end]) => end > start)
    .sort((a, b) => a[0] - b[0]);

  let totalMs = 0;
  let from: number | null = null;
  let to: number | null = null;
  for (const [start, end] of spans) {
    if (to !== null && start <= to) {
      if (end > to) to = end;
      continue;
    }
    if (from !== null && to !== null) totalMs += to - from;
    from = start;
    to = end;
  }
  if (from !== null && to !== null) totalMs += to - from;
  return Math.floor(totalMs / 1000);
}

/** Scores one Oura day (activity plan §3.8). */
export function scoreActivityDay(facts: ActivityFacts): ActivityScore {
  const steps = facts.steps === null ? null : stepsXp(facts.steps);
  const exercise = exerciseXp(facts.exerciseSeconds);
  const sleep = facts.sleepScore === null ? null : sleepXp(facts.sleepScore);
  return { steps, exercise, sleep, total: (steps ?? 0) + exercise + (sleep ?? 0) };
}
