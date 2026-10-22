import { describe, expect, it } from "vitest";
import { addDays } from "@/lib/dates";
import { daysText, sobrietyDetail, sobrietyHeadline, sobrietyOn, sobrietyText } from "@/lib/sobriety";

const FIRST = "2026-10-20";
const on = (n: number) => addDays(FIRST, n);

// The same table as the kernel's DietRulesTests (food-log plan §7.3).
describe("sobrietyOn", () => {
  it("has no streak before the Food Log's first day", () => {
    expect(sobrietyOn(on(-1), FIRST, [])).toBeNull();
  });

  it("counts every day from the first, the first included, with no drink yet", () => {
    expect(sobrietyOn(on(9), FIRST, [])).toMatchObject({ days: 10, bestDays: 10, since: FIRST, lastDrinkOn: null });
  });

  it("counts from the day after the last drink, and keeps the best run between drinks", () => {
    const drinks = [on(2), on(9)];
    expect(sobrietyOn(on(14), FIRST, drinks)).toMatchObject({
      asOf: on(14),
      days: 5,
      bestDays: 6,
      since: on(10),
      lastDrinkOn: on(9),
      firstDay: FIRST,
    });
    // A day with a drink has a streak of 0; a drink after it doesn't count.
    expect(sobrietyOn(on(9), FIRST, drinks)).toMatchObject({ days: 0, bestDays: 6 });
    expect(sobrietyOn(on(5), FIRST, drinks)).toMatchObject({ days: 3, bestDays: 3, lastDrinkOn: on(2) });
  });

  it("reads a drink on the first day, and the same day twice as once", () => {
    expect(sobrietyOn(FIRST, FIRST, [FIRST, FIRST])).toMatchObject({ days: 0, bestDays: 0 });
  });

  it("crosses months and years by calendar days", () => {
    expect(sobrietyOn("2027-01-02", "2026-12-30", [])?.days).toBe(4);
  });
});

describe("words", () => {
  const streak = sobrietyOn(on(14), FIRST, [on(2), on(9)])!;
  const drinkDay = sobrietyOn(on(9), FIRST, [on(2), on(9)])!;

  it("says the streak from where it's read", () => {
    expect(daysText(1)).toBe("1 day");
    expect(sobrietyHeadline(streak, on(14))).toBe("5 days sober");
    expect(sobrietyHeadline(drinkDay, on(9))).toBe("A drink today");
    expect(sobrietyHeadline(drinkDay, on(10))).toBe("A drink that day");
    expect(sobrietyText(streak, on(14))).toBe("Sober 5 days (best 6 days)");
    expect(sobrietyText(drinkDay, on(9))).toBe("A drink today (best 6 days)");
    expect(sobrietyDetail(drinkDay)).toBe("Best 6 days");
    expect(sobrietyDetail(streak)).toMatch(/^Drink-free since .+ · best 6 days$/);
  });
});
