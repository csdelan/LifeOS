import { describe, expect, it } from "vitest";
import { addDays } from "@/lib/dates";
import { daysText, drinkThatDay, sobrietyDetail, sobrietyHeadline, sobrietyOn, sobrietyText } from "@/lib/sobriety";

const FIRST = "2026-10-20";
const on = (n: number) => addDays(FIRST, n);

// The same table as the kernel's DietRulesTests (food-log plan §7.3).
const LATER = on(30); // a day well after the ones read, so each of them is over

describe("sobrietyOn", () => {
  it("has no streak before the Food Log's first day", () => {
    expect(sobrietyOn(on(-1), LATER, FIRST, [])).toBeNull();
  });

  it("counts every day from the first once it's over, the first included, and not today", () => {
    expect(sobrietyOn(on(9), LATER, FIRST, [])).toMatchObject({ days: 10, bestDays: 10, since: FIRST, lastDrinkOn: null });
    expect(sobrietyOn(on(9), on(9), FIRST, [])).toMatchObject({ days: 9, bestDays: 9 });
    expect(sobrietyOn(FIRST, FIRST, FIRST, [])).toMatchObject({ days: 0, bestDays: 0, lastDrinkOn: null });
  });

  it("counts from the day after the last drink, and keeps the best run between drinks", () => {
    const drinks = [on(2), on(9)];
    expect(sobrietyOn(on(14), on(14), FIRST, drinks)).toMatchObject({
      asOf: on(14),
      days: 4,
      bestDays: 6,
      since: on(10),
      lastDrinkOn: on(9),
      firstDay: FIRST,
    });
    expect(sobrietyOn(on(14), LATER, FIRST, drinks)?.days).toBe(5);
    // The day after a drink is 0 that day, and 1 the day after.
    expect(sobrietyOn(on(10), on(10), FIRST, drinks)).toMatchObject({ days: 0, bestDays: 6, since: on(10) });
    expect(sobrietyOn(on(11), on(11), FIRST, drinks)?.days).toBe(1);
    // A day with a drink has a streak of 0 at once; a drink after it doesn't count.
    expect(sobrietyOn(on(9), LATER, FIRST, drinks)).toMatchObject({ days: 0, bestDays: 6 });
    expect(sobrietyOn(on(9), on(9), FIRST, drinks)?.days).toBe(0);
    expect(sobrietyOn(on(5), LATER, FIRST, drinks)).toMatchObject({ days: 3, bestDays: 3, lastDrinkOn: on(2) });
  });

  it("reads a drink on the first day, and the same day twice as once", () => {
    expect(sobrietyOn(FIRST, LATER, FIRST, [FIRST, FIRST])).toMatchObject({ days: 0, bestDays: 0 });
  });

  it("crosses months and years by calendar days", () => {
    expect(sobrietyOn("2027-01-02", "2027-01-03", "2026-12-30", [])?.days).toBe(4);
  });
});

describe("words", () => {
  const drinks = [on(2), on(9)];
  const streak = sobrietyOn(on(14), LATER, FIRST, drinks)!;
  const drinkDay = sobrietyOn(on(9), on(9), FIRST, drinks)!;
  const dayAfter = sobrietyOn(on(10), on(10), FIRST, drinks)!;

  it("says the streak from where it's read", () => {
    expect(daysText(1)).toBe("1 day");
    expect(sobrietyHeadline(streak, LATER)).toBe("5 days sober");
    expect(sobrietyHeadline(drinkDay, on(9))).toBe("A drink today");
    expect(sobrietyHeadline(drinkDay, on(10))).toBe("A drink that day");
    expect(sobrietyText(streak, LATER)).toBe("Sober 5 days (best 6 days)");
    expect(sobrietyText(drinkDay, on(9))).toBe("A drink today (best 6 days)");
    expect(sobrietyDetail(drinkDay)).toBe("Best 6 days");
    expect(sobrietyDetail(streak)).toMatch(/^Drink-free since .+ · best 6 days$/);
  });

  it("says 0 days sober the day after a drink, which isn't a drink day", () => {
    expect(drinkThatDay(drinkDay)).toBe(true);
    expect(drinkThatDay(dayAfter)).toBe(false);
    expect(sobrietyHeadline(dayAfter, on(10))).toBe("0 days sober");
    expect(sobrietyText(dayAfter, on(10))).toBe("Sober 0 days (best 6 days)");
    expect(sobrietyDetail(dayAfter)).toMatch(/^Drink-free since .+ · best 6 days$/);
  });
});
