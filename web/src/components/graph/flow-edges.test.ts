import { describe, expect, it } from "vitest";
import { toFlowEdge } from "@/components/graph/flow-edges";
import type { GraphRelationEdge } from "@/lib/graph-model";

// Stored as: Task "serves" Project.
const taskServesProject: GraphRelationEdge = {
  id: "task:serves:proj",
  fromId: "task",
  toId: "proj",
  relation: "serves",
};

describe("toFlowEdge", () => {
  it("draws parent → child but puts the arrowhead on the parent, so it reads Task serves Project", () => {
    const edge = toFlowEdge(taskServesProject, "layered");
    expect(edge.source).toBe("proj");
    expect(edge.target).toBe("task");
    expect(edge.markerStart).toBeDefined();
    expect(edge.markerEnd).toBeUndefined();
    expect(edge.label).toBe("serves");
  });

  it("uses the straight floating edge in Force and stepped edges in Layered", () => {
    expect(toFlowEdge(taskServesProject, "force").type).toBe("floating");
    expect(toFlowEdge(taskServesProject, "layered").type).toBe("smoothstep");
  });
});
