import { beforeEach, describe, expect, it } from "vitest";
import { addDays, todayIso } from "@/lib/dates";
import { clearMockActivity } from "@/lib/mock/activity-store";
import {
  getArtifact,
  getState,
  mockCloseFoodDay,
  mockDeleteFood,
  mockDomains,
  mockEditFood,
  mockFastAction,
  mockFoodDay,
  mockFoodDays,
  mockFoodRecent,
  mockLogFood,
  mockRemoveFoodPhoto,
  mockSetFoodPhoto,
  mockSettleDomains,
  mockSettleFood,
  resetStore,
} from "@/lib/mock/store";

const HOUR = 3_600_000;
const ago = (ms: number) => new Date(Date.now() - ms).toISOString();
const foodKeys = () => getState().ledger.filter((e) => /^(fast|diet):/.test(e.awardKey ?? ""));
const health = () => mockDomains().domains.find((d) => d.key === "health_xp")!;

describe("mock Food Log (FoodLogService, FoodReads, FoodRewards)", () => {
  beforeEach(() => {
    resetStore();
    clearMockActivity();
  });

  it("reads the seeded days: closed by time, scored, and unpaid until a settle", () => {
    const today = todayIso();
    const salmon = mockFoodDay(addDays(today, -2));
    expect(salmon.meals.map((m) => m.name)).toEqual(["Salmon salad", "Black coffee"]);
    expect(salmon.summary).toMatchObject({ closed: true, closedByHand: false, foodAward: 32, quality: 28, caloriesPart: 4 });
    // The coffee has no calories, so it was never eaten "during" the fast.
    expect(salmon.meals.every((m) => m.duringFast === null)).toBe(true);
    expect(salmon.fasts).toHaveLength(1);
    expect(salmon.summary.paid.total).toBe(0);

    const empty = mockFoodDay(today);
    expect(empty).toMatchObject({ day: today, today, meals: [], fasts: [], running: null });
    expect(empty.summary).toMatchObject({ closed: false, total: 0, cap: 40 });
    expect(empty.lastMeal?.name).toBe("Salmon salad");
  });

  it("leaves food to the Food Log's own settle, which pays each closed day once, at its multiplier", () => {
    expect(mockSettleDomains().awards).toEqual([]);
    expect(foodKeys()).toEqual([]);

    const earned = mockSettleFood();
    const keys = earned.awards.filter((a) => a.trigger === "diet" || a.trigger === "fast").map((a) => a.awardKey);
    expect(keys).toHaveLength(9); // five food days, four with a fast
    expect(earned.awards.every((a) => a.entry === "earn")).toBe(true);
    const diet = earned.awards.find((a) => a.awardKey === `diet:${addDays(todayIso(), -2)}`)!;
    expect(diet).toMatchObject({ subject: null, base: { health_xp: 32 }, multipliers: { health_xp: 1.2 }, amounts: { health_xp: 38 } });
    expect(diet.note).toMatch(/^Food day \w{3} \w{3} \d+$/);
    // Health was 40 XP short of level 10: a week of diet XP reaches it, and Apprentice with it.
    expect(earned.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice" },
    ]);

    expect(mockSettleFood().awards).toEqual([]);
    expect(mockFoodDay(addDays(todayIso(), -2)).summary.paid.diet).toEqual({ base: 32, paid: 38, multiplier: 1.2 });
  });

  it("pays a recorded fast when it's saved, and the day's food when it's closed", () => {
    const fast = mockFastAction({ action: "record", from: ago(23 * HOUR + 60_000), to: ago(60_000) });
    expect(fast).toMatchObject({ action: "fast_recorded", days: [todayIso()] });
    const fastAward = fast.awards.find((a) => a.awardKey === `fast:${todayIso()}`)!;
    expect(fastAward).toMatchObject({ base: { health_xp: 7 }, amounts: { health_xp: 8 } });

    const logged = mockLogFood({ name: "Salmon salad", score: 5, calories: 1200 });
    expect(logged.meal).toMatchObject({ name: "Salmon salad", score: 5, calories: 1200, eatenOn: todayIso() });
    expect(logged.awards.filter((a) => a.awardKey.endsWith(todayIso()))).toEqual([]);

    const closed = mockCloseFoodDay();
    expect(closed).toMatchObject({ action: "closed", day: todayIso() });
    expect(closed.awards.find((a) => a.awardKey === `diet:${todayIso()}`)).toMatchObject({
      base: { health_xp: 32 },
      amounts: { health_xp: 38 },
    });
    const day = mockFoodDay();
    expect(day.summary).toMatchObject({ closed: true, closedByHand: true, total: 39 });
    expect(day.summary.paid).toMatchObject({ total: 46, baseTotal: 39 });
    expect(mockCloseFoodDay()).toMatchObject({ action: "already_closed", awards: [] });
  });

  it("re-scores a paid day when its entries change, as one reversal and one earn", () => {
    mockSettleFood();
    const day = addDays(todayIso(), -2);
    const salmon = mockFoodDay(day).meals.find((m) => m.name === "Salmon salad")!;

    const edited = mockEditFood(salmon.id, { name: "Salmon salad", score: 4, calories: 1200 });
    expect(edited.meal).toMatchObject({ score: 4, eatenAt: salmon.eatenAt, versions: 2 });
    expect(edited.awards.map((a) => [a.entry, a.awardKey, a.amounts.health_xp])).toEqual([
      ["reversal", `diet:${day}`, -38],
      ["earn", `diet:${day}`, 30],
    ]);
    expect(mockFoodDay(day).summary.paid.diet).toMatchObject({ base: 25, paid: 30 });

    // Nothing changed since: the next settle leaves it alone.
    expect(mockSettleFood().awards).toEqual([]);

    const deleted = mockDeleteFood(salmon.id);
    expect(deleted.meal?.name).toBe("Salmon salad");
    // The coffee has no calories: the day no longer scores, so it's only given back.
    expect(deleted.awards.map((a) => [a.entry, a.amounts.health_xp])).toEqual([["reversal", -30]]);
  });

  it("begins, ends and edits a fast, and ends one when food with calories is logged", () => {
    const begun = mockFastAction({ action: "begin", at: ago(13 * HOUR) });
    expect(begun.fast).toMatchObject({ running: true, endedAt: null });
    expect(() => mockFastAction({ action: "begin" })).toThrow(/A fast is already running \(since .+\)\. End it first\./);
    expect(mockFoodDay().running?.id).toBe(begun.fast!.id);

    expect(() => mockLogFood({ name: "Black coffee", score: 5, calories: 0, endFast: true })).toThrow(
      "A 0-calorie entry doesn't end a fast; leave the fast running.",
    );
    const breakfast = mockLogFood({ name: "Eggs and toast", score: 4, calories: 450, endFast: true });
    expect(breakfast.fast).toMatchObject({ id: begun.fast!.id, running: false });
    expect(breakfast.fast!.seconds).toBeGreaterThanOrEqual(13 * 3600);
    // A 13-hour fast pays ⅔ XP, which rounds to 1 base.
    expect(breakfast.awards.find((a) => a.trigger === "fast")).toMatchObject({ base: { health_xp: 1 }, amounts: { health_xp: 1 } });

    const edited = mockFastAction({ action: "edit", fast: begun.fast!.id, from: ago(19 * HOUR), to: breakfast.meal!.eatenAt });
    expect(edited.awards.map((a) => a.entry)).toEqual(["reversal", "earn"]);
    expect(mockFoodDay().fasts[0].seconds).toBeGreaterThanOrEqual(19 * 3600 - 1);

    const deleted = mockFastAction({ action: "delete", fast: begun.fast!.id });
    expect(deleted.awards.map((a) => a.entry)).toEqual(["reversal"]);
    expect(mockFoodDay().fasts).toEqual([]);
  });

  it("flags food eaten inside a recorded fast without refusing it", () => {
    const fast = mockFastAction({ action: "record", from: ago(10 * HOUR), to: ago(HOUR) });
    const snack = mockLogFood({ name: "Granola bar", score: 2, calories: 200, eatenAt: ago(5 * HOUR) });
    expect(mockFoodDay().meals.find((m) => m.id === snack.meal!.id)?.duringFast).toBe(fast.fast!.id);
    expect(mockFoodDay().fasts[0].mealsDuring).toBe(1);
  });

  it("refuses in words, writing nothing", () => {
    const before = getState().foodEvents.length;
    expect(() => mockLogFood({ name: " ", score: 5, calories: 100 })).toThrow("Say what it was: a name is required.");
    expect(() => mockLogFood({ name: "x".repeat(81), score: 5, calories: 100 })).toThrow("Keep the name to 80 characters.");
    expect(() => mockLogFood({ name: "Toast", score: 6, calories: 100 })).toThrow(/health score is 1 to 5/);
    expect(() => mockLogFood({ name: "Toast", score: 3, calories: 5001 })).toThrow("Calories are a whole number from 0 to 5,000; got 5001.");
    expect(() => mockLogFood({ name: "Toast", score: 3, calories: 100, proteinG: -1 })).toThrow(/Protein is whole grams/);
    expect(() => mockLogFood({ name: "Toast", score: 3, calories: 100, eatenAt: new Date(Date.now() + 2 * HOUR).toISOString() })).toThrow(
      "A food entry can't be in the future.",
    );
    expect(() => mockLogFood({ name: "Toast", score: 3, calories: 100, endFast: true })).toThrow(
      "No fast is running, so there is none to end.",
    );
    expect(() => mockFastAction({ action: "end" })).toThrow("No fast is running, so there is none to end.");
    expect(() => mockFastAction({ action: "record", from: ago(HOUR), to: ago(2 * HOUR) })).toThrow("A fast ends after it starts.");
    const seeded = addDays(todayIso(), -2);
    expect(() => mockFastAction({ action: "record", from: `${seeded}T12:00:00`, to: `${seeded}T13:00:00` })).toThrow(
      /^That overlaps your fast from \w{3} \w{3} \d+, 9:30 pm to \w{3} \w{3} \d+, 8:30 pm\. Change or delete that fast first\.$/,
    );
    expect(() => mockCloseFoodDay(addDays(todayIso(), 1))).toThrow(/hasn't happened yet, so it can't be closed\./);
    expect(getState().foodEvents.length).toBe(before);
  });

  it("guesses the meal type from the time, and offers each recent name once for Log again", () => {
    const at = new Date();
    at.setHours(8, 30, 0, 0);
    if (at.getTime() > Date.now()) at.setDate(at.getDate() - 1);
    expect(mockLogFood({ name: "Oatmeal and fruit", score: 4, calories: 400, eatenAt: at.toISOString() }).meal?.mealType).toBe(
      "breakfast",
    );
    const recent = mockFoodRecent(50);
    expect(recent[0].name).toBe("Oatmeal and fruit");
    expect(recent.filter((m) => m.name === "Oatmeal and fruit")).toHaveLength(1);
    expect(() => mockFoodRecent(0)).toThrow("Ask for 1 to 100 recent entries.");
  });

  it("answers a range of days, the week so far by default", () => {
    const week = mockFoodDays();
    expect(week.to).toBe(todayIso());
    expect(new Date(`${week.from}T12:00:00`).getDay()).toBe(1);
    const span = mockFoodDays(addDays(todayIso(), -6), todayIso());
    expect(span.days.map((d) => d.meals)).toEqual([3, 4, 3, 3, 2, 0, 0]);
    expect(() => mockFoodDays(todayIso(), addDays(todayIso(), -1))).toThrow("The range must end on or after the day it starts.");
    expect(() => mockFoodDays(addDays(todayIso(), -62), todayIso())).toThrow("Ask for at most 62 days at a time.");
  });

  it("keeps Health's seeded history until the Food Log settles", () => {
    expect(health().totalXp).toBe(3_560);
    mockSettleFood();
    expect(health().totalXp).toBeGreaterThan(3_560);
  });
  it("keeps a photo as an artifact on a new version, through an edit, until it's removed (FOOD-5)", () => {
    const meal = mockLogFood({ name: "Ramen", score: 3, calories: 900 }).meal!;
    const photo = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 1, 2, 3])], { type: "image/jpeg" });

    const set = mockSetFoodPhoto(meal.id, photo, "ramen.jpg");
    expect(set.action).toBe("photo_set");
    const id = set.meal!.photoArtifactId!;
    expect(getArtifact(id)).toMatchObject({ eventKind: "observation", contentType: "image/jpeg", byteSize: 6, filename: "ramen.jpg" });
    expect(set.meal).toMatchObject({ name: "Ramen", score: 3, calories: 900, versions: 2 });

    expect(mockEditFood(meal.id, { name: "Ramen", score: 2, calories: 950 }).meal?.photoArtifactId).toBe(id);

    const removed = mockRemoveFoodPhoto(meal.id);
    expect(removed).toMatchObject({ action: "photo_removed", meal: { photoArtifactId: null, score: 2 } });
    expect(() => mockRemoveFoodPhoto(meal.id)).toThrow("Ramen has no photo to remove.");
  });

  it("refuses a photo that's too big or isn't an image, writing nothing", () => {
    const meal = mockLogFood({ name: "Toast", score: 2, calories: 300 }).meal!;
    const before = getState().foodEvents.length;
    expect(() => mockSetFoodPhoto(meal.id, new Blob([new Uint8Array(5 * 1024 * 1024 + 1)], { type: "image/jpeg" }))).toThrow(
      /at most 5 MB/,
    );
    expect(() => mockSetFoodPhoto(meal.id, new Blob(["%PDF"], { type: "application/pdf" }))).toThrow(/JPEG, PNG or WebP/);
    expect(getState().foodEvents).toHaveLength(before);
  });
});
