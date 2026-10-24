import { describe, expect, it } from "vitest";
import { parseRecurrence, recurrenceLabel, sameRecurrence, serializeRecurrence } from "@/lib/recurrence";
import {
  afterCompletion,
  datesFor,
  mapSubjects,
  nextOccurrence,
  normalizeSeriesRow,
  periodReading,
  repeatKind,
  repeatLabel,
  seriesInPeriod,
  seriesTally,
  shownStatus,
  taskPatternProblem,
  weekOf,
} from "@/lib/task-series";
import { composeRecap } from "@/lib/review-recap";
import type {
  SubjectListItem,
  TaskRepetitionRow,
  TaskSeriesHistory,
  TaskSeriesRow,
} from "@/lib/production-ui-types";

describe("the Task-only repeat shapes (TASKS-3)", () => {
  it("reads and writes once each week and every N after the last completion", () => {
    expect(parseRecurrence('{"freq":"each","period":"week"}')).toEqual({ freq: "each", period: "week" });
    expect(parseRecurrence('{"freq":"each","period":"fortnight"}')).toBeNull();
    const fridge = parseRecurrence('{"freq":"interval","every":30,"unit":"days","from":"completion"}');
    expect(fridge).toEqual({ freq: "interval", every: 30, unit: "days", from: "completion" });
    expect(serializeRecurrence(fridge!)).toBe('{"freq":"interval","every":30,"unit":"days","from":"completion"}');
    expect(serializeRecurrence({ freq: "each", period: "week" })).toBe('{"freq":"each","period":"week"}');
    // Counted from the start and from the last completion are different repeats.
    expect(
      sameRecurrence('{"freq":"interval","every":30,"unit":"days"}', serializeRecurrence(fridge!)),
    ).toBe(false);
  });

  it("says them plainly", () => {
    expect(recurrenceLabel({ freq: "each", period: "week" })).toBe("Once each week");
    expect(recurrenceLabel({ freq: "interval", every: 30, unit: "days", from: "completion" })).toBe(
      "30 days after the last one's done",
    );
    expect(recurrenceLabel({ freq: "interval", every: 1, unit: "months", from: "completion" })).toBe(
      "A month after the last one's done",
    );
    expect(repeatLabel('{"freq":"weekly","on":["wed"]}')).toBe("Every Wednesday");
    expect(repeatLabel(null)).toBeNull();
  });
});

describe("how a repetition is dated", () => {
  it("knows each kind, and that a cue can't repeat a Task", () => {
    expect(repeatKind({ freq: "weekly", on: ["wed"] })).toBe("calendar");
    expect(repeatKind({ freq: "monthly", day: 1 })).toBe("calendar");
    expect(repeatKind({ freq: "each", period: "week" })).toBe("each-week");
    expect(repeatKind({ freq: "interval", every: 30, unit: "days", from: "completion" })).toBe("after-completion");
    expect(repeatKind({ freq: "trigger", cue: "after dinner" })).toBeNull();
    expect(taskPatternProblem({ freq: "trigger", cue: "after dinner" })).toMatch(/needs dates/);
    expect(taskPatternProblem({ freq: "daily" })).toBeNull();
    expect(taskPatternProblem(null)).toBeNull();
  });

  it("dates once each week Monday to Sunday, and every other kind on the date", () => {
    expect(weekOf("2026-10-25")).toBe("2026-10-19"); // a Sunday ends the week
    expect(weekOf("2026-10-19")).toBe("2026-10-19");
    expect(datesFor("each-week", "2026-10-19")).toEqual({ due: "2026-10-25", scheduled: "2026-10-19" });
    expect(datesFor("calendar", "2026-10-21")).toEqual({ due: "2026-10-21", scheduled: null });
  });

  it("counts after the last completion, a month landing on a shorter month's last day", () => {
    const every = (n: number, unit: "days" | "weeks" | "months") =>
      ({ freq: "interval", every: n, unit, from: "completion" }) as const;
    expect(afterCompletion(every(30, "days"), "2026-10-21")).toBe("2026-11-20");
    expect(afterCompletion(every(2, "weeks"), "2026-10-21")).toBe("2026-11-04");
    expect(afterCompletion(every(1, "months"), "2026-01-31")).toBe("2026-02-28");
  });

  it("finds a calendar pattern's next date from the start", () => {
    expect(nextOccurrence({ freq: "weekly", on: ["wed"] }, "2026-10-01", "2026-10-22")).toBe("2026-10-28");
    expect(nextOccurrence({ freq: "monthly", day: 31 }, "2026-01-01", "2026-04-01")).toBe("2026-05-31");
    expect(nextOccurrence({ freq: "interval", every: 2, unit: "weeks" }, "2026-10-07", "2026-10-08")).toBe(
      "2026-10-21",
    );
    // Never before the start.
    expect(nextOccurrence({ freq: "daily" }, "2026-11-01", "2026-10-22")).toBe("2026-11-01");
  });
});

describe("the readings", () => {
  it("tallies the done against the done and missed", () => {
    expect(seriesTally({ completedCount: 3, missedCount: 1 })).toBe("3 of 4 done");
    expect(seriesTally({ completedCount: 0, missedCount: 0 })).toBe("None closed yet");
  });

  it("reads the API's int64 counts as numbers", () => {
    const row = normalizeSeriesRow({
      id: "s",
      urn: "urn:bsk:task:s",
      title: "S",
      recurrence: "{}",
      archived: false,
      stopped: false,
      openCount: "1" as unknown as number,
      completedCount: "3" as unknown as number,
      missedCount: 0,
      cancelledCount: 0,
      bounty: { xp: "4" as unknown as number },
    } satisfies TaskSeriesRow);
    expect(row.openCount).toBe(1);
    expect(row.completedCount).toBe(3);
    expect(row.bounty).toEqual({ xp: 4 });
  });
});

function seriesRow(id: string, title: string): TaskSeriesRow {
  return {
    id,
    urn: `urn:bsk:task:${id}`,
    title,
    recurrence: '{"freq":"weekly","on":["wed"]}',
    archived: false,
    stopped: false,
    openCount: 0,
    completedCount: 0,
    missedCount: 0,
    cancelledCount: 0,
  };
}

function repetition(due: string, status: string, extra: Partial<TaskRepetitionRow> = {}): TaskRepetitionRow {
  const closed = ["Completed", "Missed", "Cancelled"].includes(status);
  return {
    id: `r-${due}`,
    urn: `urn:bsk:task:r-${due}`,
    title: "Take out trash",
    occurrence: due,
    due,
    status,
    closed,
    archived: false,
    ...extra,
  };
}

describe("a review's repeating tasks (TASKS-3)", () => {
  // Four Wednesdays in October, reviewed on Thursday the 29th.
  const trash: TaskSeriesHistory = {
    series: seriesRow("trash", "Take out trash"),
    repetitions: [
      repetition("2026-10-28", "Not started"),
      repetition("2026-10-21", "Completed"),
      repetition("2026-10-14", "Missed"),
      repetition("2026-10-07", "Completed"),
      repetition("2026-09-30", "Completed"),
    ],
  };

  it("counts each one's repetitions due in the period, the done against the done and missed", () => {
    const [row] = seriesInPeriod([trash], "2026-10-01", "2026-10-31", "2026-10-29");
    expect(row).toMatchObject({ seriesId: "trash", lastDue: "2026-10-28", completedCount: 2, missedCount: 1, lateCount: 1 });
    expect(periodReading(row)).toBe("2 of 3 done · 1 missed · 1 late");
  });

  it("says what's still to do, and leaves out a skipped one and a hidden one", () => {
    const week: TaskSeriesHistory = {
      series: seriesRow("dinner", "Shop for Sunday dinner"),
      repetitions: [
        repetition("2026-10-31", "Not started"),
        repetition("2026-10-24", "Cancelled"),
        repetition("2026-10-17", "Completed", { archived: true }),
      ],
    };
    const [row] = seriesInPeriod([week], "2026-10-12", "2026-10-31", "2026-10-29");
    expect(periodReading(row)).toBe("None closed yet · 1 still to do · 1 skipped");
  });

  it("lists only the ones with a repetition due in the period, by title", () => {
    const rent: TaskSeriesHistory = {
      series: seriesRow("rent", "Pay rent"),
      repetitions: [repetition("2026-11-01", "Not started")],
    };
    expect(seriesInPeriod([trash, rent], "2026-10-26", "2026-11-01", "2026-10-29").map((r) => r.title)).toEqual([
      "Pay rent",
      "Take out trash",
    ]);
    expect(seriesInPeriod([rent], "2026-10-19", "2026-10-25", "2026-10-25")).toEqual([]);
  });

  it("is the recap's Repeating tasks section, each line opening its repeating task", () => {
    const sections = composeRecap({
      start: "2026-10-19",
      end: "2026-10-25",
      asOf: "2026-10-25",
      subjects: [],
      history: {},
      occurrences: [],
      inbox: [],
      series: [trash],
    });
    const repeating = sections.find((s) => s.id === "repeating");
    expect(repeating?.label).toBe("Repeating tasks");
    expect(repeating?.items).toEqual([
      { kind: "task-series", at: "2026-10-21", title: "Take out trash", detail: "1 of 1 done", subjectId: "trash" },
    ]);
  });
});

describe("a repeating task on the Map (TASKS-3)", () => {
  const task = (id: string, extra: Partial<SubjectListItem>): SubjectListItem =>
    ({ id, urn: `urn:bsk:task:${id}`, type: "Task", title: id, ...extra }) as SubjectListItem;

  it("is one node, its series: the repetitions are left out", () => {
    const subjects = [
      task("series", { isSeries: true, seriesId: null, status: "Not started" }),
      task("this-week", { isSeries: false, seriesId: "series" }),
      task("last-week", { isSeries: false, seriesId: "series" }),
      task("one-off", { isSeries: false, seriesId: null }),
    ];
    expect(mapSubjects(subjects).map((s) => s.id)).toEqual(["series", "one-off"]);
  });

  it("reads Repeating, or Stopped once Cancelled or hidden; other subjects keep their status", () => {
    expect(shownStatus(task("s", { isSeries: true, status: "Not started" }))).toBe("Repeating");
    expect(shownStatus(task("s", { isSeries: true, status: "Cancelled" }))).toBe("Stopped");
    expect(shownStatus(task("s", { isSeries: true, status: "Not started", archived: true }))).toBe("Stopped");
    expect(shownStatus(task("t", { status: "In progress" }))).toBe("In progress");
    expect(shownStatus(task("t", {}))).toBe("Not started");
  });
});
