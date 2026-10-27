/**
 * The mock store's home (HOME-1..3, D18, environment plan §4.8): the nine spaces of plan §2.1,
 * with about a month of ratings that climb from messy — a walk-through every Sunday, and quick
 * checks of the Office and Kitchen's Tidiness and Cleanliness on the Wednesdays between — all
 * relative to today and the same on every load. It answers as `/api/environment` does, with
 * the kernel's refusals word for word, and scores the home with `lib/home-score.ts`, the
 * kernel's own sums. Environment's level reads the daily scores through `mockHomeScoreUnits`,
 * and the main store's `mockHomeWrite` answers a rating write's level change and rank-up (E3).
 * Exception days come from the main store, passed in, so this half doesn't import it back.
 *
 * Photos (HOME-4, environment plan §8): the Office and the Kitchen hold placeholder photos — a
 * drawn room whose clutter thins as its Tidiness climbs — from the first Sunday and the last,
 * the Kitchen with a second shot, Counters, photographed once. A photo's bytes live here too,
 * so `artifactBytesUrl` asks `mockSpacePhotoUrl` for a space photo's.
 */
import { objectUrlFor } from "@/lib/artifacts";
import { addDays, formatDay, nowIso, startOfWeekSunday, todayIso } from "@/lib/dates";
import {
  DEFAULT_DAYS,
  DEFAULT_PACE,
  DEFAULT_WEIGHT,
  HOME_PARTS,
  MAIN_SHOT,
  MAX_DAYS,
  NAME_MAX,
  NOTE_MAX,
  PACE_MAX,
  PACE_MIN,
  PART_LABEL,
  SHOTS_MAX,
  SHOT_NAME_MAX,
  WEIGHT_MAX,
  WEIGHT_MIN,
  isRating,
} from "@/lib/environment";
import { fixNext, homeDays, lastRatedOn, toPoints, type HomeDay } from "@/lib/home-score";
import { PHOTO_MAX_BYTES } from "@/lib/photo";
import type {
  EnvironmentRead,
  HomeDaysRead,
  HomePart,
  HomePartRow,
  HomeSpaceRow,
  SpacePhotoChangeRequest,
  SpacePhotoRequest,
  SpacePhotoResult,
  SpacePhotoRow,
  SpacePhotosRead,
  SpaceRatingEditRequest,
  SpaceRatingRequest,
  SpaceRatingResult,
  SpaceRatingRow,
  SpaceRatingsRead,
  SpaceRequest,
  SpaceRow,
  SpaceShotRow,
} from "@/lib/production-ui-types";

/** A marked exception day, as the main store keeps it. */
export interface MockException {
  day: string;
  comment: string;
}

/** A shot as the space's `shots` attribute holds it: a name only once it differs from the space's. */
interface MockShot {
  id: string;
  name: string | null;
  baseline: string | null;
}

interface MockSpace {
  id: string;
  urn: string;
  name: string;
  weight: number;
  pace: number;
  archivedAt: string | null;
  archivedOn: string | null;
  /** Null until a shot is added: the one shot, `main`, named after the space. */
  shots: MockShot[] | null;
}

type MockRating = Omit<SpaceRatingRow, "space">;

/** A photo as `v_space_photo` reads it: its newest version; a removed one is gone. */
interface MockPhoto {
  id: string;
  spaceId: string;
  shotId: string;
  day: string;
  artifactId: string;
  addedAt: string;
}

interface EnvironmentState {
  spaces: MockSpace[];
  ratings: MockRating[];
  photos: MockPhoto[];
  /** Every photo's bytes by artifact id, removed photos' too: the record keeps them. */
  photoUrls: Map<string, string>;
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

/** Test seam: no spaces, ratings or photos, as on the first day. */
export function clearMockEnvironment() {
  const state = store();
  state.spaces = [];
  state.ratings = [];
  state.photos = [];
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
    shots: null,
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

  const { photos, photoUrls } = seedPhotos(spaces, sundays, at);
  return { spaces, ratings, photos, photoUrls, nextId: n + 1 };
}

/**
 * A placeholder photo of a room: a wall, a window, a floor, a table, and clutter that thins as
 * `tidy` (0 to 1) rises, with the shot and day written in the corner. The same room for the
 * same space and shot, so Then and Now differ only in the mess.
 */
export function placeholderPhoto(space: string, shot: string, day: string, tidy: number): string {
  let seed = [...`${space}|${shot}`].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7);
  const random = () => {
    seed = (seed * 1_103_515_245 + 12_345) >>> 0;
    return seed / 0x1_0000_0000;
  };
  const hue = Math.floor(random() * 360);
  const boxes = Array.from({ length: 14 }, () => ({
    x: 30 + random() * 330,
    y: 160 + random() * 120,
    w: 14 + random() * 34,
    h: 10 + random() * 22,
    hue: Math.floor(random() * 360),
  }));
  const clutter = Math.round((1 - Math.min(1, Math.max(0, tidy))) * boxes.length);
  const mess = boxes
    .slice(0, clutter)
    .map(
      (b) =>
        `<rect x='${b.x.toFixed(0)}' y='${(b.y - b.h).toFixed(0)}' width='${b.w.toFixed(0)}' height='${b.h.toFixed(0)}' rx='3' fill='hsl(${b.hue} 55% 55%)'/>`,
    )
    .join("");
  const label = `${shot === space ? space : `${space} · ${shot}`} · ${formatDay(day)}`.replace(/[<>&'"]/g, "");
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg' width='400' height='300' viewBox='0 0 400 300'>` +
    `<rect width='400' height='300' fill='hsl(${hue} 30% 86%)'/>` +
    `<rect y='190' width='400' height='110' fill='hsl(${hue} 18% 62%)'/>` +
    `<rect x='250' y='40' width='110' height='80' rx='4' fill='hsl(${(hue + 180) % 360} 40% 78%)' stroke='white' stroke-width='6'/>` +
    `<rect x='60' y='140' width='200' height='12' rx='2' fill='hsl(30 35% 35%)'/>` +
    `<rect x='72' y='152' width='10' height='70' fill='hsl(30 35% 30%)'/>` +
    `<rect x='238' y='152' width='10' height='70' fill='hsl(30 35% 30%)'/>` +
    mess +
    `<rect x='8' y='8' width='${Math.min(384, 18 + label.length * 8)}' height='26' rx='4' fill='rgb(0 0 0 / 0.55)'/>` +
    `<text x='16' y='26' font-family='sans-serif' font-size='14' fill='white'>${label}</text>` +
    `</svg>`;
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

/** The Kitchen's second shot, a fixed id so the seed reads the same on every load. */
const COUNTERS_SHOT = "c0a7e125";

/** The Office and Kitchen's placeholder photos (see the top of this file). */
function seedPhotos(
  spaces: MockSpace[],
  sundays: readonly string[],
  at: (s: (typeof SEED)[number], p: number, step: number) => number,
): { photos: MockPhoto[]; photoUrls: Map<string, string> } {
  const photos: MockPhoto[] = [];
  const photoUrls = new Map<string, string>();
  let n = 0;
  const take = (i: number, shotId: string, shot: string, step: number) => {
    const space = spaces[i];
    const day = sundays[step];
    // As tidy as Tidiness was rated that Sunday: a 1 is all clutter, a 5 none.
    const tidy = (at(SEED[i], 0, step) - 1) / 4;
    n += 1;
    const artifactId = mockId(4, n);
    photoUrls.set(artifactId, placeholderPhoto(space.name, shot, day, tidy));
    photos.push({
      id: mockId(3, n),
      spaceId: space.id,
      shotId,
      day,
      artifactId,
      addedAt: `${day}T16:${String(n).padStart(2, "0")}:00.000Z`,
    });
  };
  const last = SEEDED_WALK_THROUGHS - 1;
  take(0, MAIN_SHOT, "Office", 0);
  take(0, MAIN_SHOT, "Office", last);
  take(1, MAIN_SHOT, "Kitchen", 0);
  take(1, MAIN_SHOT, "Kitchen", 2);
  take(1, MAIN_SHOT, "Kitchen", last);
  spaces[1].shots = [
    { id: MAIN_SHOT, name: null, baseline: null },
    { id: COUNTERS_SHOT, name: "Counters", baseline: null },
  ];
  take(1, COUNTERS_SHOT, "Counters", last);
  return { photos, photoUrls };
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
  const on = photoDays(exceptions);
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
        shots: shotRows(space, on),
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
    shots: null,
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

// ---------------------------------------------------------------------------
// Photos (HOME-4)
// ---------------------------------------------------------------------------

/** A space photo's bytes, as `artifactBytesUrl` serves them; null for any other artifact. */
export function mockSpacePhotoUrl(artifactId: string): string | null {
  return store().photoUrls.get(artifactId) ?? null;
}

function shotsOf(space: MockSpace): MockShot[] {
  return space.shots ?? [{ id: MAIN_SHOT, name: null, baseline: null }];
}

function shotName(space: MockSpace, shot: MockShot): string {
  return shot.name ?? space.name;
}

/** A space's photos, oldest first: by day, then when added. */
function photosOf(spaceId: string): MockPhoto[] {
  return store()
    .photos.filter((p) => p.spaceId === spaceId)
    .sort((a, b) => a.day.localeCompare(b.day) || a.addedAt.localeCompare(b.addedAt) || a.id.localeCompare(b.id));
}

function inShot(shot: MockShot, photo: MockPhoto): boolean {
  return same(shot.id, photo.shotId);
}

/** The shot's baseline: the picked photo while it's still the shot's, else its first. */
function baselineOf(shot: MockShot, photos: readonly MockPhoto[]): MockPhoto | undefined {
  const own = photos.filter((p) => inShot(shot, p));
  return own.find((p) => p.id === shot.baseline) ?? own[0];
}

/** A photo's day as its space scored then: the score and each part. */
type PhotoDays = (photo: MockPhoto) => { score: number | null; parts: Record<HomePart, HomePartRow> };

/** Scores every day from the first photo through today once, for each photo's day to read. */
function photoDays(exceptions: readonly MockException[]): PhotoDays {
  const state = store();
  const days = new Map<string, HomeDay>();
  if (state.photos.length > 0) {
    const first = state.photos.reduce((min, p) => (p.day < min ? p.day : min), state.photos[0].day);
    for (const d of score(first, todayIso(), exceptions)) days.set(d.day, d);
  }
  return (photo) => {
    const scored = days.get(photo.day)?.spaces.find((s) => s.spaceId === photo.spaceId);
    const parts = Object.fromEntries(
      HOME_PARTS.map((p) => {
        const found = scored?.parts.find((x) => x.part === p);
        return [p, partRow(found ?? { rated: null, ratedOn: null, value: null, drag: 0 })];
      }),
    ) as Record<HomePart, HomePartRow>;
    return { score: scored?.score == null ? null : toPoints(scored.score), parts };
  };
}

function photoRow(photo: MockPhoto, space: MockSpace, baseline: boolean, on: PhotoDays): SpacePhotoRow {
  const shot = shotsOf(space).find((s) => inShot(s, photo));
  return {
    id: photo.id,
    spaceId: space.id,
    space: space.name,
    shotId: photo.shotId,
    shot: shot ? shotName(space, shot) : null,
    day: photo.day,
    artifactId: photo.artifactId,
    addedAt: photo.addedAt,
    baseline,
    ...on(photo),
  };
}

/** Each shot with Then (its baseline) and Now (its newest photo). */
function shotRows(space: MockSpace, on: PhotoDays): SpaceShotRow[] {
  const photos = photosOf(space.id);
  return shotsOf(space).map((shot) => {
    const own = photos.filter((p) => inShot(shot, p));
    const baseline = baselineOf(shot, photos);
    const newest = own.at(-1);
    return {
      id: shot.id,
      name: shotName(space, shot),
      photos: own.length,
      baseline: baseline ? photoRow(baseline, space, true, on) : null,
      newest: newest ? photoRow(newest, space, newest.id === baseline?.id, on) : null,
    };
  });
}

/** The shot `reference` names: the first when blank, else by id, then by name (`SpaceService.ResolveShot`). */
function resolveShot(space: MockSpace, reference: string | undefined): MockShot {
  const shots = shotsOf(space);
  const wanted = reference?.trim() ?? "";
  if (!wanted) return shots[0];
  const found = shots.find((s) => same(s.id, wanted)) ?? shots.find((s) => same(shotName(space, s), wanted));
  if (!found) {
    const names = shots.map((s) => shotName(space, s)).join(", ");
    throw new Error(`${space.name} has no shot called '${wanted}'. Its shots: ${names}.`);
  }
  return found;
}

function resolvePhoto(id: string): MockPhoto {
  const photo = store().photos.find((p) => p.id === id.trim().toLowerCase());
  if (!photo) throw new Error(`No photo has the id ${id} (it may have been removed).`);
  return photo;
}

function spaceOf(photo: MockPhoto): MockSpace {
  const space = store().spaces.find((s) => s.id === photo.spaceId);
  if (!space) throw new Error("That photo's space no longer exists.");
  return space;
}

function checkPhotoDay(day: string, today: string): string {
  if (day > today) throw new Error(`A photo is for today or earlier; ${day} hasn't happened yet (today is ${today}).`);
  return day;
}

/** `PhotoChecks`: the mock can only go by the blob's type, where the kernel reads its first bytes. */
function checkPhotoBytes(photo: Blob) {
  if (photo.size === 0) throw new Error("That photo is empty.");
  if (photo.size > PHOTO_MAX_BYTES) {
    throw new Error(`That photo is ${(photo.size / (1024 * 1024)).toFixed(1)} MB; a photo can be at most 5 MB.`);
  }
  if (!["image/jpeg", "image/png", "image/webp"].includes(photo.type)) {
    throw new Error("That isn't a photo: send a JPEG, PNG or WebP image.");
  }
}

function photoResult(space: MockSpace, photo: MockPhoto | null, exceptions: readonly MockException[]): SpacePhotoResult {
  const on = photoDays(exceptions);
  const shots = shotRows(space, on);
  const live = photo ? store().photos.find((p) => p.id === photo.id) : undefined;
  return {
    photo: live ? photoRow(live, space, shots.some((s) => s.baseline?.id === live.id), on) : null,
    spaceId: space.id,
    shots,
  };
}

/** A space's photos, newest first, of one shot when given; a removed shot's left out (`GET …/spaces/{id}/photos`). */
export function mockSpacePhotos(
  reference: string,
  shot?: string,
  exceptions: readonly MockException[] = [],
): SpacePhotosRead {
  const space = resolveSpace(reference);
  const wanted = shot?.trim() ? [resolveShot(space, shot)] : shotsOf(space);
  const on = photoDays(exceptions);
  const all = photosOf(space.id);
  const baselines = new Set(shotsOf(space).map((s) => baselineOf(s, all)?.id));
  return {
    spaceId: space.id,
    space: space.name,
    shots: shotRows(space, on),
    photos: all
      .filter((p) => wanted.some((s) => inShot(s, p)))
      .reverse()
      .map((p) => photoRow(p, space, baselines.has(p.id), on)),
  };
}

/** Adds a photo of a space: today and the first shot unless said (`POST …/spaces/{id}/photos`). */
export function mockAddSpacePhoto(
  reference: string,
  photo: Blob,
  req: SpacePhotoRequest = {},
  exceptions: readonly MockException[] = [],
): SpacePhotoResult {
  const space = resolveSpace(reference);
  if (space.archivedAt != null) throw new Error(`'${space.name}' is archived: bring it back before adding photos.`);
  const shot = resolveShot(space, req.shot);
  const today = todayIso();
  const day = checkPhotoDay(req.day?.trim() || today, today);
  checkPhotoBytes(photo);
  const state = store();
  const n = state.nextId++;
  const added: MockPhoto = {
    id: mockId(3, 0x1000 + n),
    spaceId: space.id,
    shotId: shot.id,
    day,
    artifactId: mockId(4, 0x1000 + n),
    addedAt: nowIso(),
  };
  state.photoUrls.set(added.artifactId, objectUrlFor(photo));
  state.photos.push(added);
  return photoResult(space, added, exceptions);
}

/** Moves a photo to another day or shot (`POST /api/environment/photos/{id}`). */
export function mockChangeSpacePhoto(
  id: string,
  req: SpacePhotoChangeRequest,
  exceptions: readonly MockException[] = [],
): SpacePhotoResult {
  if (!req.day?.trim() && !req.shot?.trim()) throw new Error("Say what to change: a day or a shot.");
  const photo = resolvePhoto(id);
  const space = spaceOf(photo);
  const day = checkPhotoDay(req.day?.trim() || photo.day, todayIso());
  const shotId = req.shot?.trim() ? resolveShot(space, req.shot).id : photo.shotId;
  photo.day = day;
  photo.shotId = shotId;
  return photoResult(space, photo, exceptions);
}

/** Removes a photo; its bytes stay in the record (`POST /api/environment/photos/{id}/delete`). */
export function mockRemoveSpacePhoto(id: string, exceptions: readonly MockException[] = []): SpacePhotoResult {
  const photo = resolvePhoto(id);
  const space = spaceOf(photo);
  const state = store();
  state.photos = state.photos.filter((p) => p.id !== photo.id);
  return photoResult(space, null, exceptions);
}

/** Makes a photo its shot's baseline (`POST /api/environment/photos/{id}/baseline`). */
export function mockSetSpaceBaseline(id: string, exceptions: readonly MockException[] = []): SpacePhotoResult {
  const photo = resolvePhoto(id);
  const space = spaceOf(photo);
  const shots = shotsOf(space);
  const shot = shots.find((s) => inShot(s, photo));
  if (!shot) throw new Error(`That photo's shot was removed from ${space.name}: move it to one of its shots first.`);
  space.shots = shots.map((s) => (s.id === shot.id ? { ...s, baseline: photo.id } : s));
  return photoResult(space, photo, exceptions);
}

function liveSpaceForShots(reference: string): MockSpace {
  const space = resolveSpace(reference);
  if (space.archivedAt != null) throw new Error(`'${space.name}' is archived: bring it back before changing its shots.`);
  return space;
}

function checkShotName(name: string): string {
  const text = name.trim();
  if (!text) throw new Error("Give the shot a name, e.g. Desk.");
  if (/[\r\n]/.test(text)) throw new Error("A shot's name is one line: take out the line break.");
  if (text.length > SHOT_NAME_MAX) {
    throw new Error(`A shot's name is up to ${SHOT_NAME_MAX} characters; that one is ${text.length}.`);
  }
  return text;
}

function checkShotFree(space: MockSpace, name: string, except: string | null) {
  const taken = shotsOf(space).find((s) => s.id !== except && same(shotName(space, s), name));
  if (taken) throw new Error(`${space.name} already has a shot called '${shotName(space, taken)}'.`);
}

/** Adds a shot to a space (`POST /api/environment/spaces/{id}/shots`). */
export function mockAddShot(reference: string, name: string, exceptions: readonly MockException[] = []): SpacePhotoResult {
  const space = liveSpaceForShots(reference);
  const shots = shotsOf(space);
  const title = checkShotName(name);
  if (shots.length >= SHOTS_MAX) {
    throw new Error(`'${space.name}' has ${SHOTS_MAX} shots, the most a space can have: remove one first.`);
  }
  checkShotFree(space, title, null);
  space.shots = [...shots, { id: hex(0x5c000000 + store().nextId++, 8), name: title, baseline: null }];
  return photoResult(space, null, exceptions);
}

/** Renames a shot (`POST /api/environment/spaces/{id}/shots/{shot}`). */
export function mockRenameShot(
  reference: string,
  shot: string,
  name: string,
  exceptions: readonly MockException[] = [],
): SpacePhotoResult {
  const space = liveSpaceForShots(reference);
  const target = resolveShot(space, shot);
  const title = checkShotName(name);
  checkShotFree(space, title, target.id);
  space.shots = shotsOf(space).map((s) => (s.id === target.id ? { ...s, name: title } : s));
  return photoResult(space, null, exceptions);
}

/** Removes a shot; its photos stay in the record (`POST …/shots/{shot}/delete`). */
export function mockRemoveShot(reference: string, shot: string, exceptions: readonly MockException[] = []): SpacePhotoResult {
  const space = liveSpaceForShots(reference);
  const shots = shotsOf(space);
  const target = resolveShot(space, shot);
  if (shots.length === 1) {
    throw new Error(`'${shotName(space, target)}' is ${space.name}'s only shot: a space keeps at least one. Rename it instead.`);
  }
  space.shots = shots.filter((s) => s.id !== target.id);
  return photoResult(space, null, exceptions);
}
