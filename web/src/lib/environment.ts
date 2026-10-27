/**
 * The Environment page's words and the API's shapes (HOME-1..3, D18, environment plan §4.8):
 * the parts' names, the HOME-2 rating examples, how a score, a drag and "rated 3 days ago"
 * read, the walk-through's order, and the live API's numbers (`number | string`) as numbers.
 * The score itself is `lib/home-score.ts`.
 */
import { normalizeEarned } from "@/lib/domains";
import { HOME_PARTS, shownPoints } from "@/lib/home-score";
import type {
  EnvironmentRead,
  FixNextRow,
  HomeDayRow,
  HomeDaysRead,
  HomePart,
  HomePartRow,
  HomeSpaceRow,
  SpaceRatingResult,
  SpaceRatingRow,
  SpaceRatingsRead,
  SpaceRow,
} from "@/lib/production-ui-types";

export { HOME_PARTS };

export const RATING_MIN = 1;
export const RATING_MAX = 5;
/** Ratings move in half points: 1, 1.5 … 5. */
export const RATING_STEP = 0.5;
/** A space's weight: how much it counts. */
export const WEIGHT_MIN = 1;
export const WEIGHT_MAX = 10;
/** A space's pace: how fast its Tidiness and Cleanliness fade. */
export const PACE_MIN = 1;
export const PACE_MAX = 5;
/** What a new space's weight and pace are when left out. */
export const DEFAULT_WEIGHT = 3;
export const DEFAULT_PACE = 3;
export const NAME_MAX = 60;
export const NOTE_MAX = 140;
/** The days the ratings and Home Score reads cover when not asked, and the most they cover. */
export const DEFAULT_DAYS = 90;
export const MAX_DAYS = 731;

/** Whether `value` is a rating: 1 to 5 in half points, as `SpaceLimits.CheckRating`. */
export function isRating(value: number): boolean {
  return Number.isFinite(value) && value >= RATING_MIN && value <= RATING_MAX && Number.isInteger(value / RATING_STEP);
}

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------

/** "Tidiness", as the kernel's refusals name a part. */
export const PART_LABEL: Readonly<Record<HomePart, string>> = {
  tidiness: "Tidiness",
  cleanliness: "Cleanliness",
  aesthetics: "Aesthetics",
};

/** A short word for each whole rating, shown under it in the picker. */
const PART_WORDS: Readonly<Record<HomePart, readonly string[]>> = {
  tidiness: ["piles", "cluttered", "some clutter", "a few things", "put away"],
  cleanliness: ["grimy", "dirty", "dusty", "clean", "spotless"],
  aesthetics: ["broken", "bare", "plain", "pleasant", "inviting"],
};

/** HOME-2's examples for each whole rating (environment plan §2.1), 1 first. */
export const PART_EXAMPLES: Readonly<Record<HomePart, readonly string[]>> = {
  tidiness: [
    "Piles, the space can't be used",
    "Cluttered, surfaces hard to use",
    "Clutter on some surfaces",
    "A few things out",
    "Everything put away, surfaces clear",
  ],
  cleanliness: [
    "Grime or smells",
    "Dirty, floors or surfaces need washing",
    "Visible dust or crumbs, the sink needs doing",
    "Clean, light dust or smudges",
    "Freshly cleaned, spotless",
  ],
  aesthetics: [
    "Broken or ugly fixtures",
    "Bare or mismatched, poor lighting",
    "Functional and plain",
    "Pleasant, a couple of things unfinished",
    "Finished and inviting: decor, colour and light all intentional",
  ],
};

/** The word under a whole rating ("dusty"); undefined for a half. */
export function ratingWord(part: HomePart, rating: number): string | undefined {
  return Number.isInteger(rating) ? PART_WORDS[part][rating - 1] : undefined;
}

/** A rating's accessible name: "3 · dusty", "3.5 · dusty to clean". */
export function ratingLabel(part: HomePart, rating: number): string {
  if (!isRating(rating)) return String(rating);
  const words = PART_WORDS[part];
  const word = Number.isInteger(rating)
    ? words[rating - 1]
    : `${words[Math.floor(rating) - 1]} to ${words[Math.ceil(rating) - 1]}`;
  return `${rating} · ${word}`;
}

/** The line under a part's picker: the chosen rating's example, or how the scale runs. */
export function ratingHelp(part: HomePart, rating: number | null): string {
  const examples = PART_EXAMPLES[part];
  if (rating == null || !isRating(rating)) {
    return `1 ${PART_WORDS[part][0]} · 3 ${PART_WORDS[part][2]} · 5 ${PART_WORDS[part][4]} · halves in between`;
  }
  if (Number.isInteger(rating)) return `${examples[rating - 1]}.`;
  const low = Math.floor(rating);
  return `Between ${low} (${examples[low - 1].toLowerCase()}) and ${low + 1} (${examples[low].toLowerCase()}).`;
}

/** Points as the page shows them, floored to a tenth: "63.6"; "—" when there are none. */
export function shownScore(points: number | null): string {
  return points == null ? "—" : shownPoints(points);
}

/** What a part costs the Home Score: "−6.3". */
export function dragText(drag: number): string {
  return `−${shownPoints(drag)}`;
}

/** A Fix next line: "Office tidiness −6.3". */
export function fixNextText(row: FixNextRow): string {
  return `${row.space} ${row.part} ${dragText(row.drag)}`;
}

/** A walk-through or quick check's toast: "Rated Office", "Rated 9 spaces". */
export function ratedMessage(result: SpaceRatingResult): string {
  const n = result.ratings.length;
  if (n === 1) return `Rated ${result.ratings[0].space}`;
  return `Rated ${n} spaces`;
}

/** Whole days from `day` to `today`. */
export function daysBetween(day: string, today: string): number {
  return Math.round((Date.parse(`${today}T00:00:00Z`) - Date.parse(`${day}T00:00:00Z`)) / 86_400_000);
}

/** "rated today", "rated yesterday", "rated 3 days ago", "not rated yet". */
export function ratedAgo(day: string | null, today: string): string {
  if (!day) return "not rated yet";
  const days = daysBetween(day, today);
  if (days <= 0) return "rated today";
  if (days === 1) return "rated yesterday";
  return `rated ${days} days ago`;
}

/** The Home Score a space's parts cost together. */
export function spaceDrag(space: HomeSpaceRow): number {
  return HOME_PARTS.reduce((sum, p) => sum + space.parts[p].drag, 0);
}

/**
 * The walk-through's order: the longest since rated first (never rated before all), then the
 * biggest drag, then by name.
 */
export function walkThroughOrder(spaces: readonly HomeSpaceRow[], today: string): HomeSpaceRow[] {
  const since = (s: HomeSpaceRow) => (s.lastRatedOn ? daysBetween(s.lastRatedOn, today) : Number.POSITIVE_INFINITY);
  return [...spaces].sort(
    (a, b) => since(b) - since(a) || spaceDrag(b) - spaceDrag(a) || a.name.localeCompare(b.name),
  );
}

// ---------------------------------------------------------------------------
// The API's numbers
// ---------------------------------------------------------------------------

function num(value: unknown): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

function numOrNull(value: unknown): number | null {
  return value === null || value === undefined || value === "" ? null : num(value);
}

function part(value: unknown): HomePart {
  return HOME_PARTS.includes(value as HomePart) ? (value as HomePart) : "tidiness";
}

export function normalizeSpace(raw: SpaceRow): SpaceRow {
  return {
    id: raw.id,
    urn: raw.urn ?? "",
    name: raw.name ?? "",
    weight: num(raw.weight),
    pace: num(raw.pace),
    share: numOrNull(raw.share),
    archivedAt: raw.archivedAt ?? null,
    archivedOn: raw.archivedOn ?? null,
  };
}

export function normalizeSpaceRating(raw: SpaceRatingRow): SpaceRatingRow {
  return {
    id: raw.id,
    spaceId: raw.spaceId,
    space: raw.space ?? "",
    day: raw.day,
    tidiness: numOrNull(raw.tidiness),
    cleanliness: numOrNull(raw.cleanliness),
    aesthetics: numOrNull(raw.aesthetics),
    note: raw.note ?? null,
    ratedAt: raw.ratedAt ?? "",
  };
}

export function normalizeSpaceRatings(raw: SpaceRatingsRead): SpaceRatingsRead {
  return { today: raw.today, from: raw.from, ratings: (raw.ratings ?? []).map(normalizeSpaceRating) };
}

export function normalizeSpaceRatingResult(raw: unknown): SpaceRatingResult {
  const r = (raw ?? {}) as Partial<SpaceRatingResult>;
  return { ratings: (r.ratings ?? []).map(normalizeSpaceRating), ...normalizeEarned(raw) };
}

function normalizePart(raw: Partial<HomePartRow> | null | undefined): HomePartRow {
  return {
    rated: numOrNull(raw?.rated),
    ratedOn: raw?.ratedOn ?? null,
    now: numOrNull(raw?.now),
    drag: num(raw?.drag),
  };
}

export function normalizeHomeSpace(raw: HomeSpaceRow): HomeSpaceRow {
  const parts = (raw.parts ?? {}) as Partial<Record<HomePart, HomePartRow>>;
  return {
    id: raw.id,
    name: raw.name ?? "",
    weight: num(raw.weight),
    pace: num(raw.pace),
    share: numOrNull(raw.share),
    score: numOrNull(raw.score),
    lastRatedOn: raw.lastRatedOn ?? null,
    parts: {
      tidiness: normalizePart(parts.tidiness),
      cleanliness: normalizePart(parts.cleanliness),
      aesthetics: normalizePart(parts.aesthetics),
    },
  };
}

export function normalizeEnvironment(raw: EnvironmentRead): EnvironmentRead {
  return {
    today: raw.today,
    settledThrough: raw.settledThrough ?? raw.today,
    homeScore: numOrNull(raw.homeScore),
    spaces: (raw.spaces ?? []).map(normalizeHomeSpace),
    fixNext: (raw.fixNext ?? []).map((x) => ({
      spaceId: x.spaceId,
      space: x.space ?? "",
      part: part(x.part),
      drag: num(x.drag),
    })),
  };
}

function normalizeHomeDay(raw: HomeDayRow): HomeDayRow {
  return {
    day: raw.day,
    homeScore: numOrNull(raw.homeScore),
    exception: raw.exception ?? null,
    spaces: (raw.spaces ?? []).map((s) => ({ spaceId: s.spaceId, score: numOrNull(s.score) })),
  };
}

export function normalizeHomeDays(raw: HomeDaysRead): HomeDaysRead {
  return {
    today: raw.today,
    settledThrough: raw.settledThrough ?? raw.today,
    from: raw.from,
    spaces: (raw.spaces ?? []).map(normalizeSpace),
    days: (raw.days ?? []).map(normalizeHomeDay),
  };
}
