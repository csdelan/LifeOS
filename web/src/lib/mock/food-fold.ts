/**
 * The mock's food views (migrations 0034 and 0038): food entries, fasts and day closes as
 * append-only events, folded the way `bsk.v_meal`, `bsk.v_fast` and `bsk.v_food_day` fold them.
 *
 * - Each entry or fast is versioned by its own id; the newest version by `recordedAt` (then
 *   write order) wins, never by when it was eaten, and a voided newest version deletes it.
 * - Days are local days (the mock's home zone is the browser's).
 * - A day is closed by hand, or because it's over.
 */
import { scoreDay, type DietScore } from "@/lib/diet-score";
import { localDay } from "@/lib/mock/domain-credit";
import type { FastRow, FoodDaySummary, FoodMealType, FoodPaid, MealRow } from "@/lib/production-ui-types";

export interface MockMealPayload {
  id: string;
  name?: string;
  score?: number;
  calories?: number;
  proteinG?: number | null;
  carbsG?: number | null;
  fatG?: number | null;
  mealType?: FoodMealType;
  photo?: string | null;
  /** An alcoholic drink (FOOD-6), left out when false. */
  alcoholic?: boolean;
  voided?: boolean;
}

export interface MockFastPayload {
  id: string;
  startedAt?: string;
  endedAt?: string | null;
  voided?: boolean;
}

/** One food event version: an entry (`meal`), a fast (`fast`) or a day's close (`close`). */
export interface MockFoodEvent {
  id: string;
  occurredAt: string;
  recordedAt: string;
  meal?: MockMealPayload;
  fast?: MockFastPayload;
  close?: { on: string };
}

/** A live entry, with the event version it reads. */
export interface FoldedMeal extends MealRow {
  eventId: string;
}

/** A live fast, with the event version it reads. */
export interface FoldedFast extends FastRow {
  eventId: string;
  recordedAt: string;
}

/** One day as `bsk.v_food_day` reads it. */
export interface FoodDayFacts {
  day: string;
  meals: number;
  calories: number;
  scoreCalories: number;
  longestFastSeconds: number | null;
  longestFastId: string | null;
  fastEventId: string | null;
  closedByHand: boolean;
  closedAt: string | null;
  closeEventId: string | null;
  closed: boolean;
  lastRecordedAt: string | null;
  /** The entry versions the day reads, by when they were eaten. */
  foodEventIds: string[];
  /** The day's alcoholic drinks, and their versions by when they were drunk. */
  drinks: number;
  drinkEventIds: string[];
}

export interface FoodFold {
  meals: FoldedMeal[];
  fasts: FoldedFast[];
  /** Days with a live entry, an ended fast or a close, oldest first. */
  days: Map<string, FoodDayFacts>;
}

function newestFirst(events: readonly MockFoodEvent[]): MockFoodEvent[] {
  // The array is append-only, so a later index is a later write when the times tie.
  return events
    .map((e, i) => ({ e, i }))
    .sort((a, b) => b.e.recordedAt.localeCompare(a.e.recordedAt) || b.i - a.i)
    .map(({ e }) => e);
}

function seconds(from: string, to: string): number {
  return Math.round((Date.parse(to) - Date.parse(from)) / 1000);
}

/** Folds every food event as of `now` (`today` decides which days have closed by time). */
export function foldFood(events: readonly MockFoodEvent[], today: string, now: string): FoodFold {
  const ordered = newestFirst(events);
  const meals: FoldedMeal[] = [];
  const seenMeals = new Map<string, { first: string; versions: number }>();
  for (const e of [...ordered].reverse()) {
    if (!e.meal) continue;
    const seen = seenMeals.get(e.meal.id);
    seenMeals.set(e.meal.id, {
      first: seen && seen.first < e.recordedAt ? seen.first : e.recordedAt,
      versions: (seen?.versions ?? 0) + 1,
    });
  }
  const doneMeals = new Set<string>();
  for (const e of ordered) {
    if (!e.meal || doneMeals.has(e.meal.id)) continue;
    doneMeals.add(e.meal.id);
    const m = e.meal;
    if (m.voided) continue;
    const seen = seenMeals.get(m.id)!;
    meals.push({
      id: m.id,
      name: m.name ?? "",
      score: m.score ?? 1,
      calories: m.calories ?? 0,
      proteinG: m.proteinG ?? null,
      carbsG: m.carbsG ?? null,
      fatG: m.fatG ?? null,
      mealType: m.mealType ?? "snack",
      eatenAt: e.occurredAt,
      eatenOn: localDay(e.occurredAt),
      photoArtifactId: m.photo ?? null,
      alcoholic: m.alcoholic === true,
      duringFast: null,
      firstLoggedAt: seen.first,
      loggedAt: e.recordedAt,
      versions: seen.versions,
      eventId: e.id,
    });
  }

  const fasts: FoldedFast[] = [];
  const doneFasts = new Set<string>();
  for (const e of ordered) {
    if (!e.fast || doneFasts.has(e.fast.id)) continue;
    doneFasts.add(e.fast.id);
    const f = e.fast;
    if (f.voided || !f.startedAt) continue;
    const end = f.endedAt ?? null;
    fasts.push({
      id: f.id,
      startedAt: f.startedAt,
      endedAt: end,
      startedOn: localDay(f.startedAt),
      endedOn: end ? localDay(end) : null,
      running: end === null,
      seconds: end ? seconds(f.startedAt, end) : null,
      mealsDuring: meals.filter(
        (m) =>
          m.calories > 0 &&
          Date.parse(m.eatenAt) > Date.parse(f.startedAt!) &&
          Date.parse(m.eatenAt) < Date.parse(end ?? now),
      ).length,
      eventId: e.id,
      recordedAt: e.recordedAt,
    });
  }

  const days = new Map<string, FoodDayFacts>();
  const dayOf = (day: string): FoodDayFacts => {
    let d = days.get(day);
    if (!d) {
      d = emptyDay(day, day < today);
      days.set(day, d);
    }
    return d;
  };
  const later = (a: string | null, b: string) => (a === null || b > a ? b : a);

  for (const m of [...meals].sort((a, b) => a.eatenAt.localeCompare(b.eatenAt) || a.eventId.localeCompare(b.eventId))) {
    const d = dayOf(m.eatenOn);
    d.meals += 1;
    d.calories += m.calories;
    d.scoreCalories += m.score * m.calories;
    d.foodEventIds.push(m.eventId);
    if (m.alcoholic) {
      d.drinks += 1;
      d.drinkEventIds.push(m.eventId);
    }
    d.lastRecordedAt = later(d.lastRecordedAt, m.loggedAt);
  }
  for (const f of fasts) {
    if (!f.endedOn || f.seconds === null) continue;
    const d = dayOf(f.endedOn);
    // Only the longest fast ending on a day counts.
    if (d.longestFastSeconds === null || f.seconds > d.longestFastSeconds) {
      d.longestFastSeconds = f.seconds;
      d.longestFastId = f.id;
      d.fastEventId = f.eventId;
    }
  }
  for (const d of days.values()) {
    const longest = fasts.find((f) => f.id === d.longestFastId);
    if (longest) d.lastRecordedAt = later(d.lastRecordedAt, longest.recordedAt);
  }
  for (const e of [...events].sort((a, b) => a.occurredAt.localeCompare(b.occurredAt))) {
    if (!e.close) continue;
    const d = dayOf(e.close.on);
    if (d.closedByHand) continue;
    d.closedByHand = true;
    d.closedAt = e.occurredAt;
    d.closeEventId = e.id;
    d.closed = true;
    d.lastRecordedAt = later(d.lastRecordedAt, e.occurredAt);
  }

  return { meals, fasts, days: new Map([...days].sort(([a], [b]) => a.localeCompare(b))) };
}

export function emptyDay(day: string, closed: boolean): FoodDayFacts {
  return {
    day,
    meals: 0,
    calories: 0,
    scoreCalories: 0,
    longestFastSeconds: null,
    longestFastId: null,
    fastEventId: null,
    closedByHand: false,
    closedAt: null,
    closeEventId: null,
    closed,
    lastRecordedAt: null,
    foodEventIds: [],
    drinks: 0,
    drinkEventIds: [],
  };
}

export function scoreFacts(facts: FoodDayFacts): DietScore {
  return scoreDay({
    calories: facts.calories,
    scoreCalories: facts.scoreCalories,
    longestFastSeconds: facts.longestFastSeconds,
    drinks: facts.drinks,
  });
}

const round2 = (n: number) => Math.round(n * 100) / 100;

/** A day's summary as `GET /api/food/days` answers it (parts to 2 places). */
export function toSummary(facts: FoodDayFacts, paid: FoodPaid, cap: number): FoodDaySummary {
  const score = scoreFacts(facts);
  return {
    day: facts.day,
    meals: facts.meals,
    calories: facts.calories,
    averageScore: score.averageScore === null ? null : round2(score.averageScore),
    longestFastSeconds: facts.longestFastSeconds,
    closed: facts.closed,
    closedByHand: facts.closedByHand,
    closedAt: facts.closedAt,
    quality: round2(score.quality),
    caloriesPart: round2(score.caloriesPart),
    fasting: round2(score.fasting),
    foodAward: score.foodAward,
    fastAward: score.fastAward,
    total: score.total,
    cap,
    drinks: score.drinks,
    drinkFactor: score.drinkFactor,
    paid,
  };
}

/** A fast or entry without the fold's bookkeeping, as the API answers it. */
export function mealRow({ eventId: _e, ...meal }: FoldedMeal, duringFast: string | null = null): MealRow {
  return { ...meal, duringFast };
}

export function fastRow({ eventId: _e, recordedAt: _r, ...fast }: FoldedFast): FastRow {
  return { ...fast };
}

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "Tue Oct 20" for "2026-10-20" (the kernel's `ddd MMM d`). */
export function noteDay(day: string): string {
  const [y, m, d] = day.split("-").map(Number);
  const date = new Date(y, m - 1, d);
  return `${WEEKDAYS[date.getDay()]} ${MONTHS[m - 1]} ${d}`;
}
