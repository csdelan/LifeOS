/**
 * The Home Score (HOME-3, D18, environment plan §2.2, §4.4): a port of the kernel's `HomeScore`
 * and `HomeRules` (src/LifeOs.Domain/Home.cs), in whole ten-thousandths of a point, rounding
 * down at every step, so the web and the kernel agree to the digit.
 *
 * - Each part takes its newest rating on or before the day (by day, then by when it was
 *   written); a rating can leave a part out.
 * - A part fades from the second day after it's rated, not counting exception days, and never
 *   below a rating of 2. Pace speeds up or slows the Tidiness and Cleanliness fades.
 * - A space counts once all three parts are rated, until the day it's archived.
 * - The Home Score is the weighted average of the counted spaces' scores.
 */

import type { HomePart } from "@/lib/production-ui-types";

export type { HomePart };

/** The three parts, in the order they're shown. */
export const HOME_PARTS: readonly HomePart[] = ["tidiness", "cleanliness", "aesthetics"];

export const HOME_RULES = {
  /** One point, in ten-thousandths. */
  unit: 10_000,
  /** A perfect score: 100 points. */
  full: 1_000_000,
  /** Half a rating step: 12.5 points. */
  halfStep: 125_000,
  /** The rating a fade never takes a part below. */
  floorRating: 2,
  /** Days after a rating before its fade starts. */
  graceDays: 1,
  /** Pace divides by this, so pace 3 is a normal fade. */
  paceDivisor: 3,
  /** How much each part counts in a space's score, in percent. */
  weights: { tidiness: 45, cleanliness: 35, aesthetics: 20 } as Readonly<Record<HomePart, number>>,
  /** The days a part takes to fade half a point at pace 3. */
  fadeDays: { tidiness: 3, cleanliness: 7, aesthetics: 90 } as Readonly<Record<HomePart, number>>,
} as const;

/** Whether a space's pace speeds up or slows a part's fade: Tidiness and Cleanliness, not Aesthetics. */
export function paced(part: HomePart): boolean {
  return part !== "aesthetics";
}

/** A rating (1–5 in halves) as points: (r − 1) × 25, in ten-thousandths. */
export function ratingPoints(rating: number): number {
  return Math.round(2 * rating - 2) * HOME_RULES.halfStep;
}

/** The floor a fade stops at: a rating of 2, 25 points. */
export const FLOOR_POINTS = ratingPoints(HOME_RULES.floorRating);

/** What `fadingDays` of fade take off a part: ⌊125,000 × f × k / (3 × N)⌋. */
export function fade(part: HomePart, pace: number, fadingDays: number): number {
  if (fadingDays <= 0) return 0;
  const k = paced(part) ? pace : HOME_RULES.paceDivisor;
  return Math.floor((HOME_RULES.halfStep * fadingDays * k) / (HOME_RULES.paceDivisor * HOME_RULES.fadeDays[part]));
}

/** A part rated `rating` after `fadingDays` of fade: max(min(p₀, floor), p₀ − fade). */
export function partValue(part: HomePart, rating: number, pace: number, fadingDays: number): number {
  const p0 = ratingPoints(rating);
  return Math.max(Math.min(p0, FLOOR_POINTS), p0 - fade(part, pace, fadingDays));
}

/** A space's score from its parts' values: ⌊(45 T + 35 C + 20 A) / 100⌋. */
export function spaceScore(tidiness: number, cleanliness: number, aesthetics: number): number {
  const w = HOME_RULES.weights;
  return Math.floor((w.tidiness * tidiness + w.cleanliness * cleanliness + w.aesthetics * aesthetics) / 100);
}

/** The Home Score a part costs: ⌊w × part weight × (1,000,000 − v) / (Σw × 100)⌋. */
export function partDrag(spaceWeight: number, part: HomePart, value: number, totalWeight: number): number {
  if (totalWeight <= 0) return 0;
  return Math.floor((spaceWeight * HOME_RULES.weights[part] * (HOME_RULES.full - value)) / (totalWeight * 100));
}

/** What the score needs of a space: its weight, pace and the day it was archived (null while it isn't). */
export interface HomeSpace {
  id: string;
  weight: number;
  pace: number;
  archivedOn: string | null;
}

/** What the score needs of a rating: the space, the day, any parts, and when it was written. */
export interface HomeRating {
  id: string;
  spaceId: string;
  day: string;
  tidiness: number | null;
  cleanliness: number | null;
  aesthetics: number | null;
  ratedAt: string;
}

/**
 * One part of a space on one day: the rating it was last given and when (null when never), its
 * value now in ten-thousandths, and the Home Score it costs (0 while the space doesn't count).
 */
export interface PartValue {
  part: HomePart;
  rated: number | null;
  ratedOn: string | null;
  value: number | null;
  drag: number;
}

/** One space on one day: its score (null until all three parts are rated) and its parts. */
export interface SpaceDay {
  spaceId: string;
  score: number | null;
  parts: PartValue[];
}

/** The home on one day: the Home Score (null before any space counts) and every space not archived by then. */
export interface HomeDay {
  day: string;
  score: number | null;
  spaces: SpaceDay[];
}

/** Days since the epoch for an ISO day, `YYYY-MM-DD`. */
function dayNumber(iso: string): number {
  const [y, m, d] = iso.split("-").map(Number);
  return Math.round(Date.UTC(y, m - 1, d) / 86_400_000);
}

function isoOf(n: number): string {
  return new Date(n * 86_400_000).toISOString().slice(0, 10);
}

function partOf(rating: HomeRating, part: HomePart): number | null {
  return rating[part];
}

/** Counts a fade's days: those after a rating, less exception days and the grace day. */
function fadingCounter(exceptionDays: Iterable<string>) {
  const days = [...new Set(exceptionDays)].map(dayNumber).sort((a, b) => a - b);
  // The exception days on or before day n.
  const upTo = (n: number) => {
    let low = 0;
    let high = days.length;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (days[mid] <= n) low = mid + 1;
      else high = mid;
    }
    return low;
  };
  return (rated: string, day: string) => {
    const from = dayNumber(rated);
    const to = dayNumber(day);
    const elapsed = to - from;
    if (elapsed <= HOME_RULES.graceDays) return 0;
    return Math.max(0, elapsed - (upTo(to) - upTo(from)) - HOME_RULES.graceDays);
  };
}

/** Oldest first: by day, then by when written, then by id. */
function compareRatings(a: HomeRating, b: HomeRating): number {
  return a.day.localeCompare(b.day) || a.ratedAt.localeCompare(b.ratedAt) || a.id.localeCompare(b.id);
}

/**
 * The home on each day from `from` to `to`, oldest first. `spaces` keep their order in each
 * day; a space archived on or before a day isn't in it.
 */
export function homeDays(
  spaces: readonly HomeSpace[],
  ratings: readonly HomeRating[],
  exceptionDays: Iterable<string>,
  from: string,
  to: string,
): HomeDay[] {
  const result: HomeDay[] = [];
  const first = dayNumber(from);
  const last = dayNumber(to);
  if (last < first) return result;

  const fading = fadingCounter(exceptionDays);
  // Each space's parts: the ratings that rate it, oldest first, read forward as the days advance.
  const tracks = new Map(
    spaces.map((s) => [
      s.id,
      HOME_PARTS.map((part) => ({
        part,
        list: ratings.filter((r) => r.spaceId === s.id && partOf(r, part) != null).sort(compareRatings),
        next: 0,
        current: null as HomeRating | null,
      })),
    ]),
  );

  for (let n = first; n <= last; n++) {
    const day = isoOf(n);
    const present = spaces.filter((s) => s.archivedOn == null || s.archivedOn > day);
    const raw = present.map((space) => ({
      space,
      parts: tracks.get(space.id)!.map((track) => {
        while (track.next < track.list.length && track.list[track.next].day <= day) {
          track.current = track.list[track.next++];
        }
        const rating = track.current;
        const value =
          rating == null ? null : partValue(track.part, partOf(rating, track.part)!, space.pace, fading(rating.day, day));
        return { part: track.part, rating, value };
      }),
    }));

    const counted = raw.filter((r) => r.parts.every((p) => p.value != null));
    const totalWeight = counted.reduce((sum, r) => sum + r.space.weight, 0);
    let weighted = 0;
    const spaceDays: SpaceDay[] = raw.map(({ space, parts }) => {
      const isCounted = parts.every((p) => p.value != null);
      const score = isCounted ? spaceScore(parts[0].value!, parts[1].value!, parts[2].value!) : null;
      if (score != null) weighted += space.weight * score;
      return {
        spaceId: space.id,
        score,
        parts: parts.map((p) => ({
          part: p.part,
          rated: p.rating ? partOf(p.rating, p.part) : null,
          ratedOn: p.rating?.day ?? null,
          value: p.value,
          drag: isCounted ? partDrag(space.weight, p.part, p.value!, totalWeight) : 0,
        })),
      };
    });

    result.push({ day, score: totalWeight > 0 ? Math.floor(weighted / totalWeight) : null, spaces: spaceDays });
  }
  return result;
}

/** The home on one day. */
export function homeOn(
  spaces: readonly HomeSpace[],
  ratings: readonly HomeRating[],
  exceptionDays: Iterable<string>,
  day: string,
): HomeDay {
  return homeDays(spaces, ratings, exceptionDays, day, day)[0];
}

/** Whether a space counts in its day's Home Score: all three parts rated. */
export function counted(space: SpaceDay): boolean {
  return space.score != null;
}

/** The day a space was last rated, any part; null when never. */
export function lastRatedOn(space: SpaceDay): string | null {
  return space.parts.reduce<string | null>((latest, p) => (p.ratedOn && (!latest || p.ratedOn > latest) ? p.ratedOn : latest), null);
}

/** The parts that cost the Home Score most on `day`, biggest first: Fix next. Parts that cost nothing are left out. */
export function fixNext(day: HomeDay, count = 3): { spaceId: string; part: PartValue }[] {
  return day.spaces
    .filter(counted)
    .flatMap((s) => s.parts.map((part) => ({ spaceId: s.spaceId, part })))
    .filter((x) => x.part.drag > 0)
    .sort((a, b) => b.part.drag - a.part.drag)
    .slice(0, count);
}

/** Ten-thousandths of a point as points, exact: 636,805 → 63.6805. */
export function toPoints(units: number): number {
  return units / HOME_RULES.unit;
}

/** Points as whole ten-thousandths: 63.6805 → 636,805. */
export function toUnits(points: number): number {
  return Math.round(points * HOME_RULES.unit);
}

/** Points floored to a tenth, as the page shows them: 63.6805 → "63.6". */
export function shownPoints(points: number): string {
  return (Math.floor(toUnits(points) / 1_000) / 10).toFixed(1);
}
