import { describe, expect, it } from "vitest";
import { endOfWeekSunday, startOfWeekMonday } from "@/lib/dates";
import {
  normalizeReview,
  obsidianUrl,
  reviewHabitDays,
  reviewPeriod,
  reviewStanding,
  reviewStateAsOf,
  stepReview,
  whyNotOpen,
} from "@/lib/reviews";
import type { HabitOccurrenceRow, HabitRow, ReviewDoc } from "@/lib/production-ui-types";

// Sun Nov 8 2026 names the week Mon Nov 2 – Sun Nov 8 (Chris, 2026-10-23).
describe("review weeks run Monday to Sunday, named by their Sunday", () => {
  it("finds the week's Monday and Sunday from any day in it", () => {
    for (const day of ["2026-11-02", "2026-11-05", "2026-11-08"]) {
      expect(startOfWeekMonday(day)).toBe("2026-11-02");
      expect(endOfWeekSunday(day)).toBe("2026-11-08");
    }
    expect(endOfWeekSunday("2026-11-09")).toBe("2026-11-15");
  });

  it("names a weekly review by its Sunday, looking back on Monday to Sunday and planning the next week", () => {
    expect(reviewPeriod("weekly", "2026-11-04")).toEqual({
      kind: "weekly",
      date: "2026-11-08",
      start: "2026-11-02",
      end: "2026-11-08",
      planStart: "2026-11-09",
      planEnd: "2026-11-15",
    });
  });

  it("has a daily review look back on its day and plan the next", () => {
    expect(reviewPeriod("daily", "2026-11-08")).toMatchObject({
      date: "2026-11-08",
      start: "2026-11-08",
      end: "2026-11-08",
      planStart: "2026-11-09",
      planEnd: "2026-11-09",
    });
  });

  it("steps a day or a week at a time", () => {
    expect(stepReview(reviewPeriod("daily", "2026-11-08"), 1)).toBe("2026-11-09");
    expect(stepReview(reviewPeriod("weekly", "2026-11-08"), -1)).toBe("2026-11-01");
  });
});

describe("a review opens on its day, not before", () => {
  it("opens the week's review on its Sunday, and not on the Saturday before", () => {
    const week = reviewPeriod("weekly", "2026-11-08");
    expect(whyNotOpen(week, "2026-11-08")).toBeNull();
    expect(whyNotOpen(week, "2026-11-09")).toBeNull();
    expect(whyNotOpen(week, "2026-11-07")).toMatch(/opens that Sunday/);
  });

  it("doesn't open tomorrow's daily review today", () => {
    expect(whyNotOpen(reviewPeriod("daily", "2026-11-09"), "2026-11-08")).toMatch(/opens that day/);
  });
});

describe("due at the end of its day, missed after (D8)", () => {
  const due = "2026-11-08";

  it("is open on its day", () => {
    expect(reviewStateAsOf({ dueOn: due }, due)).toEqual({ status: "Open", missed: false, completedLate: false });
  });

  it("reads missed once its day is over and it was never completed", () => {
    expect(reviewStateAsOf({ dueOn: due }, "2026-11-09")).toEqual({
      status: "Missed",
      missed: true,
      completedLate: false,
    });
  });

  it("keeps the miss when it's completed late", () => {
    expect(
      reviewStateAsOf({ dueOn: due, completedAt: "2026-11-10T03:00:00Z", firstCompletedOn: "2026-11-09" }, "2026-11-12"),
    ).toEqual({ status: "Completed", missed: true, completedLate: true });
  });

  it("isn't missed when completed on its day, even after it's reopened", () => {
    expect(
      reviewStateAsOf({ dueOn: due, completedAt: "2026-11-08T20:00:00Z", firstCompletedOn: due }, "2026-11-12"),
    ).toEqual({ status: "Completed", missed: false, completedLate: false });
    expect(reviewStateAsOf({ dueOn: due, completedAt: null, firstCompletedOn: due }, "2026-11-12")).toEqual({
      status: "Open",
      missed: false,
      completedLate: false,
    });
  });
});

describe("the review's standing in words", () => {
  const doc = (extra: Partial<ReviewDoc>): ReviewDoc => ({
    ...normalizeReview({ id: "r", kind: "daily", date: "2026-11-08", dueOn: "2026-11-08" }),
    ...extra,
  });

  it("says when it's due, missed, or completed late", () => {
    expect(reviewStanding(doc({}), "2026-11-08")).toBe("Due by the end of today. Complete it when it feels done.");
    expect(reviewStanding(doc({ status: "Missed", missed: true }), "2026-11-09")).toMatch(/^Missed: its day ended on /);
    expect(
      reviewStanding(doc({ status: "Completed", missed: true, completedLate: true, completedAt: null }), "2026-11-09"),
    ).toBe("Completed late. It still counts as missed. Still editable.");
  });
});

describe("the API's review row", () => {
  it("reads every field the page needs, with no reflection until one is linked", () => {
    const doc = normalizeReview({
      id: "6f1c",
      urn: "urn:bsk:review:6f1c",
      title: "Weekly review, Sun Nov 8",
      kind: "weekly",
      date: "2026-11-08",
      weekStart: "2026-11-02",
      weekEnd: "2026-11-08",
      planStart: "2026-11-09",
      planEnd: "2026-11-15",
      dueOn: "2026-11-08",
      status: "Completed",
      missed: true,
      completedLate: true,
      completedAt: "2026-11-09T18:00:00Z",
      keepFocus: false,
    });
    expect(doc).toMatchObject({
      kind: "weekly",
      date: "2026-11-08",
      planEnd: "2026-11-15",
      status: "Completed",
      missed: true,
      completedLate: true,
      keepFocus: false,
      reflection: null,
    });
  });

  it("keeps focus unless told otherwise, and reads an unknown status as Open", () => {
    expect(normalizeReview({ kind: "daily", status: "Weird" })).toMatchObject({ status: "Open", keepFocus: true });
  });

  it("reads a linked reflection, and a link missing its path or vault as none (reviews plan §4.1)", () => {
    const reflection = {
      path: "10 Reflections/2026-11-12 Daily reflection.md",
      vault: "Chris2.0",
      summary: "A calm day.",
      linkedAt: "2026-11-13T11:00:00Z",
    };
    expect(normalizeReview({ kind: "daily", reflection }).reflection).toEqual(reflection);
    expect(normalizeReview({ kind: "daily", reflection: { ...reflection, path: "" } }).reflection).toBeNull();
    expect(normalizeReview({ kind: "daily", reflection: { summary: "No place" } }).reflection).toBeNull();
  });

  it("opens a reflection in Obsidian by its vault and path, spaces and all", () => {
    expect(obsidianUrl({ vault: "Chris2.0", path: "10 Reflections/2026-11-12 Daily reflection.md" })).toBe(
      "obsidian://open?vault=Chris2.0&file=10%20Reflections%2F2026-11-12%20Daily%20reflection.md",
    );
  });
});

describe("habit check-offs (REVIEW-5)", () => {
  const habit = (id: string, extra: Partial<HabitRow> = {}): HabitRow => ({
    id,
    urn: `urn:bsk:habit:${id}`,
    name: id,
    allowsPartial: false,
    archived: false,
    createdAt: "2026-10-01T12:00:00Z",
    currentStreak: 0,
    status: "Building",
    ...extra,
  });
  const followed = (habitId: string, day: string): HabitOccurrenceRow => ({
    habitId,
    habitUrn: `urn:bsk:habit:${habitId}`,
    habitName: habitId,
    occurrenceDate: day,
    state: "followed",
    allowsPartial: false,
  });

  it("gives each day up to today a row per tracked habit, recorded or not", () => {
    const days = reviewHabitDays(
      [habit("walk"), habit("read")],
      [followed("walk", "2026-11-02")],
      "2026-11-02",
      "2026-11-08",
      "2026-11-04",
    );
    expect(days.map((d) => d.day)).toEqual(["2026-11-02", "2026-11-03", "2026-11-04"]);
    expect(days[0].rows.map((r) => [r.habitName, r.state])).toEqual([
      ["read", "unrecorded"],
      ["walk", "followed"],
    ]);
    expect(days[2].rows.every((r) => r.state === "unrecorded")).toBe(true);
  });

  it("leaves out a habit before its start, after its end, Planned, archived or abandoned", () => {
    const days = reviewHabitDays(
      [
        habit("late", { startDate: "2026-11-03" }),
        habit("ended", { endDate: "2026-11-02" }),
        habit("planned", { status: "Planned" }),
        habit("archived", { archived: true }),
        habit("kept", { status: "Established" }),
      ],
      [],
      "2026-11-02",
      "2026-11-03",
      "2026-11-08",
    );
    expect(days[0].rows.map((r) => r.habitName)).toEqual(["ended", "kept"]);
    expect(days[1].rows.map((r) => r.habitName)).toEqual(["kept", "late"]);
  });

  it("still shows a day that was recorded for a habit no longer tracked", () => {
    const days = reviewHabitDays([], [followed("old", "2026-11-02")], "2026-11-02", "2026-11-02", "2026-11-08");
    expect(days[0].rows.map((r) => r.habitName)).toEqual(["old"]);
  });
});
