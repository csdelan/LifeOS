/**
 * A metric's day values, trend and goal date (MEASURE-2, measurements plan §2.2–§2.3): the
 * TypeScript port of the kernel's `LifeOs.Domain.MetricTrend`, for the mock store. It follows
 * the C# step for step, rounding included, and `metric-trend.test.ts` checks the same cases as
 * `MetricTrendTests`, so the mock and the API agree.
 */
import { dayNumber } from "@/lib/dates";
import type { MetricBetter, MetricGoalStatus } from "@/lib/features/measurements/types";

/** How far each day's value pulls the trend after a one-day step (§2.2). */
export const ALPHA = 0.2;

/** The days the goal date's rate is measured over, the newest included (§2.3). */
export const BASIS_DAYS = 28;

/** The furthest a goal date is given, in days; past it the goal reads as far. */
export const HORIZON_DAYS = 730;

/** A reading as the trend needs it: the day and instant it was taken. */
export interface TrendReading {
  id: string;
  value: number;
  /** ISO instant. */
  takenAt: string;
  /** Home-zone day, `yyyy-MM-dd`. */
  takenOn: string;
}

/** One day's value: the first reading of that home-zone day. */
export interface DayValue {
  day: string;
  value: number;
  readingId: string;
}

/** A day's value with the trend on that day. */
export interface TrendPoint extends DayValue {
  trend: number;
}

/** Where a metric stands against its target, as the kernel's `MetricGoal`. */
export interface Goal {
  status: MetricGoalStatus;
  target: number | null;
  direction: MetricBetter | null;
  now: number | null;
  ratePerWeek: number | null;
  reachedBy: string | null;
  basisDays: number;
  basisFrom: string | null;
  basisTo: string | null;
}

function fromDayNumber(n: number): string {
  return new Date(n * 86_400_000).toISOString().slice(0, 10);
}

/**
 * `value` rounded to `places`, half away from zero, as the kernel's
 * `decimal.Round((decimal)value, places, MidpointRounding.AwayFromZero)`: the double is first
 * cut to 15 significant digits, as the conversion to decimal does.
 */
export function roundAway(value: number, places: number): number {
  const kept = Number(value.toPrecision(15));
  const shifted = Math.round(Number(`${Math.abs(kept)}e${places}`));
  const rounded = Number(`${shifted}e-${places}`);
  return kept < 0 ? -rounded : rounded;
}

/** Each day's value: the reading with the earliest time that day (then the lowest id), oldest day first. */
export function dayValues(readings: readonly TrendReading[]): DayValue[] {
  const first = new Map<string, TrendReading>();
  for (const r of readings) {
    const kept = first.get(r.takenOn);
    const earlier =
      !kept ||
      Date.parse(r.takenAt) < Date.parse(kept.takenAt) ||
      (Date.parse(r.takenAt) === Date.parse(kept.takenAt) && r.id < kept.id);
    if (earlier) first.set(r.takenOn, r);
  }
  return [...first.values()]
    .sort((a, b) => a.takenOn.localeCompare(b.takenOn))
    .map((r) => ({ day: r.takenOn, value: r.value, readingId: r.id }));
}

/**
 * The trend on each day with a value (§2.2): the first day's value, then each value pulling it
 * `1 − (1 − α)^g` of the way, g being the days since the previous value. Rounded to
 * `decimals` + 1 places; worked out unrounded.
 */
export function trend(days: readonly DayValue[], decimals: number): TrendPoint[] {
  const points: TrendPoint[] = [];
  let t: number | null = null;
  let previous = 0;
  for (const day of [...days].sort((a, b) => a.day.localeCompare(b.day))) {
    const n = dayNumber(day.day);
    if (t === null) {
      t = day.value;
    } else {
      const pull = 1 - (1 - ALPHA) ** (n - previous);
      t = t + pull * (day.value - t);
    }
    previous = n;
    points.push({ ...day, trend: roundAway(t, decimals + 1) });
  }
  return points;
}

/** The least-squares slope of value against day number, in units a day. */
function slope(points: readonly DayValue[]): number {
  const origin = dayNumber(points[0].day);
  const xs = points.map((p) => dayNumber(p.day) - origin);
  const meanX = xs.reduce((a, b) => a + b, 0) / points.length;
  const meanY = points.reduce((a, p) => a + p.value, 0) / points.length;
  let sxy = 0;
  let sxx = 0;
  points.forEach((p, i) => {
    const dx = xs[i] - meanX;
    sxy += dx * (p.value - meanY);
    sxx += dx * dx;
  });
  return sxx === 0 ? 0 : sxy / sxx;
}

/**
 * Where the metric stands against `target` (§2.3). `better` is the goal's direction; when null,
 * lower while the trend is above the target and higher while it's below.
 */
export function goal(
  days: readonly DayValue[],
  target: number | null,
  better: MetricBetter | null,
  decimals: number,
): Goal {
  if (target === null) {
    return {
      status: "no_target",
      target: null,
      direction: better,
      now: null,
      ratePerWeek: null,
      reachedBy: null,
      basisDays: 0,
      basisFrom: null,
      basisTo: null,
    };
  }

  const ordered = [...days].sort((a, b) => a.day.localeCompare(b.day));
  if (ordered.length < 2) {
    const only = ordered[0] ?? null;
    return {
      status: "too_few",
      target,
      direction: better,
      now: only?.value ?? null,
      ratePerWeek: null,
      reachedBy: null,
      basisDays: ordered.length,
      basisFrom: only?.day ?? null,
      basisTo: only?.day ?? null,
    };
  }

  const newest = dayNumber(ordered[ordered.length - 1].day);
  let basis = ordered.filter((d) => dayNumber(d.day) > newest - BASIS_DAYS);
  if (basis.length < 2) basis = ordered.slice(-2);

  const now = trend(ordered, decimals + 3)[ordered.length - 1].trend;
  const s = slope(basis);
  const direction: MetricBetter = better ?? (target < now ? "lower" : "higher");
  const rate = roundAway(s * 7, decimals + 1);
  const toGo = direction === "lower" ? now - target : target - now;
  const toward = direction === "lower" ? -s : s;

  let status: MetricGoalStatus;
  let reachedBy: string | null = null;
  if (toGo <= 0) {
    status = "reached";
  } else if (toward <= 0 || rate === 0) {
    status = "not_heading";
  } else {
    const daysAway = Math.ceil(toGo / toward);
    if (daysAway > HORIZON_DAYS) {
      status = "far";
    } else {
      status = "heading";
      reachedBy = fromDayNumber(newest + daysAway);
    }
  }

  return {
    status,
    target,
    direction,
    now: roundAway(now, decimals + 1),
    // A flat rate reads as 0, never -0.
    ratePerWeek: rate === 0 ? 0 : rate,
    reachedBy,
    basisDays: basis.length,
    basisFrom: basis[0].day,
    basisTo: basis[basis.length - 1].day,
  };
}
