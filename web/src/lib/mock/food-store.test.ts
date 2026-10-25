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
    // Oldest first (FOOD-7).
    expect(salmon.meals.map((m) => m.name)).toEqual(["Black coffee", "Salmon salad"]);
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
    // Health was 37 XP short of level 10: a week of diet XP reaches it, and Apprentice with it.
    expect(earned.progress).toEqual([
      { domain: "health_xp", levelFrom: 9, levelTo: 10, rankFrom: 0, rankTo: 1, rankTitle: "Apprentice", bestLevelFrom: 9, bestLevelTo: 10, bestRankFrom: 0, bestRankTo: 1 },
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
    // Read the days they landed on: 5 hours ago is yesterday for the first 5 hours of a day.
    const snackDay = mockFoodDay(snack.meal!.eatenOn);
    expect(snackDay.meals.find((m) => m.id === snack.meal!.id)?.duringFast).toBe(fast.fast!.id);
    const endDay = mockFoodDay(fast.fast!.endedOn!);
    expect(endDay.fasts.find((f) => f.id === fast.fast!.id)?.mealsDuring).toBe(1);
  });

  it("refuses in words, writing nothing", () => {
    const before = getState().foodEvents.length;
    expect(() => mockLogFood({ name: " ", score: 5, calories: 100 })).toThrow("Say what it was: a name is required.");
    expect(() => mockLogFood({ name: "x".repeat(81), score: 5, calories: 100 })).toThrow("Keep the name to 80 characters.");
    expect(() => mockLogFood({ name: "Toast", score: 6, calories: 100 })).toThrow(/health score is 1 to 5/);
    expect(() => mockLogFood({ name: "Toast", score: 3.7, calories: 100 })).toThrow(/in whole or half points, like 3 or 3\.5 .*got 3\.7\./);
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

  it("keeps a half score through the day's read, its sum and Log again (food-log plan §8)", () => {
    const wrap = mockLogFood({ name: "Turkey wrap", score: 3.5, calories: 1200 });
    expect(wrap.meal!.score).toBe(3.5);
    const meal = mockFoodDay().meals.find((m) => m.id === wrap.meal!.id)!;
    expect(meal.score).toBe(3.5);
    expect(mockFoodDay().summary).toMatchObject({ averageScore: 3.5, quality: 17.5, foodAward: 22 });
    expect(mockFoodRecent().find((m) => m.name === "Turkey wrap")?.score).toBe(3.5);
    mockEditFood(wrap.meal!.id, { name: "Turkey wrap", score: 4, calories: 1200 });
    expect(mockFoodDay().summary).toMatchObject({ averageScore: 4, quality: 21, foodAward: 25 });
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
    expect(health().totalXp).toBe(1_439);
    mockSettleFood();
    expect(health().totalXp).toBeGreaterThan(1_439);
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

  it("logs again with the photo already stored, and offers a name's newest photo (FOOD-5)", () => {
    const first = mockLogFood({ name: "Sausage burrito", score: 2, calories: 650, eatenAt: ago(26 * HOUR) }).meal!;
    const photo = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 1, 2, 3])], { type: "image/jpeg" });
    const id = mockSetFoodPhoto(first.id, photo, "burrito.jpg").meal!.photoArtifactId!;
    const stored = Object.keys(getState().artifacts).length;

    const again = mockLogFood({ name: "Sausage burrito", score: 2, calories: 650, photoArtifactId: id }).meal!;
    expect(again).toMatchObject({ photoArtifactId: id, versions: 1 });
    expect(again.id).not.toBe(first.id);
    expect(Object.keys(getState().artifacts)).toHaveLength(stored);

    // Each entry's photo is its own.
    mockRemoveFoodPhoto(again.id);
    expect(mockFoodDay(first.eatenOn).meals.find((m) => m.id === first.id)?.photoArtifactId).toBe(id);
    // The newest entry has no photo now, but Log again still offers the name's newest.
    expect(mockFoodRecent().find((m) => m.name === "Sausage burrito")).toMatchObject({ id: again.id, photoArtifactId: id });

    expect(() => mockLogFood({ name: "Toast", score: 2, calories: 300, photoArtifactId: "nope" })).toThrow(
      "That photo isn't stored here: pick the photo again, or log the entry without one.",
    );
  });

  // Drinks (FOOD-6, plan §7): as the kernel's FoodDrinksTests.
  it("stores a drink at score 1, typed as a drink, and keeps the flag until told otherwise", () => {
    const beer = mockLogFood({ name: "IPA", score: 5, calories: 200, alcoholic: true }).meal!;
    expect(beer).toMatchObject({ name: "IPA", score: 1, mealType: "drink", alcoholic: true });

    const edited = mockEditFood(beer.id, { name: "IPA", score: 4, calories: 210 }).meal!;
    expect(edited).toMatchObject({ score: 1, calories: 210, alcoholic: true });
    const photo = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 1])], { type: "image/jpeg" });
    expect(mockSetFoodPhoto(beer.id, photo).meal?.alcoholic).toBe(true);

    const notADrink = mockEditFood(beer.id, { name: "Kombucha", score: 3, calories: 90, alcoholic: false }).meal!;
    expect(notADrink).toMatchObject({ name: "Kombucha", score: 3, mealType: "drink", alcoholic: false });
    expect(mockFoodRecent(1)[0]).toMatchObject({ name: "Kombucha", alcoholic: false });
  });

  it("cuts a closed day's food and fast above two drinks, paying the fast in full until the close", () => {
    const today = todayIso();
    mockFastAction({ action: "record", from: ago(23 * HOUR + 60_000), to: ago(60_000) });
    mockLogFood({ name: "Salmon salad", score: 5, calories: 1200 });
    const beers = [1, 2, 3, 4].map(() => mockLogFood({ name: "Beer", score: 1, calories: 150, alcoholic: true }));
    // Open: the fast's 7 stays paid, and nothing is taken back on the spot.
    expect(beers.flatMap((b) => b.awards)).toEqual([]);
    const open = mockFoodDay().summary;
    expect(open).toMatchObject({ drinks: 4, drinkFactor: 0.8, foodAward: 18, fastAward: 6, total: 24 });
    expect(open.paid.fast?.base).toBe(7);

    // Closing pays the cut food and re-scores the fast with the cut.
    const closed = mockCloseFoodDay();
    const base = (r: typeof closed, key: string) =>
      r.awards.filter((a) => a.awardKey === key).map((a) => [a.entry, a.base?.health_xp ?? a.amounts.health_xp]);
    expect(base(closed, `diet:${today}`)).toEqual([["earn", 18]]);
    expect(base(closed, `fast:${today}`).map(([entry]) => entry)).toEqual(["reversal", "earn"]);
    expect(mockFoodDay().summary.paid).toMatchObject({ baseTotal: 24 });
    expect(mockSettleFood().awards).toEqual([]);

    // Taking the fourth back restores the fast (6.6 rounds to 7) and lifts the food to 22.
    const fewer = mockDeleteFood(beers[3].meal!.id);
    expect(base(fewer, `fast:${today}`).at(-1)).toEqual(["earn", 7]);
    expect(base(fewer, `diet:${today}`).at(-1)).toEqual(["earn", 22]);
    // The third: only the food moves.
    const back = mockDeleteFood(beers[2].meal!.id);
    expect(base(back, `fast:${today}`)).toEqual([]);
    expect(base(back, `diet:${today}`).at(-1)).toEqual(["earn", 26]);
    expect(mockFoodDay().summary.paid.baseTotal).toBe(33);
  });

  it("reads the sobriety streak on a day and over a range, and lists a day oldest first", () => {
    const today = todayIso();
    const before = mockFoodDay().sobriety!;
    expect(before).toMatchObject({ asOf: today, lastDrinkOn: null, since: before.firstDay });
    expect(before.days).toBe(before.bestDays);
    expect(before.days).toBeGreaterThan(5);

    mockLogFood({ name: "Wine", score: 1, calories: 120, alcoholic: true, eatenAt: ago(60_000) });
    mockLogFood({ name: "Bread", score: 3, calories: 200, eatenAt: ago(2 * 60_000) });
    const day = mockFoodDay();
    expect(day.meals.map((m) => m.name)).toEqual(["Bread", "Wine"]);
    // Today didn't count yet, so the run before it is the best.
    expect(day.sobriety).toMatchObject({ days: 0, bestDays: before.days, lastDrinkOn: today });

    // A range reads the streak as of its last day so far.
    expect(mockFoodDays(addDays(today, -6), today).sobriety?.asOf).toBe(today);
    expect(mockFoodDays(addDays(today, -6), addDays(today, 7)).sobriety?.days).toBe(0);
    // A past day counts itself, since it's over; today's streak runs to yesterday.
    expect(mockFoodDays(addDays(today, -6), addDays(today, -3)).sobriety).toMatchObject({
      asOf: addDays(today, -3),
      days: before.days - 2,
    });
    // Nothing before the Food Log began, or ahead of today.
    expect(mockFoodDays("2001-01-01", "2001-01-07").sobriety).toBeNull();
    expect(mockFoodDays(addDays(today, 1), addDays(today, 7)).sobriety).toBeNull();
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
