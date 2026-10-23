import { describe, expect, it } from "vitest";
import {
  buildAlignmentForest,
  deriveDashboard,
  maintainingHabitRows,
  todayHabitRows,
  type AlignmentEdge,
} from "@/lib/derive";
import type {
  CoverRead,
  HabitOccurrenceRow,
  HabitRow,
  SubjectListItem,
  SubjectType,
} from "@/lib/production-ui-types";

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

describe("buildAlignmentForest", () => {
  it("duplicates a multi-parent child and records extra parents", () => {
    const subjects = [
      item("v", "Value", "Health"),
      item("g", "Goal", "Run"),
      item("p1", "Project", "Base"),
      item("p2", "Project", "Strength"),
      item("t", "Task", "Long run"),
    ];
    const edges: AlignmentEdge[] = [
      { fromId: "g", toId: "v", relation: "serves" },
      { fromId: "p1", toId: "g", relation: "results_in" },
      { fromId: "p2", toId: "g", relation: "results_in" },
      { fromId: "t", toId: "p1", relation: "serves" },
      { fromId: "t", toId: "p2", relation: "serves" },
    ];
    const forest = buildAlignmentForest(subjects, edges);
    const goal = forest[0]?.children.find((n) => n.id === "g");
    const underP1 = goal?.children.find((n) => n.id === "p1")?.children.find((n) => n.id === "t");
    const underP2 = goal?.children.find((n) => n.id === "p2")?.children.find((n) => n.id === "t");
    expect(underP1).toBeTruthy();
    expect(underP2).toBeTruthy();
    expect(underP1?.extraParents.map((p) => p.id)).toContain("p2");
    expect(underP2?.extraParents.map((p) => p.id)).toContain("p1");
  });

  it("guards cycles so a loop does not recurse forever", () => {
    const subjects = [
      item("v", "Value", "Craft"),
      item("g", "Goal", "Ship"),
      item("p", "Project", "Web"),
      item("t", "Task", "Polish"),
    ];
    const edges: AlignmentEdge[] = [
      { fromId: "g", toId: "v", relation: "serves" },
      { fromId: "p", toId: "g", relation: "results_in" },
      { fromId: "t", toId: "p", relation: "serves" },
      { fromId: "p", toId: "t", relation: "serves" },
    ];
    const forest = buildAlignmentForest(subjects, edges);
    const titles: string[] = [];
    const walk = (nodes: typeof forest) => {
      for (const n of nodes) {
        titles.push(n.id);
        walk(n.children);
      }
    };
    walk(forest);
    expect(titles.filter((id) => id === "p").length).toBeGreaterThan(0);
    expect(titles.length).toBeLessThan(20);
  });
});

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

  it("tracks Building habits daily and maintains Established ones, leaving Abandoned out", () => {
    expect(todayHabitRows(habits, [], today).map((h) => h.habitId)).toEqual(["building"]);
    expect(maintainingHabitRows(habits, [], today).map((h) => h.habitId)).toEqual(["established"]);
  });

  it("reads a maintained habit's logged day, and an unlogged one as unrecorded, never missed", () => {
    expect(maintainingHabitRows(habits, [logged], today)).toEqual([logged]);
    expect(maintainingHabitRows(habits, [], today)).toEqual([
      expect.objectContaining({ habitId: "established", state: "unrecorded", allowsPartial: true }),
    ]);
  });
});

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
