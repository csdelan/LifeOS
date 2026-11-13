import { addDays, nowIso, todayIso, startOfWeekMonday } from "@/lib/dates";
import {
  CALORIES_MAX,
  DIET_CAP,
  DIET_RULES,
  DRINK_SCORE,
  formatDuration,
  GRAMS_MAX,
  guessMealType,
  isScore,
  MEAL_TYPES,
  NAME_MAX,
  SCORE_MAX,
  SCORE_MIN,
} from "@/lib/diet-score";
import {
  emptyDay,
  fastRow,
  foldFood,
  mealRow,
  noteDay,
  scoreFacts,
  toSummary,
  type FoldedFast,
  type FoldedMeal,
  type FoodFold,
  type MockFoodEvent,
} from "@/lib/mock/food-fold";
import type {
  Amounts,
  Award,
  Earned,
  FastActionRequest,
  FoodAction,
  FoodAwardPaid,
  FoodDayRead,
  FoodDaysRead,
  FoodDaySummary,
  FoodEntryRequest,
  FoodMealType,
  FoodPaid,
  FoodWriteResult,
  MealRow,
  SobrietyRow,
} from "@/lib/production-ui-types";
import { localDay } from "@/lib/mock/domain-credit";
import { sobrietyOn } from "@/lib/sobriety";
import type { MockLedgerEntry } from "@/lib/mock/seed";
import {
  getState,
  emit,
  newId,
  putArtifact,
  earning,
  priceDomains,
} from "@/lib/mock/store";

// ---------------------------------------------------------------------------
// Food Log (FOOD-1..7, food-log plan §3 and §7): FoodLogService, FoodReads and FoodRewards.
// Entries, fasts and closes are versioned events (`lib/mock/food-fold.ts`). Every write settles
// every food day with the ledger: `fast:<day>` and `diet:<day>` pay base Health XP at the day's
// multiplier, and a paid day whose events changed is re-priced (given back, then earned again).
// Drinks cut both: the food award at the close, the fast once the day closes.
// The mock's domain settle leaves food alone, so the seeded Health history reads as the
// Character page's tests expect; the live `POST /api/domains/settle` settles food first.
// ---------------------------------------------------------------------------

const FOOD_SKEW_MS = 60_000;
const FOOD_EARLIEST = Date.parse("2000-01-01T00:00:00Z");
const FOOD_MAX_RANGE_DAYS = 62;
const FOOD_MAX_RECENT = 100;
const FAST_TRIGGER = "fast";
const DIET_TRIGGER = "diet";
const FOOD_KEY = /^(fast|diet):(\d{4}-\d{2}-\d{2})$/;

function foodFold(): FoodFold {
  return foldFood(getState().foodEvents, todayIso(), nowIso());
}

/** A time as the kernel keeps it: to the second, not in the future, not before 2000. */
function foodTime(at: string | undefined | null, now: number, what: string): string {
  const ms = at === undefined || at === null ? now : Date.parse(at);
  if (Number.isNaN(ms)) throw new Error(`${what} needs a time; got '${at}'.`);
  const second = Math.floor(ms / 1000) * 1000;
  if (second > now + FOOD_SKEW_MS) throw new Error(`${what} can't be in the future.`);
  if (second < FOOD_EARLIEST) throw new Error(`${what} can't be before the year 2000.`);
  return new Date(second).toISOString();
}

/** "8:00 pm". */
function foodClock(iso: string): string {
  const d = new Date(iso);
  const h = d.getHours();
  return `${h % 12 === 0 ? 12 : h % 12}:${String(d.getMinutes()).padStart(2, "0")} ${h < 12 ? "am" : "pm"}`;
}

/** "Mon Oct 19, 8:00 pm". */
function foodWhen(iso: string): string {
  return `${noteDay(localDay(iso))}, ${foodClock(iso)}`;
}

function fastSpanText(f: FoldedFast): string {
  return f.endedAt
    ? `from ${foodWhen(f.startedAt)} to ${foodWhen(f.endedAt)}`
    : `from ${foodWhen(f.startedAt)} (still running)`;
}

interface NormalizedFood {
  name: string;
  score: number;
  calories: number;
  eatenAt: string;
  eatenOn: string;
  mealType: FoodMealType;
  proteinG: number | null;
  carbsG: number | null;
  fatG: number | null;
  alcoholic: boolean;
}

function checkGrams(grams: number | null | undefined, what: string): number | null {
  if (grams === undefined || grams === null) return null;
  if (!Number.isInteger(grams) || grams < 0 || grams > GRAMS_MAX) {
    throw new Error(`${what} is whole grams from 0 to ${GRAMS_MAX.toLocaleString("en-US")}; got ${grams}.`);
  }
  return grams;
}

/** `FoodLogService.NormalizeAsync`: the checks, refused in words with nothing written. */
function normalizeFood(req: FoodEntryRequest, now: number): NormalizedFood {
  const name = (req.name ?? "").trim();
  if (!name) throw new Error("Say what it was: a name is required.");
  if (name.length > NAME_MAX) throw new Error(`Keep the name to ${NAME_MAX} characters.`);
  // A drink always scores 1, whatever was sent (FOOD-6).
  const alcoholic = req.alcoholic === true;
  const score = alcoholic ? DRINK_SCORE : req.score;
  if (!isScore(score)) {
    throw new Error(
      `The health score is ${SCORE_MIN} to ${SCORE_MAX} in whole or half points, like 3 or 3.5 (1 = junk, 5 = very healthy); got ${score}.`,
    );
  }
  if (!Number.isInteger(req.calories) || req.calories < 0 || req.calories > CALORIES_MAX) {
    throw new Error(`Calories are a whole number from 0 to ${CALORIES_MAX.toLocaleString("en-US")}; got ${req.calories}.`);
  }
  const proteinG = checkGrams(req.proteinG, "Protein");
  const carbsG = checkGrams(req.carbsG, "Carbs");
  const fatG = checkGrams(req.fatG, "Fat");
  const wanted = req.mealType?.trim().toLowerCase();
  if (wanted && !MEAL_TYPES.includes(wanted as FoodMealType)) {
    throw new Error(`Unknown meal type '${req.mealType}'. Expected one of: ${MEAL_TYPES.join(", ")}.`);
  }
  const eatenAt = foodTime(req.eatenAt, now, "A food entry");
  return {
    name,
    score,
    calories: req.calories,
    eatenAt,
    eatenOn: localDay(eatenAt),
    mealType: (wanted as FoodMealType | undefined) || (alcoholic ? "drink" : guessMealType(new Date(eatenAt))),
    proteinG,
    carbsG,
    fatG,
    alcoholic,
  };
}

function mealPayload(id: string, entry: NormalizedFood, photo: string | null): MockFoodEvent["meal"] {
  return {
    id,
    name: entry.name,
    score: entry.score,
    calories: entry.calories,
    proteinG: entry.proteinG,
    carbsG: entry.carbsG,
    fatG: entry.fatG,
    mealType: entry.mealType,
    photo,
    ...(entry.alcoholic ? { alcoholic: true } : {}),
  };
}

function appendFood(...events: Omit<MockFoodEvent, "id">[]): string[] {
  const written = events.map((e) => ({ ...e, id: newId() }));
  getState().foodEvents = [...getState().foodEvents, ...written];
  return written.map((e) => e.id);
}

function foodMeal(fold: FoodFold, ref: string): FoldedMeal {
  const meal = fold.meals.find((m) => m.id === ref);
  if (!meal) throw new Error(`No food entry has the id ${ref} (it may have been deleted).`);
  return meal;
}

function foodFast(fold: FoodFold, ref: string): FoldedFast {
  const fast = fold.fasts.find((f) => f.id === ref);
  if (!fast) throw new Error(`No fast has the id ${ref} (it may have been deleted).`);
  return fast;
}

/** Two live fasts can't overlap; `to` null runs to now. */
function refuseOverlap(fold: FoodFold, from: string, to: string | null, except?: string) {
  const now = Date.now();
  const start = Date.parse(from);
  const end = to ? Date.parse(to) : now;
  const overlap = fold.fasts
    .filter((f) => f.id !== except)
    .sort((a, b) => a.startedAt.localeCompare(b.startedAt))
    .find((f) => Date.parse(f.startedAt) < end && start < (f.endedAt ? Date.parse(f.endedAt) : now));
  if (overlap) throw new Error(`That overlaps your fast ${fastSpanText(overlap)}. Change or delete that fast first.`);
}

/** A write's answer, after settling every food day (food-log plan §3.4). */
function foodResult(
  action: FoodAction,
  find: { meal?: string; fast?: string; asWas?: FoldedMeal | FoldedFast },
  day: string | null,
  days: (string | null | undefined)[],
  cause: string | undefined,
): FoodWriteResult {
  const touched = [...new Set(days.filter((d): d is string => !!d))].sort();
  const earned = settleFoodDays(cause, touched);
  const fold = foodFold();
  const meal = find.meal ? (fold.meals.find((m) => m.id === find.meal) ?? find.asWas) : undefined;
  const fast = find.fast ? (fold.fasts.find((f) => f.id === find.fast) ?? find.asWas) : undefined;
  emit();
  return {
    action,
    meal: meal && "eatenAt" in meal ? mealRow(meal) : null,
    fast: fast && "startedAt" in fast ? fastRow(fast) : null,
    day,
    days: touched,
    ...earned,
  };
}

/**
 * `POST /api/food/meals`: log an entry; with `endFast`, end the running fast at its time, and
 * with `photoArtifactId`, show that stored photo (Log again) without storing it again.
 */
export function mockLogFood(req: FoodEntryRequest): FoodWriteResult {
  const now = Date.now();
  const entry = normalizeFood(req, now);
  const photo = req.photoArtifactId ?? null;
  if (photo !== null) {
    const stored = getState().artifacts[photo];
    if (!stored?.hasBytes || !PHOTO_TYPES.includes(stored.contentType)) {
      throw new Error("That photo isn't stored here: pick the photo again, or log the entry without one.");
    }
  }
  const recordedAt = new Date(now).toISOString();
  const mealId = newId();
  const events: Omit<MockFoodEvent, "id">[] = [
    { occurredAt: entry.eatenAt, recordedAt, meal: mealPayload(mealId, entry, photo) },
  ];
  let ending: FoldedFast | undefined;
  if (req.endFast) {
    if (entry.calories === 0) throw new Error("A 0-calorie entry doesn't end a fast; leave the fast running.");
    ending = foodFold().fasts.find((f) => f.running);
    if (!ending) throw new Error("No fast is running, so there is none to end.");
    if (Date.parse(entry.eatenAt) <= Date.parse(ending.startedAt)) {
      throw new Error(`That's before your fast began (${foodWhen(ending.startedAt)}), so it can't end it.`);
    }
    events.push({
      occurredAt: entry.eatenAt,
      recordedAt,
      fast: { id: ending.id, startedAt: ending.startedAt, endedAt: entry.eatenAt },
    });
  }
  const [cause] = appendFood(...events);
  return foodResult("logged", { meal: mealId, fast: ending?.id }, null, [entry.eatenOn], cause);
}

/**
 * `POST /api/food/meals/{id}`: replace an entry as a whole. Its time and meal type stay as
 * they were when left out; the photo is kept.
 */
export function mockEditFood(ref: string, req: FoodEntryRequest): FoodWriteResult {
  const now = Date.now();
  const meal = foodMeal(foodFold(), ref);
  const entry = normalizeFood(
    {
      ...req,
      eatenAt: req.eatenAt ?? meal.eatenAt,
      mealType: req.mealType ?? meal.mealType,
      alcoholic: req.alcoholic ?? meal.alcoholic,
    },
    now,
  );
  const [cause] = appendFood({
    occurredAt: entry.eatenAt,
    recordedAt: new Date(now).toISOString(),
    meal: mealPayload(meal.id, entry, meal.photoArtifactId),
  });
  return foodResult("edited", { meal: meal.id }, null, [meal.eatenOn, entry.eatenOn], cause);
}

/** `POST /api/food/meals/{id}/delete`: a voided version, so the record keeps what it was. */
export function mockDeleteFood(ref: string): FoodWriteResult {
  const now = new Date().toISOString();
  const meal = foodMeal(foodFold(), ref);
  const [cause] = appendFood({ occurredAt: now, recordedAt: now, meal: { id: meal.id, voided: true } });
  return foodResult("deleted", { meal: meal.id, asWas: meal }, null, [meal.eatenOn], cause);
}

const PHOTO_TYPES = ["image/jpeg", "image/png", "image/webp"];
const PHOTO_MAX_BYTES = 5 * 1024 * 1024;

/** A new version of an entry, the same in every other way, pointing at `photo`. */
function mealWithPhoto(meal: FoldedMeal, photo: string | null): Omit<MockFoodEvent, "id"> {
  return {
    occurredAt: meal.eatenAt,
    recordedAt: new Date().toISOString(),
    meal: {
      id: meal.id,
      name: meal.name,
      score: meal.score,
      calories: meal.calories,
      proteinG: meal.proteinG,
      carbsG: meal.carbsG,
      fatG: meal.fatG,
      mealType: meal.mealType,
      photo,
      ...(meal.alcoholic ? { alcoholic: true } : {}),
    },
  };
}

/**
 * `POST /api/food/meals/{id}/photo` (FOOD-5): keep the photo as an artifact (an object URL,
 * as the live client shows one) and point the entry at it. The kernel reads the type from the
 * bytes; the mock trusts the blob's.
 */
export function mockSetFoodPhoto(ref: string, photo: Blob, filename = "photo.jpg"): FoodWriteResult {
  const meal = foodMeal(foodFold(), ref);
  if (photo.size === 0) throw new Error("That photo is empty.");
  if (photo.size > PHOTO_MAX_BYTES) {
    throw new Error(`That photo is ${(photo.size / (1024 * 1024)).toFixed(1)} MB; a photo can be at most 5 MB.`);
  }
  if (!PHOTO_TYPES.includes(photo.type)) throw new Error("That isn't a photo: send a JPEG, PNG or WebP image.");
  const artifact = putArtifact({
    eventId: newId(),
    eventKind: "observation",
    blob: photo,
    filename,
    contentType: photo.type,
  });
  const [cause] = appendFood(mealWithPhoto(meal, artifact.id));
  return foodResult("photo_set", { meal: meal.id }, null, [meal.eatenOn], cause);
}

/** `POST /api/food/meals/{id}/photo/delete`: a version without the photo; the bytes stay. */
export function mockRemoveFoodPhoto(ref: string): FoodWriteResult {
  const meal = foodMeal(foodFold(), ref);
  if (!meal.photoArtifactId) throw new Error(`${meal.name} has no photo to remove.`);
  const [cause] = appendFood(mealWithPhoto(meal, null));
  return foodResult("photo_removed", { meal: meal.id }, null, [meal.eatenOn], cause);
}

function fastSpan(fromRaw: string, toRaw: string, now: number): { from: string; to: string } {
  const from = foodTime(fromRaw, now, "A fast");
  const to = foodTime(toRaw, now, "A fast's end");
  if (Date.parse(to) <= Date.parse(from)) throw new Error("A fast ends after it starts.");
  return { from, to };
}

/** `POST /api/food/fasts`: begin, end, record, edit or delete a fast. */
export function mockFastAction(req: FastActionRequest): FoodWriteResult {
  const now = Date.now();
  const recordedAt = new Date(now).toISOString();
  const fold = foodFold();
  switch (req.action) {
    case "begin": {
      const start = foodTime(req.at, now, "A fast");
      const running = fold.fasts.find((f) => f.running);
      if (running) throw new Error(`A fast is already running (since ${foodWhen(running.startedAt)}). End it first.`);
      refuseOverlap(fold, start, null);
      const id = newId();
      const [cause] = appendFood({ occurredAt: start, recordedAt, fast: { id, startedAt: start, endedAt: null } });
      return foodResult("fast_begun", { fast: id }, null, [], cause);
    }
    case "end": {
      const running = fold.fasts.find((f) => f.running);
      if (!running) throw new Error("No fast is running, so there is none to end.");
      const end = foodTime(req.at, now, "A fast's end");
      if (Date.parse(end) <= Date.parse(running.startedAt)) {
        throw new Error(`A fast ends after it starts; this one began ${foodWhen(running.startedAt)}.`);
      }
      refuseOverlap(fold, running.startedAt, end, running.id);
      const [cause] = appendFood({
        occurredAt: end,
        recordedAt,
        fast: { id: running.id, startedAt: running.startedAt, endedAt: end },
      });
      return foodResult("fast_ended", { fast: running.id }, null, [localDay(end)], cause);
    }
    case "record": {
      const { from, to } = fastSpan(req.from, req.to, now);
      refuseOverlap(fold, from, to);
      const id = newId();
      const [cause] = appendFood({ occurredAt: to, recordedAt, fast: { id, startedAt: from, endedAt: to } });
      return foodResult("fast_recorded", { fast: id }, null, [localDay(to)], cause);
    }
    case "edit": {
      const fast = foodFast(fold, req.fast);
      let from: string;
      let to: string | null;
      if (req.to === undefined || req.to === null) {
        if (!fast.running) throw new Error("Give the fast an end time: it has already ended.");
        from = foodTime(req.from, now, "A fast");
        to = null;
      } else {
        ({ from, to } = fastSpan(req.from, req.to, now));
      }
      refuseOverlap(fold, from, to, fast.id);
      const [cause] = appendFood({
        occurredAt: to ?? from,
        recordedAt,
        fast: { id: fast.id, startedAt: from, endedAt: to },
      });
      return foodResult("fast_edited", { fast: fast.id }, null, [fast.endedOn, to ? localDay(to) : null], cause);
    }
    case "delete": {
      const fast = foodFast(fold, req.fast);
      const [cause] = appendFood({ occurredAt: recordedAt, recordedAt, fast: { id: fast.id, voided: true } });
      return foodResult("fast_deleted", { fast: fast.id, asWas: fast }, null, [fast.endedOn], cause);
    }
  }
}

/**
 * `POST /api/food/close`: close a day (today by default) so its food pays. A day already
 * closed, by hand or because it's over, writes nothing but still settles.
 */
export function mockCloseFoodDay(day?: string): FoodWriteResult {
  const today = todayIso();
  const target = day ?? today;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(target)) throw new Error(`'${target}' isn't a date (yyyy-MM-dd).`);
  if (target > today) throw new Error(`${target} hasn't happened yet, so it can't be closed.`);
  if (target < today || foodFold().days.get(target)?.closedByHand) {
    return foodResult("already_closed", {}, target, [target], undefined);
  }
  const now = new Date().toISOString();
  const [cause] = appendFood({ occurredAt: now, recordedAt: now, close: { on: target } });
  return foodResult("closed", {}, target, [target], cause);
}

/** `POST /api/food/settle`: settle every food day, which the Food Log does when it opens. */
export function mockSettleFood(): Earned {
  const earned = settleFoodDays();
  if (earned.awards.length > 0) emit();
  return earned;
}

/** A food award a day could pay: its base (0 included), when it became due, what it read. */
export interface DueFood {
  key: string;
  trigger: string;
  day: string;
  base: number;
  triggeredAt: string;
  sources: string[];
  note: string;
}

function foodNote(trigger: string, day: string): string {
  return `${trigger === FAST_TRIGGER ? "Fast ending" : "Food day"} ${noteDay(day)}`;
}

/** `FoodRewards.CandidatesAsync`: a fast award per day a fast ended, a food award per closed day with entries. */
function foodCandidates(fold: FoodFold): DueFood[] {
  const today = todayIso();
  const due: DueFood[] = [];
  for (const facts of fold.days.values()) {
    if (facts.day > today) continue;
    const score = scoreFacts(facts);
    const fast = fold.fasts.find((f) => f.id === facts.longestFastId);
    if (fast?.endedAt && facts.fastEventId) {
      // The fast pays in full when it ends; once the day closes, the drinks cut it, and a day
      // with any drinks cites them (and its close), so a retune leaves a paid fast alone.
      const cut = facts.closed && facts.drinks > 0;
      due.push({
        key: `${FAST_TRIGGER}:${facts.day}`,
        trigger: FAST_TRIGGER,
        day: facts.day,
        base: facts.closed ? score.fastAward : score.fastAwardUncut,
        triggeredAt: fast.endedAt,
        sources: cut
          ? [facts.fastEventId, ...facts.drinkEventIds, ...(facts.closeEventId ? [facts.closeEventId] : [])]
          : [facts.fastEventId],
        note: `${foodNote(FAST_TRIGGER, facts.day)} (${formatDuration(fast.seconds ?? 0)})`,
      });
    }
    const closedAt = facts.closedByHand ? facts.closedAt : facts.lastRecordedAt;
    if (facts.closed && facts.meals > 0 && closedAt) {
      due.push({
        key: `${DIET_TRIGGER}:${facts.day}`,
        trigger: DIET_TRIGGER,
        day: facts.day,
        base: score.foodAward,
        triggeredAt: closedAt,
        sources: facts.closeEventId ? [...facts.foodEventIds, facts.closeEventId] : [...facts.foodEventIds],
        note: foodNote(DIET_TRIGGER, facts.day),
      });
    }
  }
  return due;
}

/** A food key as the ledger holds it: its net, and its latest earn. */
export interface HeldFood {
  key: string;
  net: Amounts;
  paid: boolean;
  latestEarn: MockLedgerEntry | null;
}

function heldFood(): Map<string, HeldFood> {
  return heldKeyed(FOOD_KEY);
}

/** The keyed awards on the ledger whose keys match `pattern`: each one's net and latest earn. */
export function heldKeyed(pattern: RegExp): Map<string, HeldFood> {
  const held = new Map<string, HeldFood>();
  for (const e of getState().ledger) {
    if (!e.awardKey || !pattern.test(e.awardKey) || (e.entry !== "earn" && e.entry !== "reversal")) continue;
    const h = held.get(e.awardKey) ?? { key: e.awardKey, net: {}, paid: false, latestEarn: null };
    for (const [k, n] of Object.entries(e.amounts)) h.net[k] = (h.net[k] ?? 0) + n;
    if (e.entry === "earn" && (!h.latestEarn || e.occurredAt >= h.latestEarn.occurredAt)) h.latestEarn = e;
    held.set(e.awardKey, h);
  }
  for (const h of held.values()) h.paid = Object.values(h.net).some((n) => n !== 0);
  return held;
}

export function latestBase(held: HeldFood): number {
  const earn = held.latestEarn;
  return earn?.base?.[DIET_RULES.currency] ?? earn?.amounts[DIET_RULES.currency] ?? 0;
}

function sameSources(a: readonly string[], b: readonly string[]): boolean {
  const set = new Set(a);
  return set.size === new Set(b).size && b.every((id) => set.has(id));
}

/**
 * `FoodRewards.SettleAsync`. Before the wallet has ever opened, a write that makes one of the
 * days it touched pay opens it, dated at that award's trigger; a settle with no cause never does.
 */
function settleFoodDays(cause?: string, touched: string[] = []): Earned {
  const fold = foodFold();
  let open = getState().ledger.find((e) => e.entry === "open");
  if (!open) {
    if (!cause || touched.length === 0) return { awards: [], progress: [] };
    const first = foodCandidates(fold)
      .filter((c) => c.base > 0 && touched.includes(c.day))
      .map((c) => c.triggeredAt)
      .sort()[0];
    if (!first) return { awards: [], progress: [] };
    open = { id: newId(), occurredAt: first, entry: "open", provenance: "declared", amounts: {} };
    getState().ledger = [...getState().ledger, open];
  }
  const openedAt = Date.parse(open.occurredAt);
  return earning(() => planFood(fold, openedAt));
}

/**
 * `FoodRewards.PlanAsync`: for each key that could pay or is paid — paid and still reading the
 * events it was scored from, or paid at the base due now: nothing; paid otherwise: a reversal
 * of what it pays, then an earn at the base due (if any); unpaid with a base due: an earn.
 */
function planFood(fold: FoodFold, openedAt: number): Award[] {
  return planKeyed(foodCandidates(fold), heldFood(), openedAt, (key) => {
    const [, trigger, day] = FOOD_KEY.exec(key)!;
    return { trigger, note: foodNote(trigger, day) };
  });
}

/**
 * The plan `FoodRewards` and `ActivityRewards` share (D13): for each key that could pay or is
 * paid, the entries that bring the ledger in line. `reversalOf` names a key's trigger and note.
 * A base is due from the wallet's opening: food by its trigger's instant, activity (given
 * `openedOn`, the Oura day the wallet opened on) by its day.
 */
export function planKeyed(
  due: DueFood[],
  held: Map<string, HeldFood>,
  openedAt: number,
  reversalOf: (key: string) => { trigger: string; note: string },
  openedOn?: string,
): Award[] {
  const candidates = new Map(due.map((c) => [c.key, c]));
  const paidKeys = [...held.values()].filter((h) => h.paid).map((h) => h.key);
  const keys = [...new Set([...candidates.keys(), ...paidKeys])].sort();
  const now = Date.now();
  const awards: Award[] = [];
  for (const key of keys) {
    const candidate = candidates.get(key);
    const paid = held.get(key);
    const fromOpening = (c: DueFood) => (openedOn ? c.day >= openedOn : Date.parse(c.triggeredAt) >= openedAt);
    const due = candidate && candidate.base > 0 && fromOpening(candidate) ? candidate : undefined;
    let earnAt = now;
    if (paid?.paid) {
      if (candidate && sameSources(candidate.sources, paid.latestEarn?.sources ?? [])) continue;
      if (due && latestBase(paid) === due.base) continue;
      // The new earn follows the reversal, so the ledger reads them in that order.
      awards.push(keyedReversal(paid, now, reversalOf(paid.key)));
      earnAt = now + 1;
    }
    if (due) awards.push(keyedEarn(due, paid, earnAt, openedAt));
  }
  return awards;
}

/**
 * An earn at the credited day's multiplier, dated at its trigger the first time (never before
 * the wallet opened), else at the change.
 */
function keyedEarn(due: DueFood, previous: HeldFood | undefined, now: number, openedAt: number): Award {
  const { amounts, base, multipliers } = priceDomains({ [DIET_RULES.currency]: due.base }, due.day);
  let at = previous?.latestEarn ? now : Math.max(Date.parse(due.triggeredAt), openedAt);
  const last = previous?.latestEarn ? Date.parse(previous.latestEarn.occurredAt) : null;
  if (last !== null && at <= last) at = last + 1;
  const id = newId();
  getState().ledger = [
    ...getState().ledger,
    {
      id,
      occurredAt: new Date(at).toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts,
      trigger: due.trigger,
      awardKey: due.key,
      note: due.note,
      creditedOn: due.day,
      ...(base ? { base, multipliers } : {}),
      sources: [...due.sources],
    },
  ];
  return {
    id,
    entry: "earn",
    trigger: due.trigger,
    awardKey: due.key,
    subject: null,
    note: due.note,
    amounts: { ...amounts },
    ...(base ? { base: { ...base }, multipliers: { ...multipliers } } : {}),
  };
}

/** A reversal of everything the key pays. */
function keyedReversal(paid: HeldFood, now: number, { trigger, note }: { trigger: string; note: string }): Award {
  const amounts = Object.fromEntries(
    Object.entries(paid.net)
      .filter(([, n]) => n !== 0)
      .map(([k, n]) => [k, -n]),
  );
  const id = newId();
  getState().ledger = [
    ...getState().ledger,
    {
      id,
      occurredAt: new Date(now).toISOString(),
      entry: "reversal",
      provenance: "derived",
      amounts,
      trigger,
      awardKey: paid.key,
      note,
    },
  ];
  return { id, entry: "reversal", trigger, awardKey: paid.key, subject: null, note, amounts: { ...amounts } };
}

const NOTHING_PAID: FoodPaid = { fast: null, diet: null, total: 0, baseTotal: 0 };

/** What the ledger pays each food day from `from` to `to`. */
function foodPaidByDay(from: string, to: string): Map<string, FoodPaid> {
  const byDay = new Map<string, FoodPaid>();
  for (const held of heldFood().values()) {
    const [, trigger, day] = FOOD_KEY.exec(held.key)!;
    if (!held.paid || day < from || day > to) continue;
    const award: FoodAwardPaid = {
      base: latestBase(held),
      paid: held.net[DIET_RULES.currency] ?? 0,
      multiplier: held.latestEarn?.multipliers?.[DIET_RULES.currency] ?? null,
    };
    const current = byDay.get(day) ?? NOTHING_PAID;
    const next = trigger === FAST_TRIGGER ? { ...current, fast: award } : { ...current, diet: award };
    next.total = (next.fast?.paid ?? 0) + (next.diet?.paid ?? 0);
    next.baseTotal = (next.fast?.base ?? 0) + (next.diet?.base ?? 0);
    byDay.set(day, next);
  }
  return byDay;
}

/** The sobriety streak as of `asOf` seen today (`DrinkHistory`): from the Food Log's first day over its drink days. */
function foodSobriety(fold: FoodFold, asOf: string, today: string): SobrietyRow | null {
  const first = fold.days.keys().next();
  if (first.done) return null;
  const drinkDays = [...fold.days.values()].filter((d) => d.drinks > 0).map((d) => d.day);
  return sobrietyOn(asOf, today, first.value, drinkDays);
}

/** `GET /api/food/day`: a day's entries (oldest first), its fasts, the running fast, its summary and the streak. */
export function mockFoodDay(date?: string): FoodDayRead {
  const today = todayIso();
  const day = date ?? today;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) throw new Error(`'${day}' isn't a date (yyyy-MM-dd).`);
  const fold = foodFold();
  const facts = fold.days.get(day) ?? emptyDay(day, day < today);
  const meals = fold.meals
    .filter((m) => m.eatenOn === day)
    .sort((a, b) => a.eatenAt.localeCompare(b.eatenAt) || a.id.localeCompare(b.id));
  const during = (m: FoldedMeal) =>
    m.calories > 0
      ? (fold.fasts.find(
          (f) =>
            Date.parse(f.startedAt) < Date.parse(m.eatenAt) &&
            (f.endedAt === null || Date.parse(m.eatenAt) < Date.parse(f.endedAt)),
        )?.id ?? null)
      : null;
  const running = fold.fasts.find((f) => f.running) ?? null;
  const last = fold.meals.filter((m) => m.calories > 0).sort((a, b) => b.eatenAt.localeCompare(a.eatenAt))[0];
  return {
    day,
    today,
    meals: meals.map((m) => mealRow(m, during(m))),
    fasts: fold.fasts
      .filter((f) => f.endedOn === day)
      .sort((a, b) => a.startedAt.localeCompare(b.startedAt))
      .map(fastRow),
    running: running ? fastRow(running) : null,
    lastMeal: last ? mealRow(last) : null,
    summary: toSummary(facts, foodPaidByDay(day, day).get(day) ?? NOTHING_PAID, DIET_CAP),
    sobriety: foodSobriety(fold, day, today),
  };
}

/** `GET /api/food/days`: every day of a range (the week so far by default), at most 62 days. */
export function mockFoodDays(from?: string, to?: string): FoodDaysRead {
  const today = todayIso();
  const end = to ?? today;
  const start = from ?? startOfWeekMonday(end);
  if (end < start) throw new Error("The range must end on or after the day it starts.");
  const fold = foodFold();
  const paid = foodPaidByDay(start, end);
  const days: FoodDaySummary[] = [];
  for (let d = start; d <= end; d = addDays(d, 1)) {
    if (days.length >= FOOD_MAX_RANGE_DAYS) throw new Error(`Ask for at most ${FOOD_MAX_RANGE_DAYS} days at a time.`);
    days.push(toSummary(fold.days.get(d) ?? emptyDay(d, d < today), paid.get(d) ?? NOTHING_PAID, DIET_CAP));
  }
  // The streak as of the range's last day so far: today, when the range holds it.
  const asOf = end < today ? end : today;
  return { from: start, to: end, today, days, sobriety: asOf < start ? null : foodSobriety(fold, asOf, today) };
}

/**
 * `GET /api/food/recent`: the newest entry of each distinct name, for "Log again", with the
 * newest photo that name has had (its own, or an older entry's when it was logged without one).
 */
export function mockFoodRecent(limit = 20): MealRow[] {
  if (!Number.isInteger(limit) || limit < 1 || limit > FOOD_MAX_RECENT) {
    throw new Error(`Ask for 1 to ${FOOD_MAX_RECENT} recent entries.`);
  }
  const byName = new Map<string, MealRow>();
  for (const m of [...foodFold().meals].sort((a, b) => b.eatenAt.localeCompare(a.eatenAt))) {
    const name = m.name.trim().toLowerCase();
    const newest = byName.get(name);
    if (!newest) {
      if (byName.size === limit) continue;
      byName.set(name, mealRow(m));
    } else if (!newest.photoArtifactId && m.photoArtifactId) {
      byName.set(name, { ...newest, photoArtifactId: m.photoArtifactId });
    }
  }
  return [...byName.values()];
}
