import { describe, expect, it } from "vitest";
import { applyStreakToHabit, recomputeStreak } from "@/lib/habit-streak";
import type { HabitOccurrenceRow, HabitRow } from "@/lib/production-ui-types";

function occ(
  date: string,
  state: HabitOccurrenceRow["state"],
): HabitOccurrenceRow {
  return {
    habitId: "h1",
    habitUrn: "urn:bsk:habit:h1",
    habitName: "Mobility",
    occurrenceDate: date,
    state,
    allowsPartial: true,
  };
}

describe("habit streak recompute", () => {
  it("counts consecutive followed days backward from today", () => {
    const rows = [occ("2026-09-20", "followed"), occ("2026-09-19", "followed"), occ("2026-09-18", "followed")];
    expect(recomputeStreak(rows, "2026-09-20").currentStreak).toBe(3);
  });

  it("does not break the streak when today is still unrecorded", () => {
    const rows = [occ("2026-09-20", "unrecorded"), occ("2026-09-19", "followed"), occ("2026-09-18", "followed")];
    expect(recomputeStreak(rows, "2026-09-20").currentStreak).toBe(2);
  });

  it("breaks on partial, missed, or a closed unrecorded day", () => {
    expect(
      recomputeStreak([occ("2026-09-20", "followed"), occ("2026-09-19", "partial")], "2026-09-20")
        .currentStreak,
    ).toBe(1);
    expect(
      recomputeStreak([occ("2026-09-20", "followed"), occ("2026-09-19", "not_followed")], "2026-09-20")
        .currentStreak,
    ).toBe(1);
    expect(
      recomputeStreak([occ("2026-09-19", "unrecorded"), occ("2026-09-18", "followed")], "2026-09-20")
        .currentStreak,
    ).toBe(0);
  });

  it("backfills lastState onto the habit row", () => {
    const habit: HabitRow = {
      id: "h1",
      urn: "urn:bsk:habit:h1",
      name: "Mobility",
      allowsPartial: true,
      archived: false,
      createdAt: "2026-01-01T00:00:00.000Z",
      currentStreak: 0,
      lastState: "unrecorded",
    };
    const next = applyStreakToHabit(habit, [occ("2026-09-20", "followed")], "2026-09-20");
    expect(next.currentStreak).toBe(1);
    expect(next.lastState).toBe("followed");
  });
});
