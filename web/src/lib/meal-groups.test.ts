import { describe, expect, it } from "vitest";
import { formatClock } from "@/lib/food";
import { groupMeals, groupName, groupTimeRange, mealGroupKey } from "@/lib/meal-groups";
import type { MealRow } from "@/lib/production-ui-types";

let n = 0;
function meal(name: string, at: string, overrides: Partial<MealRow> = {}): MealRow {
  n += 1;
  return {
    id: `meal-${String(n).padStart(3, "0")}`,
    name,
    score: 4,
    calories: 300,
    proteinG: null,
    carbsG: null,
    fatG: null,
    mealType: "lunch",
    eatenAt: `2026-10-21T${at}:00`,
    eatenOn: "2026-10-21",
    photoArtifactId: null,
    alcoholic: false,
    duringFast: null,
    firstLoggedAt: `2026-10-21T${at}:00`,
    loggedAt: `2026-10-21T${at}:00`,
    versions: 1,
    ...overrides,
  };
}

describe("groupMeals (FOOD-7)", () => {
  it("combines identical entries whatever the name's case or spaces, the meal type or the photo", () => {
    const groups = groupMeals([
      meal("Beer", "19:10", { alcoholic: true, score: 1, mealType: "drink" }),
      meal(" beer ", "20:30", { alcoholic: true, score: 1, mealType: "dinner", photoArtifactId: "photo-1" }),
      meal("BEER", "21:30", { alcoholic: true, score: 1, mealType: "drink" }),
    ]);
    expect(groups).toHaveLength(1);
    const [beers] = groups;
    expect([beers.count, beers.calories, beers.first.name, groupName(beers)]).toEqual([3, 900, "Beer", "Beer ×3"]);
    expect(beers.photo?.artifactId).toBe("photo-1");
    expect(groupTimeRange(beers)).toBe(`${formatClock(beers.first.eatenAt)}–${formatClock(beers.last.eatenAt)}`);
  });

  it("keeps entries apart that differ in score, calories, a nutrient or the alcohol flag", () => {
    const base = meal("Toast", "08:00");
    const keys = [
      base,
      { ...base, score: 3 },
      { ...base, calories: 301 },
      { ...base, proteinG: 10 },
      { ...base, carbsG: 0 },
      { ...base, fatG: 2 },
      { ...base, alcoholic: true },
    ].map(mealGroupKey);
    expect(new Set(keys).size).toBe(7);
  });

  it("lists cards oldest first, each at its first copy's time", () => {
    const groups = groupMeals([
      meal("Coffee", "10:00", { calories: 5 }),
      meal("Eggs", "08:00"),
      meal("Coffee", "07:00", { calories: 5 }),
      meal("Salad", "12:00"),
    ]);
    expect(groups.map(groupName)).toEqual(["Coffee ×2", "Eggs", "Salad"]);
    expect(groups[0].meals.map((m) => m.eatenAt)).toEqual(["2026-10-21T07:00:00", "2026-10-21T10:00:00"]);
  });

  it("sums the nutrients the copies carry, and flags the card when any copy was inside a fast", () => {
    const [shakes] = groupMeals([
      meal("Protein shake", "09:00", { proteinG: 30, carbsG: 5 }),
      meal("Protein shake", "15:00", { proteinG: 30, carbsG: 5, duringFast: "fast-1" }),
    ]);
    expect([shakes.proteinG, shakes.carbsG, shakes.fatG, shakes.duringFast]).toEqual([60, 10, null, true]);
  });

  it("shows one time when the copies share the minute, and the name alone for one entry", () => {
    const [same] = groupMeals([meal("Shot", "23:00"), meal("Shot", "23:00")]);
    expect(groupTimeRange(same)).toBe(formatClock(same.first.eatenAt));
    const [one] = groupMeals([meal("Apple", "15:00")]);
    expect([groupName(one), one.count, one.photo]).toEqual(["Apple", 1, null]);
  });
});
