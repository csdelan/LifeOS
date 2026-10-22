import { describe, expect, it } from "vitest";
import {
  dayInWords,
  dayStanding,
  dietHeat,
  foodPeriod,
  foodPeriodTitle,
  foodTotals,
  holdsToday,
  stepFoodDate,
} from "@/lib/food-period";
import type { FoodDaySummary } from "@/lib/production-ui-types";

const TODAY = "2026-10-20"; // a Tuesday

function day(on: string, overrides: Partial<FoodDaySummary> = {}): FoodDaySummary {
  return {
    day: on,
    meals: 0,
    calories: 0,
    averageScore: null,
    longestFastSeconds: null,
    closed: on < TODAY,
    closedByHand: false,
    closedAt: null,
    quality: 0,
    caloriesPart: 0,
    fasting: 0,
    foodAward: 0,
    fastAward: 0,
    total: 0,
    cap: 40,
    drinks: 0,
    drinkFactor: 1,
    paid: { fast: null, diet: null, total: 0, baseTotal: 0 },
    ...overrides,
  };
}

const paidDay = (on: string, base: number, paid: number, calories: number) =>
  day(on, {
    meals: 3,
    calories,
    averageScore: 4,
    total: base,
    paid: { fast: null, diet: { base, paid, multiplier: 1.2 }, total: paid, baseTotal: base },
  });

describe("foodPeriod", () => {
  it("covers Monday to Sunday around a day", () => {
    expect(foodPeriod("week", TODAY)).toEqual({ view: "week", from: "2026-10-19", to: "2026-10-25" });
    expect(foodPeriod("week", "2026-10-25")).toEqual({ view: "week", from: "2026-10-19", to: "2026-10-25" });
    expect(foodPeriod("week", "2026-10-26").from).toBe("2026-10-26");
  });

  it("covers the whole month, however long", () => {
    expect(foodPeriod("month", TODAY)).toEqual({ view: "month", from: "2026-10-01", to: "2026-10-31" });
    expect(foodPeriod("month", "2028-02-10").to).toBe("2028-02-29");
  });

  it("knows when it holds today", () => {
    expect(holdsToday(foodPeriod("week", TODAY), TODAY)).toBe(true);
    expect(holdsToday(foodPeriod("week", "2026-10-12"), TODAY)).toBe(false);
  });
});

describe("stepFoodDate", () => {
  it("steps a day, a week from its Monday, a month from its first", () => {
    expect(stepFoodDate("day", TODAY, -1)).toBe("2026-10-19");
    expect(stepFoodDate("week", TODAY, -1)).toBe("2026-10-12");
    expect(stepFoodDate("week", TODAY, 1)).toBe("2026-10-26");
    expect(stepFoodDate("month", "2026-10-31", 1)).toBe("2026-11-01");
    expect(stepFoodDate("month", "2026-03-31", -1)).toBe("2026-02-01");
  });
});

describe("foodPeriodTitle", () => {
  it("names the day, the week and the month", () => {
    expect(foodPeriodTitle(foodPeriod("day", TODAY), TODAY)).toBe("Today");
    expect(foodPeriodTitle(foodPeriod("week", TODAY), TODAY)).toMatch(/^Week of Oct 19$/);
    expect(foodPeriodTitle(foodPeriod("month", TODAY), TODAY)).toBe("October 2026");
  });
});

describe("dayStanding and dayInWords", () => {
  it("is ahead, on track, or paid", () => {
    expect(dayStanding(day("2026-10-21"), TODAY)).toEqual({ kind: "future" });
    expect(dayStanding(day(TODAY, { total: 35 }), TODAY)).toEqual({ kind: "open", base: 35 });
    expect(dayStanding(paidDay("2026-10-18", 39, 46, 1200), TODAY)).toEqual({
      kind: "paid",
      base: 39,
      paid: 46,
      multiplier: 1.2,
    });
  });

  it("says the day in words for its button", () => {
    expect(dayInWords(paidDay("2026-10-18", 39, 46, 1200), TODAY)).toBe(
      "Sun, Oct 18, 2026: 46 Health XP paid (39 base) · 3 entries, 1,200 cal, average score 4",
    );
    expect(dayInWords(day(TODAY, { total: 7, longestFastSeconds: 23 * 3600 }), TODAY)).toBe(
      "Tue, Oct 20, 2026 (today): on track for 7 of 40 · 0 entries, 0 cal, fasted 23 h 00 m",
    );
    expect(dayInWords(day("2026-10-14"), TODAY)).toBe("Wed, Oct 14, 2026: 0 Health XP paid (0 base) · nothing logged");
  });
});

describe("foodTotals", () => {
  it("adds what was paid, averages closed and logged days, and ignores days ahead", () => {
    const days = [
      paidDay("2026-10-19", 39, 46, 1200),
      day(TODAY, { meals: 1, calories: 600, total: 20 }),
      day("2026-10-21"),
    ];
    expect(foodTotals(days, TODAY)).toEqual({
      paid: 46,
      closedDays: 1,
      daysSoFar: 2,
      averagePaid: 46,
      loggedDays: 2,
      averageCalories: 900,
      drinks: 0,
      countedDays: 0,
      drinkFreeDays: 0,
    });
  });

  it("counts drinks over the days so far, and drink-free days from the Food Log's first day", () => {
    const days = [
      day("2026-10-17"), // before the first day: not counted
      day("2026-10-18", { meals: 3, drinks: 4 }),
      day("2026-10-19"), // nothing logged: drink-free
      day(TODAY, { meals: 2, drinks: 1 }),
      day("2026-10-21", { drinks: 9 }), // ahead: ignored
    ];
    expect(foodTotals(days, TODAY, "2026-10-18")).toMatchObject({ drinks: 5, countedDays: 3, drinkFreeDays: 1 });
    // Without the first day there's nothing to count drink-free days against.
    expect(foodTotals(days, TODAY)).toMatchObject({ drinks: 5, countedDays: 0, drinkFreeDays: 0 });
  });

  it("names a day's drinks in words", () => {
    expect(dayInWords(day("2026-10-19", { meals: 2, calories: 300, drinks: 2, total: 0 }), TODAY)).toContain(
      "2 drinks",
    );
  });

  it("has no averages with nothing to average", () => {
    const totals = foodTotals([day(TODAY)], TODAY);
    expect(totals.averagePaid).toBeNull();
    expect(totals.averageCalories).toBeNull();
  });
});

describe("dietHeat", () => {
  it("shades a base out of 40 in four steps", () => {
    expect([0, 1, 10, 11, 20, 29, 31, 40, 45].map(dietHeat)).toEqual([0, 1, 1, 2, 2, 3, 4, 4, 4]);
  });
});
