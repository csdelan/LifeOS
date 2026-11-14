import { describe, expect, it } from "vitest";
import { deriveDashboard } from "@/lib/mock/dashboard";
import type { CoverRead, SubjectListItem, SubjectType } from "@/lib/production-ui-types";

function item(
  id: string,
  type: SubjectType,
  title: string,
  extra: Partial<SubjectListItem> = {},
): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:${type.toLowerCase()}:${id}`,
    type,
    title,
    status: null,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    ...extra,
  };
}

describe("deriveDashboard's primary focus cover (COVER-3)", () => {
  const cover = { photographer: "Mina Cho", url: "https://images.unsplash.com/photo-1" } as CoverRead;
  const base = { edges: [], inboxCount: 0, habits: [], occurrences: [], today: "2026-10-23" };

  it("carries the focus subject's cover from the list it was given", () => {
    const subjects = [item("g", "Goal", "Ship", { status: "Active", cover })];
    expect(deriveDashboard({ ...base, subjects }).primaryFocus).toMatchObject({ subjectId: "g", cover });
    const chosen = deriveDashboard({
      ...base,
      subjects,
      primaryFocus: { title: "Ship", subjectId: "g", kind: "goal" },
    });
    expect(chosen.primaryFocus.cover).toBe(cover);
  });

  it("has none for a focus without a cover, or with no goal at all", () => {
    expect(deriveDashboard({ ...base, subjects: [item("g", "Goal", "Ship", { status: "Active" })] }).primaryFocus.cover).toBeNull();
    expect(deriveDashboard({ ...base, subjects: [] }).primaryFocus).toMatchObject({ subjectId: "", cover: null });
  });
});

describe("deriveDashboard's Due & overdue list (TASKS-1)", () => {
  const base = { edges: [], inboxCount: 0, habits: [], occurrences: [], today: "2026-10-28" };
  const task = (id: string, extra: Partial<SubjectListItem>) =>
    item(id, "Task", id, { status: "Not started", ...extra });

  it("holds a Task once its do-date arrives, until it's done, beside the late and due-today ones", () => {
    const subjects = [
      task("week-chore", { scheduled: "2026-10-26", due: "2026-11-01" }),
      task("yesterday", { scheduled: "2026-10-27" }),
      task("late", { due: "2026-10-20" }),
      task("due-today", { due: "2026-10-28" }),
      task("next-week", { scheduled: "2026-11-02" }),
      task("done", { scheduled: "2026-10-26", status: "Completed" }),
    ];
    const ids = deriveDashboard({ ...base, subjects }).dueAndOverdue.map((t) => t.id);
    expect(ids.sort()).toEqual(["due-today", "late", "week-chore", "yesterday"]);
  });
});
