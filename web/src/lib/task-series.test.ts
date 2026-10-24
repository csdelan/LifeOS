import { describe, expect, it } from "vitest";
import { parseRecurrence, recurrenceLabel, sameRecurrence, serializeRecurrence } from "@/lib/recurrence";
import {
  afterCompletion,
  datesFor,
  nextOccurrence,
  normalizeSeriesRow,
  repeatKind,
  repeatLabel,
  seriesTally,
  taskPatternProblem,
  weekOf,
} from "@/lib/task-series";
import type { TaskSeriesRow } from "@/lib/production-ui-types";

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
