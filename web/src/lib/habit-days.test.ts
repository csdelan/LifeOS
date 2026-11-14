import { describe, expect, it } from "vitest";
import { maintainingHabitRows, todayHabitRows } from "@/lib/habit-days";
import type { HabitOccurrenceRow, HabitRow } from "@/lib/production-ui-types";

describe("today's habit rows (REWARD-2 maintenance)", () => {
  const habit = (id: string, status: string | null, extra: Partial<HabitRow> = {}): HabitRow => ({
    id,
    urn: `urn:bsk:habit:${id}`,
    name: id,
    allowsPartial: false,
    recurrence: "daily",
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    currentStreak: 0,
    lastState: null,
    status,
    ...extra,
  });
  const habits = [
    habit("building", null),
    habit("planned", "Planned"),
    habit("established", "Established", { allowsPartial: true }),
    habit("abandoned", "Abandoned"),
    habit("archived-established", "Established", { archived: true }),
  ];
  const today = "2026-10-21";
  const logged: HabitOccurrenceRow = {
    habitId: "established",
    habitUrn: "urn:bsk:habit:established",
    habitName: "established",
    occurrenceDate: today,
    state: "partial",
    allowsPartial: true,
  };

  it("tracks Building habits daily and maintains Established ones, leaving Planned and Abandoned out", () => {
    expect(todayHabitRows(habits, [], today).map((h) => h.habitId)).toEqual(["building"]);
    expect(maintainingHabitRows(habits, [], today).map((h) => h.habitId)).toEqual(["established"]);
  });

  it("reads a maintained habit's logged day, and an unlogged one as unrecorded, never missed", () => {
    expect(maintainingHabitRows(habits, [logged], today)).toEqual([{ ...logged, listed: true }]);
    expect(maintainingHabitRows(habits, [], today)).toEqual([
      expect.objectContaining({ habitId: "established", state: "unrecorded", allowsPartial: true, listed: false }),
    ]);
  });

  // Code review, web data #1: the kernel's days, not the web's reckoning of a pattern.
  it("takes a Building habit's day from the kernel, and offers one it doesn't list only to log", () => {
    const due: HabitOccurrenceRow = { ...logged, habitId: "building", habitName: "building", state: "unrecorded" };
    expect(todayHabitRows(habits, [due], today)).toEqual([{ ...due, listed: true }]);
    expect(todayHabitRows(habits, [], today)).toEqual([
      expect.objectContaining({ habitId: "building", state: "unrecorded", listed: false }),
    ]);
  });
});
