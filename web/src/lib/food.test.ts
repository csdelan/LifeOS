import { describe, expect, it } from "vitest";
import {
  fastEarningText,
  foodWriteMessage,
  fromLocalInput,
  mealTypeWord,
  normalizeFoodSummary,
  summaryHeadline,
  toLocalInput,
} from "@/lib/food";
import type { FoodDaySummary, FoodWriteResult } from "@/lib/production-ui-types";

const HOUR = 3600;

function summary(overrides: Partial<FoodDaySummary> = {}): FoodDaySummary {
  return {
    day: "2026-10-20",
    meals: 1,
    calories: 1200,
    averageScore: 5,
    longestFastSeconds: 23 * HOUR,
    closed: false,
    closedByHand: false,
    closedAt: null,
    quality: 28,
    caloriesPart: 4,
    fasting: 7.33,
    foodAward: 32,
    fastAward: 7,
    total: 39,
    cap: 40,
    paid: { fast: null, diet: null, total: 0, baseTotal: 0 },
    ...overrides,
  };
}

function write(overrides: Partial<FoodWriteResult>): FoodWriteResult {
  return {
    awards: [],
    action: "logged",
    meal: null,
    fast: null,
    day: "2026-10-20",
    days: [],
    ...overrides,
  } as FoodWriteResult;
}

describe("summaryHeadline", () => {
  it("says what an open day is on track for", () => {
    expect(summaryHeadline(summary())).toBe("On track for 39 of 40");
  });

  it("says what a closed day paid, at the day's multiplier", () => {
    const closed = summary({
      closed: true,
      closedByHand: true,
      paid: {
        fast: { base: 7, paid: 8, multiplier: 1.2 },
        diet: { base: 32, paid: 38, multiplier: 1.2 },
        total: 46,
        baseTotal: 39,
      },
    });
    expect(summaryHeadline(closed)).toBe("39 base → 46 Health XP (×1.2)");
  });

  it("leaves the multiplier out when none applied", () => {
    const closed = summary({
      closed: true,
      paid: { fast: null, diet: { base: 15, paid: 15, multiplier: null }, total: 15, baseTotal: 15 },
    });
    expect(summaryHeadline(closed)).toBe("15 base → 15 Health XP");
  });

  it("tells a closed day that paid nothing from an empty one", () => {
    expect(summaryHeadline(summary({ closed: true, total: 0 }))).toBe("Closed · nothing to pay");
    expect(summaryHeadline(summary({ closed: true, meals: 0, total: 0 }))).toBe("Nothing logged");
  });
});

describe("fastEarningText", () => {
  it("counts down to 12 hours, then earns, then is full at 24", () => {
    expect(fastEarningText(11 * HOUR)).toBe("Pays from 12 h · 1 h 00 m to go");
    expect(fastEarningText(18 * HOUR)).toBe("Earning since 12 h · 4 of 8 XP");
    expect(fastEarningText(24 * HOUR)).toBe("The full 8 XP");
    expect(fastEarningText(30 * HOUR)).toBe("The full 8 XP");
  });
});

describe("foodWriteMessage", () => {
  const salmon = { name: "Salmon salad" } as FoodWriteResult["meal"];
  const fast = { seconds: 23 * HOUR + 5 * 60 } as FoodWriteResult["fast"];

  it("names the entry, and the fast it ended", () => {
    expect(foodWriteMessage(write({ meal: salmon }))).toBe("Logged Salmon salad");
    expect(foodWriteMessage(write({ meal: salmon, fast }))).toBe("Logged Salmon salad and ended your fast · 23 h 05 m");
    expect(foodWriteMessage(write({ action: "edited", meal: salmon }))).toBe("Saved Salmon salad");
    expect(foodWriteMessage(write({ action: "deleted", meal: salmon }))).toBe("Deleted Salmon salad");
  });

  it("says what happened to a fast or a day", () => {
    expect(foodWriteMessage(write({ action: "fast_begun" }))).toBe("Fast begun");
    expect(foodWriteMessage(write({ action: "fast_ended", fast }))).toBe("Fast ended · 23 h 05 m");
    expect(foodWriteMessage(write({ action: "fast_recorded", fast }))).toBe("Fast recorded · 23 h 05 m");
    expect(foodWriteMessage(write({ action: "closed" }))).toBe("Day closed");
    expect(foodWriteMessage(write({ action: "already_closed" }))).toBe("That day is already closed");
  });

  it("says where a photo went", () => {
    expect(foodWriteMessage(write({ action: "photo_set", meal: salmon }))).toBe("Photo added to Salmon salad");
    expect(foodWriteMessage(write({ action: "photo_removed", meal: salmon }))).toBe("Photo removed from Salmon salad");
  });
});

describe("local times", () => {
  it("round-trips a datetime-local value", () => {
    const at = new Date(2026, 9, 20, 12, 30);
    expect(toLocalInput(at)).toBe("2026-10-20T12:30");
    expect(fromLocalInput("2026-10-20T12:30")).toBe(at.toISOString());
  });

  it("refuses an incomplete value", () => {
    expect(fromLocalInput("")).toBeNull();
    expect(fromLocalInput("2026-10-20")).toBeNull();
  });
});

describe("shapes", () => {
  it("reads the API's decimals as numbers", () => {
    const raw = { ...summary(), quality: "28" as unknown as number, fasting: "7.33" as unknown as number };
    const s = normalizeFoodSummary(raw);
    expect(s.quality).toBe(28);
    expect(s.fasting).toBeCloseTo(7.33);
  });

  it("names meal types", () => {
    expect(mealTypeWord("breakfast")).toBe("Breakfast");
    expect(mealTypeWord("drink")).toBe("Drink");
  });
});
