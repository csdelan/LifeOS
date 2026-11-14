import { describe, expect, it } from "vitest";
import { buildAlignmentForest, type AlignmentEdge } from "@/lib/derive";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";

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
