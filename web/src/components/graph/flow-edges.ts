import { MarkerType, type Edge } from "@xyflow/react";
import type { GraphLayoutKind, GraphRelationEdge } from "@/lib/graph-model";
import type { Relation } from "@/lib/production-ui-types";

const RELATION_LABEL: Record<Relation, string> = {
  serves: "serves",
  results_in: "results in",
  supersedes: "supersedes",
};

function relationStroke(relation: Relation): string {
  if (relation === "serves") return "var(--type-goal)";
  if (relation === "results_in") return "var(--type-project)";
  return "var(--attention)";
}

/**
 * The xyflow edge for one relation. The edge is drawn parent → child (so
 * layered DOWN is Value → Goal → Project → Task), but the arrowhead sits at
 * the parent end: it points the way the label reads — Task "serves" Project.
 */
export function toFlowEdge(e: GraphRelationEdge, layout: GraphLayoutKind): Edge {
  const stroke = relationStroke(e.relation);
  return {
    id: e.id,
    source: e.toId,
    target: e.fromId,
    type: layout === "layered" ? "smoothstep" : "floating",
    label: RELATION_LABEL[e.relation],
    selectable: false,
    markerStart: {
      type: MarkerType.ArrowClosed,
      width: 14,
      height: 14,
      color: stroke,
    },
    style: {
      stroke,
      strokeWidth: e.relation === "supersedes" ? 1.6 : 1.25,
      strokeDasharray: e.relation === "supersedes" ? "5 4" : undefined,
    },
    labelStyle: { fontSize: 10, fill: "var(--muted-foreground)" },
    labelBgStyle: { fill: "var(--card)", fillOpacity: 0.92 },
    labelBgPadding: [3, 5],
    labelBgBorderRadius: 6,
  };
}
