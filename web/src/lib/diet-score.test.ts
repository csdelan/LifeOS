import { describe, expect, it } from "vitest";
import {
  caloriesPartFor,
  DIET_CAP,
  DIET_RULES,
  DRINK_SCORE,
  drinkFactorFor,
  DRINKS_TO_NOTHING,
  drinksLine,
  fastingFor,
  formatDuration,
  formatPart,
  guessMealType,
  scoreDay,
  scoreLabel,
} from "@/lib/diet-score";

const HOUR = 3600;

/** A day from its entries (calories, score) and its longest fast in seconds. */
function day(entries: [number, number][], fastSeconds: number | null = null) {
  return scoreDay({
    calories: entries.reduce((sum, [c]) => sum + c, 0),
    scoreCalories: entries.reduce((sum, [c, s]) => sum + c * s, 0),
    longestFastSeconds: fastSeconds,
  });
}

// The same table as the kernel's DietRulesTests (food-log plan §1).
describe("scoreDay", () => {
  it("pays nothing for 3,000 calories of ice cream and no fast", () => {
    expect(day([[3000, 1]])).toMatchObject({ quality: 0, caloriesPart: 0, fasting: 0, total: 0, averageScore: 1 });
  });

  it("pays 39 of 40 for a salmon salad and a 23-hour fast", () => {
    const s = day([[1200, 5]], 23 * HOUR);
    expect(s.quality).toBe(28);
    expect(s.caloriesPart).toBe(4);
    expect(formatPart(s.fasting)).toBe("7.3");
    expect([s.foodAward, s.fastAward, s.total]).toEqual([32, 7, 39]);
  });

  it("rounds each award, so the good day pays 31 where its total alone would round to 30", () => {
    const s = day(
      [
        [700, 5],
        [200, 4],
        [900, 4],
      ],
      16 * HOUR,
    );
    expect(formatPart(s.quality)).toBe("23.7");
    expect(formatPart(s.fasting)).toBe("2.7");
    expect([s.foodAward, s.fastAward, s.total]).toEqual([28, 3, 31]);
  });

  it("pays an ordinary day 15", () => {
    const s = day(
      [
        [400, 4],
        [700, 3],
        [900, 2],
        [200, 1],
      ],
      14 * HOUR,
    );
    expect(formatPart(s.quality)).toBe("11.1");
    expect(formatPart(s.caloriesPart)).toBe("2.4");
    expect(formatPart(s.fasting)).toBe("1.3");
    expect([s.foodAward, s.fastAward, s.total]).toEqual([14, 1, 15]);
  });

  it("has no average and no quality on a day of 0-calorie entries", () => {
    expect(day([[0, 5]])).toMatchObject({ quality: 0, averageScore: null, total: 0 });
  });

  it("never pays more than the cap", () => {
    expect(day([[1500, 5]], 40 * HOUR).total).toBe(DIET_CAP);
  });
});

describe("the parts", () => {
  it("pays calories from 500, fading from 2,000 to 0 at 2,500", () => {
    expect([0, 499, 500, 2000, 2250, 2499, 2500].map(caloriesPartFor)).toEqual([0, 0, 4, 4, 2, 0.008, 0]);
  });

  it("pays fasting by the second from 12 hours, up to 8 at 24", () => {
    expect(fastingFor(18 * HOUR)).toBe(4);
    expect(fastingFor(24 * HOUR)).toBe(8);
    expect(fastingFor(36 * HOUR)).toBe(8);
    expect(fastingFor(12 * HOUR)).toBe(0);
    // 12 h 45 m is half an XP, which rounds up.
    expect(fastingFor(12.75 * HOUR)).toBe(0.5);
    expect(day([], 12.75 * HOUR).fastAward).toBe(1);
  });
});

// The same table as the kernel's DietRulesTests (food-log plan §7.2).
describe("drinks (FOOD-6)", () => {
  /** The salmon day of §1 plus `beers` beers of 150 calories, each a 1. */
  const salmonDayWithBeers = (beers: number) =>
    scoreDay({
      calories: 1200 + 150 * beers,
      scoreCalories: 1200 * 5 + 150 * beers * DRINK_SCORE,
      longestFastSeconds: 23 * HOUR,
      drinks: beers,
    });

  it("starts the cut after two and leaves nothing at twelve", () => {
    expect([DIET_RULES.drinksFree, DIET_RULES.drinkCutPercent]).toEqual([2, 10]);
    expect(DRINK_SCORE).toBe(1);
    expect(DRINKS_TO_NOTHING).toBe(12);
    expect([0, 2, 3, 11, 12, 40].map(drinkFactorFor)).toEqual([1, 1, 0.9, 0.1, 0, 0]);
  });

  it.each([
    [0, "28", 4, 32, 7, 39],
    [2, "22.4", 4, 26, 7, 33],
    [3, "20.4", 4, 22, 7, 29],
    [4, "18.7", 4, 18, 6, 24],
    [12, "11.2", 0, 0, 0, 0],
  ])("pays the salmon day with %i beers as the plan's table says", (beers, quality, calories, food, fast, total) => {
    const s = salmonDayWithBeers(beers);
    expect(formatPart(s.quality)).toBe(quality);
    expect(s.caloriesPart).toBe(calories);
    expect(formatPart(s.fasting)).toBe("7.3");
    expect([s.foodAward, s.fastAward, s.total]).toEqual([food, fast, total]);
    expect(s.drinks).toBe(beers);
    // Before the close the fast pays in full, whatever was drunk.
    expect(s.fastAwardUncut).toBe(7);
  });

  it("cuts each award before rounding it", () => {
    // A 23-hour fast is 7.33: three drinks leave 6.6, which rounds to 7 (the rounded 7 cut would be 6.3, so 6).
    const three = scoreDay({ calories: 0, scoreCalories: 0, longestFastSeconds: 23 * HOUR, drinks: 3 });
    expect([three.fastAward, three.fastAwardUncut, three.drinkFactor]).toEqual([7, 7, 0.9]);
    // Four leave 80%: 5.87, so 6.
    expect(scoreDay({ calories: 0, scoreCalories: 0, longestFastSeconds: 23 * HOUR, drinks: 4 }).fastAward).toBe(6);
  });

  it("says a day's drinks and their cut", () => {
    expect(drinksLine(0)).toBeNull();
    expect(drinksLine(1)).toBe("1 drink · the cut starts above 2");
    expect(drinksLine(4)).toBe("4 drinks · 20% off the day's diet XP");
    expect(drinksLine(12)).toBe("12 drinks · 100% off the day's diet XP");
  });
});

describe("words", () => {
  it("guesses the meal type from the local time", () => {
    const at = (h: number, m = 0) => new Date(2026, 9, 20, h, m);
    expect([at(4), at(10, 59), at(11), at(15, 59), at(16), at(21, 59), at(22), at(3, 59)].map(guessMealType)).toEqual([
      "breakfast",
      "breakfast",
      "lunch",
      "lunch",
      "dinner",
      "dinner",
      "snack",
      "snack",
    ]);
  });

  it("names scores and durations as the kernel does", () => {
    expect(scoreLabel(5)).toBe("5 · very healthy");
    expect(scoreLabel(1)).toBe("1 · junk");
    expect(formatDuration(23 * HOUR + 5 * 60)).toBe("23 h 05 m");
    expect(formatDuration(45 * 60 + 59)).toBe("45 m");
  });
});
