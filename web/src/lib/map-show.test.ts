import { describe, expect, it } from "vitest";
import { buildAlignmentForest, type AlignmentEdge, type AlignmentNode } from "@/lib/derive";
import { buildAlignmentGraph } from "@/lib/graph-model";
import { hiddenByShow, isFinished, parseMapShow, showForest } from "@/lib/map-show";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";

function item(
  id: string,
  type: SubjectType,
  status: string | null = null,
  extra: Partial<SubjectListItem> = {},
): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:${type.toLowerCase()}:${id}`,
    type,
    title: id,
    status,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    ...extra,
  };
}

const serves = (fromId: string, toId: string): AlignmentEdge => ({ fromId, toId, relation: "serves" });

/**
 * health (Identity Statement)
 * ├─ half (Goal · Completed)
 * │  └─ shoes (Task · Completed)
 * └─ lose (Goal · Active)
 *    ├─ meals (Project · Active)
 *    │  ├─ shop (Task · Completed)
 *    │  └─ prep (Task · Not started)
 *    └─ keto (Project · Abandoned)
 *       ├─ cancel (Task · Not started)
 *       └─ macros (Task · Completed)
 */
const subjects = [
  item("health", "Value"),
  item("half", "Goal", "Completed"),
  item("shoes", "Task", "Completed"),
  item("lose", "Goal", "Active"),
  item("meals", "Project", "Active"),
  item("shop", "Task", "Completed"),
  item("prep", "Task", "Not started"),
  item("keto", "Project", "Abandoned"),
  item("cancel", "Task", "Not started"),
  item("macros", "Task", "Completed"),
];
const edges = [
  serves("half", "health"),
  serves("shoes", "half"),
  serves("lose", "health"),
  serves("meals", "lose"),
  serves("shop", "meals"),
  serves("prep", "meals"),
  serves("keto", "lose"),
  serves("cancel", "keto"),
  serves("macros", "keto"),
];

function ids(nodes: AlignmentNode[]): string[] {
  const out: string[] = [];
  const walk = (ns: AlignmentNode[]) => {
    for (const n of ns) {
      out.push(n.id);
      walk(n.children);
    }
  };
  walk(nodes);
  return out.sort();
}

describe("the Map's Show filter on the outline", () => {
  const forest = buildAlignmentForest(subjects, edges);

  it("Open hides a finished item with everything under it, but keeps a finished parent of open work", () => {
    const { nodes, hidden } = showForest(forest, "open");
    expect(ids(nodes)).toEqual(["cancel", "health", "keto", "lose", "meals", "prep"]);
    // half, shoes, shop, macros
    expect(hidden).toBe(4);
  });

  it("Open + done steps also shows a finished item directly under an open parent", () => {
    const { nodes, hidden } = showForest(forest, "open-done");
    // half sits under the Identity Statement and shop under an active Project; shoes and
    // macros sit under finished parents, so they stay hidden.
    expect(ids(nodes)).toEqual(["cancel", "half", "health", "keto", "lose", "meals", "prep", "shop"]);
    expect(hidden).toBe(2);
  });

  it("All shows everything", () => {
    const { nodes, hidden } = showForest(forest, "all");
    expect(ids(nodes)).toEqual(ids(forest));
    expect(hidden).toBe(0);
  });

  it("keeps the selected item, with its path, whatever its status", () => {
    const { nodes, hidden } = showForest(forest, "open", "shoes");
    expect(ids(nodes)).toContain("shoes");
    expect(ids(nodes)).toContain("half");
    expect(hidden).toBe(2);
  });

  it("doesn't count an item that's still shown under another parent", () => {
    // shop also serves the abandoned keto: hidden there, but still a done step under meals.
    const both = buildAlignmentForest(subjects, [...edges, serves("shop", "keto")]);
    const { nodes, hidden } = showForest(both, "open-done");
    expect(ids(nodes).filter((id) => id === "shop")).toHaveLength(1);
    // shoes and macros only
    expect(hidden).toBe(2);
  });

  it("keeps a finished parent that open work under another parent also serves", () => {
    const both = buildAlignmentForest(subjects, [...edges, serves("cancel", "half")]);
    const { nodes } = showForest(both, "open");
    expect(ids(nodes)).toContain("half");
    expect(ids(nodes)).not.toContain("shoes");
  });

  it("treats a stopped repeating Task as finished, and a running one as open", () => {
    expect(isFinished(item("s", "Task", "Cancelled", { isSeries: true }))).toBe(true);
    expect(isFinished(item("s", "Task", "Not started", { isSeries: true }))).toBe(false);
    expect(isFinished(item("v", "Value"))).toBe(false);
  });
});

describe("the Map's Show filter on the graph", () => {
  it("follows the outline's rules", () => {
    expect([...hiddenByShow(subjects, edges, "open")].sort()).toEqual([
      "half",
      "macros",
      "shoes",
      "shop",
    ]);
    expect([...hiddenByShow(subjects, edges, "open-done")].sort()).toEqual(["macros", "shoes"]);
    expect(hiddenByShow(subjects, edges, "all").size).toBe(0);
    expect([...hiddenByShow(subjects, edges, "open", "shoes")].sort()).toEqual(["macros", "shop"]);
  });

  it("doesn't keep an item on the graph for the open item that superseded it", () => {
    const decisions = [item("old", "Decision", "Closed"), item("new", "Decision", "Implementing")];
    const replaced: AlignmentEdge[] = [{ fromId: "new", toId: "old", relation: "supersedes" }];
    expect([...hiddenByShow(decisions, replaced, "open")]).toEqual(["old"]);
    expect([...hiddenByShow(decisions, replaced, "open-done")]).toEqual(["old"]);
  });

  it("only counts work the other filters kept as open work under a finished item", () => {
    // Through the Goals lens, keto's open Task isn't on the graph, so keto doesn't stay for it.
    const goalsLens = buildAlignmentGraph(subjects, edges, { lens: "Goal", show: "open" });
    expect(goalsLens.nodes.map((n) => n.subject.id).sort()).toEqual(["health", "lose"]);
    expect(goalsLens.hiddenFinished).toBe(1);
  });

  it("drops the edges of what it hides and counts it", () => {
    const model = buildAlignmentGraph(subjects, edges, { show: "open" });
    expect(model.nodes.map((n) => n.subject.id).sort()).toEqual([
      "cancel",
      "health",
      "keto",
      "lose",
      "meals",
      "prep",
    ]);
    expect(model.edges.some((e) => e.fromId === "shop" || e.toId === "half")).toBe(false);
    expect(model.hiddenFinished).toBe(4);
  });

  it("shows everything when no Show is given", () => {
    const model = buildAlignmentGraph(subjects, edges);
    expect(model.nodes).toHaveLength(subjects.length);
    expect(model.hiddenFinished).toBe(0);
  });
});

describe("parseMapShow", () => {
  it("reads the three choices and nothing else", () => {
    expect(parseMapShow("open")).toBe("open");
    expect(parseMapShow("open-done")).toBe("open-done");
    expect(parseMapShow("all")).toBe("all");
    expect(parseMapShow("done")).toBeUndefined();
    expect(parseMapShow(undefined)).toBeUndefined();
  });
});
