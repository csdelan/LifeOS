/**
 * The mock store's home (HOME-1..3, D18, environment plan §4.8): the nine spaces of plan §2.1,
 * with about a month of ratings that climb from messy — a walk-through every Sunday, and quick
 * checks of the Office and Kitchen's Tidiness and Cleanliness on the Wednesdays between — all
 * relative to today and the same on every load. It answers as `/api/environment` does, with
 * the kernel's refusals word for word, and scores the home with `lib/home-score.ts`, the
 * kernel's own sums. Environment's level reads the daily scores through `mockHomeScoreUnits`,
 * and the main store's `mockHomeWrite` answers a rating write's level change and rank-up (E3).
 * Exception days come from the main store, passed in, so this half doesn't import it back.
 */
import { addDays, nowIso, startOfWeekSunday, todayIso } from "@/lib/dates";
import {
  DEFAULT_DAYS,
  DEFAULT_PACE,
  DEFAULT_WEIGHT,
  HOME_PARTS,
  MAX_DAYS,
  NAME_MAX,
  NOTE_MAX,
  PACE_MAX,
  PACE_MIN,
  PART_LABEL,
  WEIGHT_MAX,
  WEIGHT_MIN,
  isRating,
} from "@/lib/environment";
import { fixNext, homeDays, lastRatedOn, toPoints, type HomeDay } from "@/lib/home-score";
import type {
  EnvironmentRead,
  HomeDaysRead,
  HomePart,
  HomePartRow,
  HomeSpaceRow,
  SpaceRatingEditRequest,
  SpaceRatingRequest,
  SpaceRatingResult,
  SpaceRatingRow,
  SpaceRatingsRead,
  SpaceRequest,
  SpaceRow,
} from "@/lib/production-ui-types";

/** A marked exception day, as the main store keeps it. */
export interface MockException {
  day: string;
  comment: string;
}

interface MockSpace {
  id: string;
  urn: string;
  name: string;
  weight: number;
  pace: number;
  archivedAt: string | null;
  archivedOn: string | null;
}

type MockRating = Omit<SpaceRatingRow, "space">;

interface EnvironmentState {
  spaces: MockSpace[];
  ratings: MockRating[];
  nextId: number;
}

let seeded: EnvironmentState | null = null;

/** Seeded on first use, so "today" is the day it's first read. */
function store(): EnvironmentState {
  return (seeded ??= seedEnvironment());
}

export function resetEnvironmentStore() {
  seeded = null;
}

/** Test seam: no spaces and no ratings, as on the first day. */
export function clearMockEnvironment() {
  const state = store();
  state.spaces = [];
  state.ratings = [];
}

// ---------------------------------------------------------------------------
// The seed
// ---------------------------------------------------------------------------

type Triple = readonly [number, number, number];

/**
 * Plan §2.1's spaces, each rated messy at the first walk-through and better at each one after,
 * up to the last; Aesthetics barely moves.
 */
const SEED: readonly { name: string; weight: number; pace: number; from: Triple; to: Triple }[] = [
  { name: "Office", weight: 5, pace: 5, from: [2, 2.5, 3], to: [4.5, 4, 3.5] },
  { name: "Kitchen", weight: 4, pace: 5, from: [2, 2, 3], to: [4, 4, 3.5] },
  { name: "Master Bath", weight: 4, pace: 3, from: [2.5, 2, 3], to: [4, 4, 3.5] },
  { name: "Living Area", weight: 3, pace: 4, from: [2.5, 3, 3.5], to: [4, 4, 4] },
  { name: "Stairway/Foyer/Halls", weight: 3, pace: 4, from: [3, 3, 3], to: [4, 3.5, 3] },
  { name: "Other Baths", weight: 3, pace: 2, from: [3, 2.5, 3], to: [3.5, 3.5, 3] },
  { name: "Bedroom", weight: 2, pace: 1, from: [2.5, 3, 3.5], to: [4, 4, 4] },
  { name: "Exercise Room", weight: 2, pace: 1, from: [2, 2.5, 2.5], to: [3.5, 3.5, 3] },
  { name: "Garage", weight: 1, pace: 2, from: [1.5, 2, 2], to: [2.5, 2.5, 2] },
];

/** The Sunday walk-throughs the seed holds, the last on or before today. */
export const SEEDED_WALK_THROUGHS = 5;

/** The nearest half point. */
function half(value: number): number {
  return Math.round(value * 2) / 2;
}

function slug(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function hex(n: number, width: number): string {
  return n.toString(16).padStart(width, "0");
}

/** A uuid-shaped id, the same on every load. */
function mockId(kind: number, n: number): string {
  return `00000000-0000-4000-8${hex(kind, 3)}-${hex(n, 12)}`;
}

function seedEnvironment(): EnvironmentState {
  const today = todayIso();
  const lastSunday = startOfWeekSunday(today);
  const sundays = Array.from({ length: SEEDED_WALK_THROUGHS }, (_, i) =>
    addDays(lastSunday, -7 * (SEEDED_WALK_THROUGHS - 1 - i)),
  );
  // Each walk-through rates every space a step further from messy, the last at its best.
  const at = (s: (typeof SEED)[number], p: number, step: number) =>
    half(s.from[p] + ((s.to[p] - s.from[p]) * step) / (SEEDED_WALK_THROUGHS - 1));

  const spaces: MockSpace[] = SEED.map((s, i) => ({
    id: mockId(1, i + 1),
    urn: `urn:bsk:space:${slug(s.name)}-${hex(0xa10 + i, 6)}`,
    name: s.name,
    weight: s.weight,
    pace: s.pace,
    archivedAt: null,
    archivedOn: null,
  }));

  const ratings: MockRating[] = [];
  let n = 0;
  const rate = (space: MockSpace, day: string, parts: readonly (number | null)[], note: string | null, hour: number) => {
    ratings.push({
      id: mockId(2, ++n),
      spaceId: space.id,
      day,
      tidiness: parts[0],
      cleanliness: parts[1],
      aesthetics: parts[2],
      note,
      ratedAt: `${day}T${String(hour).padStart(2, "0")}:${String(n % 60).padStart(2, "0")}:00.000Z`,
    });
  };

  sundays.forEach((sunday, w) => {
    SEED.forEach((s, i) => {
      // The garage is skipped on the last walk-through, so it reads as rated a week ago.
      if (s.name === "Garage" && w === SEEDED_WALK_THROUGHS - 1) return;
      const note = w === 0 && i === 0 ? "Papers everywhere; started a donate box." : null;
      rate(spaces[i], sunday, [at(s, 0, w), at(s, 1, w), at(s, 2, w)], note, 16);
    });
    // The Wednesday after, once it's come: a quick check of the two busiest, halfway to the next.
    const wednesday = addDays(sunday, 3);
    if (wednesday <= today && w < SEEDED_WALK_THROUGHS - 1) {
      for (const i of [0, 1]) rate(spaces[i], wednesday, [at(SEED[i], 0, w + 0.5), at(SEED[i], 1, w + 0.5), null], null, 18);
    }
  });

  return { spaces, ratings, nextId: n + 1 };
}

// ---------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------

function byName(a: { name: string }, b: { name: string }): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
}

function liveWeight(): number {
  return store()
    .spaces.filter((s) => s.archivedAt == null)
    .reduce((sum, s) => sum + s.weight, 0);
}

/** Its weight's share of the spaces not archived, to one place, halves away from zero. */
function share(space: MockSpace, total: number): number | null {
  if (space.archivedAt != null || total === 0) return null;
  return Math.round((1000 * space.weight) / total) / 10;
}

function spaceRow(space: MockSpace, total = liveWeight()): SpaceRow {
  return {
    id: space.id,
    urn: space.urn,
    name: space.name,
    weight: space.weight,
    pace: space.pace,
    share: share(space, total),
    archivedAt: space.archivedAt,
    archivedOn: space.archivedOn,
  };
}

function ratingRow(rating: MockRating): SpaceRatingRow {
  const space = store().spaces.find((s) => s.id === rating.spaceId);
  return { ...rating, space: space?.name ?? "" };
}

function checkDays(days: number) {
  if (!Number.isInteger(days) || days < 1 || days > MAX_DAYS) {
    throw new Error(`Ask for 1 to ${MAX_DAYS} days; got ${days}.`);
  }
}

/** The home from `from` through `to`, as the kernel scores it. */
function score(from: string, to: string, exceptions: readonly MockException[]): HomeDay[] {
  const state = store();
  return homeDays(state.spaces, state.ratings, exceptions.map((e) => e.day), from, to);
}

/**
 * Each day's Home Score in ten-thousandths, from the first rating through `today`, as
 * Environment's fold reads them (`DomainService.HomeScoresFor`): empty before the first rating.
 */
export function mockHomeScoreUnits(exceptionDays: readonly string[], today: string = todayIso()): Map<string, number> {
  const state = store();
  const scores = new Map<string, number>();
  if (state.ratings.length === 0) return scores;
  const first = state.ratings.reduce((min, r) => (r.day < min ? r.day : min), state.ratings[0].day);
  for (const d of homeDays(state.spaces, state.ratings, exceptionDays, first, today)) {
    if (d.score != null) scores.set(d.day, d.score);
  }
  return scores;
}

/** Every space, archived ones included, by name (`GET /api/environment/spaces`). */
export function mockSpaces(): SpaceRow[] {
  const total = liveWeight();
  return [...store().spaces].sort(byName).map((s) => spaceRow(s, total));
}

/** The ratings among the last `days`, of one space when given, newest first (`GET /api/environment/ratings`). */
export function mockSpaceRatings(opts: { space?: string; days?: number } = {}): SpaceRatingsRead {
  const days = opts.days ?? DEFAULT_DAYS;
  checkDays(days);
  const today = todayIso();
  const from = addDays(today, 1 - days);
  const spaceId = opts.space ? resolveSpace(opts.space).id : null;
  const ratings = store()
    .ratings.filter((r) => r.day >= from && (spaceId == null || r.spaceId === spaceId))
    .sort((a, b) => b.day.localeCompare(a.day) || b.ratedAt.localeCompare(a.ratedAt))
    .map(ratingRow);
  return { today, from, ratings };
}

function partRow(part: { rated: number | null; ratedOn: string | null; value: number | null; drag: number }): HomePartRow {
  return {
    rated: part.rated,
    ratedOn: part.ratedOn,
    now: part.value == null ? null : toPoints(part.value),
    drag: toPoints(part.drag),
  };
}

/** The home today: the Home Score, each space not archived with its parts, and Fix next (`GET /api/environment`). */
export function mockEnvironment(exceptions: readonly MockException[] = []): EnvironmentRead {
  const today = todayIso();
  const [day] = score(today, today, exceptions);
  const total = liveWeight();
  const spaces = store()
    .spaces.filter((s) => s.archivedAt == null)
    .sort(byName);
  const names = new Map(spaces.map((s) => [s.id, s.name]));
  return {
    today,
    settledThrough: addDays(today, -1),
    homeScore: day.score == null ? null : toPoints(day.score),
    spaces: spaces.map((space): HomeSpaceRow => {
      const scored = day.spaces.find((s) => s.spaceId === space.id);
      const parts = Object.fromEntries(
        HOME_PARTS.map((p) => {
          const found = scored?.parts.find((x) => x.part === p);
          return [p, partRow(found ?? { rated: null, ratedOn: null, value: null, drag: 0 })];
        }),
      ) as Record<HomePart, HomePartRow>;
      return {
        id: space.id,
        name: space.name,
        weight: space.weight,
        pace: space.pace,
        share: share(space, total),
        score: scored?.score == null ? null : toPoints(scored.score),
        lastRatedOn: scored ? lastRatedOn(scored) : null,
        parts,
      };
    }),
    fixNext: fixNext(day).map((x) => ({
      spaceId: x.spaceId,
      space: names.get(x.spaceId) ?? "",
      part: x.part.part,
      drag: toPoints(x.part.drag),
    })),
  };
}

/** The Home Score on each of the last `days` days, oldest first (`GET /api/environment/days`). */
export function mockHomeDays(days = DEFAULT_DAYS, exceptions: readonly MockException[] = []): HomeDaysRead {
  checkDays(days);
  const today = todayIso();
  const from = addDays(today, 1 - days);
  const marked = new Map(exceptions.map((e) => [e.day, e.comment]));
  const total = liveWeight();
  return {
    today,
    settledThrough: addDays(today, -1),
    from,
    spaces: [...store().spaces].sort(byName).map((s) => spaceRow(s, total)),
    days: score(from, today, exceptions).map((d) => ({
      day: d.day,
      homeScore: d.score == null ? null : toPoints(d.score),
      exception: marked.get(d.day) ?? null,
      spaces: d.spaces.map((s) => ({ spaceId: s.spaceId, score: s.score == null ? null : toPoints(s.score) })),
    })),
  };
}

// ---------------------------------------------------------------------------
// Spaces
// ---------------------------------------------------------------------------

/** Names match in any case. */
function same(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

/**
 * The space `reference` names: its id, its urn, its urn's tail, or its name in any case — a
 * space not archived first. As `SpaceService.ResolveAsync`.
 */
function resolveSpace(reference: string, preferArchived = false): MockSpace {
  const wanted = reference.trim();
  if (!wanted) throw new Error("Say which space, by its name.");
  const all = store().spaces;
  const exact = all.find(
    (s) =>
      s.id === wanted.toLowerCase() ||
      s.urn.toLowerCase() === wanted.toLowerCase() ||
      s.urn.toLowerCase().endsWith(`-${wanted.toLowerCase()}`),
  );
  if (exact) return exact;
  const named = all
    .filter((s) => same(s.name, wanted))
    .sort((a, b) => Number((a.archivedAt != null) !== preferArchived) - Number((b.archivedAt != null) !== preferArchived));
  if (named.length === 0) throw new Error(`No subject matches '${reference}'.`);
  return named[0];
}

function checkName(name: string | undefined): string {
  const text = name?.trim() ?? "";
  if (!text) throw new Error("Give the space a name, e.g. Office.");
  if (/[\r\n]/.test(text)) throw new Error("A space's name is one line: take out the line break.");
  if (text.length > NAME_MAX) throw new Error(`A space's name is up to ${NAME_MAX} characters; that one is ${text.length}.`);
  return text;
}

function checkWeight(weight: number): number {
  if (!Number.isInteger(weight) || weight < WEIGHT_MIN || weight > WEIGHT_MAX) {
    throw new Error(`A space's weight is a whole number from ${WEIGHT_MIN} to ${WEIGHT_MAX}; got ${weight}.`);
  }
  return weight;
}

function checkPace(pace: number): number {
  if (!Number.isInteger(pace) || pace < PACE_MIN || pace > PACE_MAX) {
    throw new Error(
      `A space's pace is a whole number from ${PACE_MIN} (slow to get messy) to ${PACE_MAX} (fast); got ${pace}.`,
    );
  }
  return pace;
}

function checkFree(name: string, except: string | null) {
  const taken = store().spaces.find((s) => s.archivedAt == null && s.id !== except && same(s.name, name));
  if (taken) throw new Error(`You already have a space called '${taken.name}'.`);
}

/** Adds a space; weight and pace are 3 when left out (`POST /api/environment/spaces`). */
export function mockAddSpace(req: SpaceRequest): SpaceRow {
  const name = checkName(req.name);
  const weight = checkWeight(req.weight ?? DEFAULT_WEIGHT);
  const pace = checkPace(req.pace ?? DEFAULT_PACE);
  checkFree(name, null);
  const state = store();
  const n = state.nextId++;
  const space: MockSpace = {
    id: mockId(1, 0x100 + n),
    urn: `urn:bsk:space:${slug(name) || "space"}-${hex(0xb00 + n, 6)}`,
    name,
    weight,
    pace,
    archivedAt: null,
    archivedOn: null,
  };
  state.spaces.push(space);
  return spaceRow(space);
}

/** Renames, reweights or re-paces a space (`POST /api/environment/spaces/{id}`). */
export function mockSetSpace(reference: string, req: SpaceRequest): SpaceRow {
  if (req.name === undefined && req.weight === undefined && req.pace === undefined) {
    throw new Error("Say what to change: a name, a weight or a pace.");
  }
  const space = resolveSpace(reference);
  const name = req.name === undefined ? null : checkName(req.name);
  const weight = req.weight === undefined ? null : checkWeight(req.weight);
  const pace = req.pace === undefined ? null : checkPace(req.pace);
  if (name != null && name !== space.name) checkFree(name, space.id);
  if (name != null) space.name = name;
  if (weight != null) space.weight = weight;
  if (pace != null) space.pace = pace;
  return spaceRow(space);
}

/**
 * Archives or restores a space when `subject` names one (the mock's `archive` and `restore`):
 * it leaves the Home Score from today, and counts again for all of history once restored.
 * Answers false when `subject` isn't a space, for the main store to handle.
 */
export function mockArchiveSpace(subject: string, archived: boolean): boolean {
  const wanted = subject.trim().toLowerCase();
  const space = store().spaces.find((s) => s.id === wanted || s.urn.toLowerCase() === wanted);
  if (!space) return false;
  if (archived && space.archivedAt == null) {
    space.archivedAt = nowIso();
    space.archivedOn = todayIso();
  } else if (!archived && space.archivedAt != null) {
    const taken = store().spaces.find((s) => s.archivedAt == null && same(s.name, space.name));
    if (taken) throw new Error(`You already have a space called '${space.name}': rename one of them first.`);
    space.archivedAt = null;
    space.archivedOn = null;
  }
  return true;
}

// ---------------------------------------------------------------------------
// Ratings
// ---------------------------------------------------------------------------

function checkRating(value: number | undefined | null, part: HomePart): number | null {
  if (value == null) return null;
  if (!isRating(value)) throw new Error(`${PART_LABEL[part]} is rated 1 to 5 in half points (1, 1.5 … 5); got ${value}.`);
  return value;
}

function checkNote(note: string | undefined | null): string | null {
  const text = note?.trim() ?? "";
  if (!text) return null;
  if (/[\r\n]/.test(text)) throw new Error("A rating's note is one line: take out the line break.");
  if (text.length > NOTE_MAX) throw new Error(`A rating's note is up to ${NOTE_MAX} characters; that one is ${text.length}.`);
  return text;
}

function checkDay(day: string, today: string): string {
  if (day > today) throw new Error(`A rating is for today or earlier; ${day} hasn't happened yet (today is ${today}).`);
  return day;
}

/** A part's last rating on or before `on`: what "same as last time" repeats. */
function lastOf(spaceId: string, part: HomePart, on: string): number | null {
  const last = store()
    .ratings.filter((r) => r.spaceId === spaceId && r.day <= on && r[part] != null)
    .sort((a, b) => a.day.localeCompare(b.day) || a.ratedAt.localeCompare(b.ratedAt))
    .at(-1);
  return last ? last[part] : null;
}

/**
 * Rates one or more spaces on one day, today when none is given (`POST /api/environment/ratings`).
 * Refused as a whole, with nothing written, as `SpaceRatingService.RateAsync` refuses.
 */
export function mockRateSpaces(inputs: readonly SpaceRatingRequest[], day?: string): SpaceRatingResult {
  if (inputs.length === 0) throw new Error("Rate at least one space.");
  const today = todayIso();
  const on = checkDay(day ?? today, today);

  const planned: MockRating[] = [];
  const state = store();
  let next = state.nextId;
  for (const input of inputs) {
    const space = resolveSpace(input.space);
    if (space.archivedAt != null) throw new Error(`'${space.name}' is archived: bring it back before rating it.`);
    if (planned.some((p) => p.spaceId === space.id)) throw new Error(`'${space.name}' is rated twice: rate each space once.`);

    let parts: (number | null)[];
    if (input.same) {
      if (input.tidiness != null || input.cleanliness != null || input.aesthetics != null) {
        throw new Error(`'Same as last time' repeats ${space.name}'s last rating, so it takes no new ones: leave them out.`);
      }
      parts = HOME_PARTS.map((p) => lastOf(space.id, p, on));
      if (parts.every((p) => p == null)) {
        throw new Error(`'${space.name}' hasn't been rated by ${on}, so there's nothing to repeat: rate it instead.`);
      }
    } else {
      parts = HOME_PARTS.map((p) => checkRating(input[p], p));
      if (parts.every((p) => p == null)) {
        throw new Error(`Rate at least one part of '${space.name}': tidiness, cleanliness or aesthetics.`);
      }
    }

    planned.push({
      id: mockId(2, next++),
      spaceId: space.id,
      day: on,
      tidiness: parts[0],
      cleanliness: parts[1],
      aesthetics: parts[2],
      note: checkNote(input.note),
      ratedAt: nowIso(),
    });
  }

  state.nextId = next;
  state.ratings.push(...planned);
  return { ratings: planned.map(ratingRow), awards: [], progress: [] };
}

function resolveRating(id: string): MockRating {
  const found = store().ratings.find((r) => r.id === id.trim().toLowerCase());
  if (!found) throw new Error(`No rating has the id ${id} (it may have been deleted).`);
  return found;
}

/** Changes a rating's day, parts or note (`POST /api/environment/ratings/{id}`); changing nothing writes nothing. */
export function mockEditSpaceRating(id: string, req: SpaceRatingEditRequest): SpaceRatingResult {
  const rating = resolveRating(id);
  const today = todayIso();
  const day = checkDay(req.day ?? rating.day, today);
  const parts = HOME_PARTS.map((p) => checkRating(req[p], p) ?? rating[p]);
  const note = req.note === undefined ? rating.note : checkNote(req.note);
  const unchanged =
    day === rating.day && HOME_PARTS.every((p, i) => parts[i] === rating[p]) && note === rating.note;
  if (!unchanged) {
    rating.day = day;
    [rating.tidiness, rating.cleanliness, rating.aesthetics] = parts;
    rating.note = note;
    rating.ratedAt = nowIso();
  }
  return { ratings: [ratingRow(rating)], awards: [], progress: [] };
}

/** Deletes a rating (`POST /api/environment/ratings/{id}/delete`). */
export function mockDeleteSpaceRating(id: string): SpaceRatingResult {
  const rating = resolveRating(id);
  const state = store();
  state.ratings = state.ratings.filter((r) => r !== rating);
  return { ratings: [], awards: [], progress: [] };
}
