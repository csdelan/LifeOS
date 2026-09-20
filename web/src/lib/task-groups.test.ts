import { describe, expect, it } from "vitest";
import { groupTasks, isHiddenTaskStatus, taskGroup } from "@/lib/task-groups";
import type { SubjectListItem } from "@/lib/production-ui-types";

function task(
  id: string,
  title: string,
  dates: { due?: string | null; scheduled?: string | null; status?: string },
): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:task:${id}`,
    type: "Task",
    title,
    status: dates.status ?? "Not started",
    due: dates.due ?? null,
    scheduled: dates.scheduled ?? null,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
  };
}

const today = "2026-09-20";

describe("task grouping", () => {
  it("applies overdue / today / upcoming / unscheduled precedence", () => {
    expect(taskGroup(task("a", "late", { due: "2026-09-18" }), today)).toBe("overdue");
    expect(taskGroup(task("b", "due today", { due: today }), today)).toBe("today");
    expect(taskGroup(task("c", "do today", { scheduled: today }), today)).toBe("today");
    expect(taskGroup(task("d", "soon", { due: "2026-09-22" }), today)).toBe("upcoming");
    expect(taskGroup(task("e", "parked", {}), today)).toBe("unscheduled");
  });

  it("lets due-overdue win even when scheduled is today", () => {
    expect(
      taskGroup(task("f", "late but scheduled", { due: "2026-09-18", scheduled: today }), today),
    ).toBe("overdue");
  });

  it("places each task in exactly one group (show-once)", () => {
    const rows = [
      task("a", "late", { due: "2026-09-18", scheduled: today }),
      task("b", "due today", { due: today, scheduled: "2026-09-22" }),
      task("c", "soon", { due: "2026-09-22", scheduled: "2026-09-25" }),
      task("d", "parked", {}),
    ];
    const grouped = groupTasks(rows, today);
    const ids = Object.values(grouped).flatMap((g) => g.map((t) => t.id));
    expect(ids.sort()).toEqual(["a", "b", "c", "d"]);
    expect(grouped.overdue.map((t) => t.id)).toEqual(["a"]);
    expect(grouped.today.map((t) => t.id)).toEqual(["b"]);
    expect(grouped.upcoming.map((t) => t.id)).toEqual(["c"]);
    expect(grouped.unscheduled.map((t) => t.id)).toEqual(["d"]);
  });

  it("folds terminal statuses out of the active working view", () => {
    expect(isHiddenTaskStatus("Completed")).toBe(true);
    expect(isHiddenTaskStatus("Cancelled")).toBe(true);
    expect(isHiddenTaskStatus("In progress")).toBe(false);
  });
});
