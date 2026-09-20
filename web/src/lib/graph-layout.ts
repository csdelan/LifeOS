import ELK from "elkjs/lib/elk.bundled.js";
import type { ElkNode } from "elkjs";
import type { GraphLayoutKind } from "@/lib/graph-model";

const elk = new ELK();

export interface LayoutNode {
  id: string;
  width: number;
  height: number;
}

export interface LayoutEdge {
  id: string;
  /** Visual source — parent, so layered DOWN is Value → Goal → Project → Task. */
  source: string;
  target: string;
}

export async function layoutAlignmentGraph(
  nodes: LayoutNode[],
  edges: LayoutEdge[],
  kind: GraphLayoutKind,
): Promise<Map<string, { x: number; y: number }>> {
  const layered = kind === "layered";
  const graph: ElkNode = {
    id: "root",
    layoutOptions: layered
      ? {
          "elk.algorithm": "layered",
          "elk.direction": "DOWN",
          "elk.edgeRouting": "POLYLINE",
          "elk.layered.spacing.nodeNodeBetweenLayers": "78",
          "elk.spacing.nodeNode": "42",
          "elk.padding": "[48, 48, 48, 48]",
          "elk.layered.nodePlacement.bk.fixedAlignment": "BALANCED",
          "elk.separateConnectedComponents": "true",
          "elk.spacing.componentComponent": "64",
        }
      : {
          "elk.algorithm": "org.eclipse.elk.force",
          "elk.force.model": "FRUCHTERMAN_REINGOLD",
          "elk.force.repulsivePower": "2",
          "elk.force.iterations": "500",
          "elk.spacing.nodeNode": "96",
          "elk.padding": "[56, 56, 56, 56]",
        },
    children: nodes.map((n) => ({
      id: n.id,
      width: n.width,
      height: n.height,
    })),
    edges: edges.map((e) => ({
      id: e.id,
      sources: [e.source],
      targets: [e.target],
    })),
  };

  const laid = await elk.layout(graph);
  const pos = new Map<string, { x: number; y: number }>();
  for (const child of laid.children ?? []) {
    pos.set(child.id, { x: child.x ?? 0, y: child.y ?? 0 });
  }
  return pos;
}
