/**
 * Alignment graph projection — the same subjects + serves/results_in/supersedes
 * edges the Map outline uses, as a unique-node DAG (BROWSE-1).
 *
 * Area is a property, never an edge. Tags are not edges.
 */

import type { AlignmentEdge, MapLens } from "@/lib/derive";
import type { Relation, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { ALIGNMENT_TYPES } from "@/lib/subject-meta";

export type MapViewMode = "outline" | "graph";
export type GraphLayoutKind = "layered" | "force";
export type OrphanFilter = "all" | "hide" | "only";

/** Types that always appear on the graph (matching outline roots + their chain). */
export const GRAPH_ANCHOR_TYPES: readonly SubjectType[] = ALIGNMENT_TYPES;

export interface GraphSubjectNode {
  subject: SubjectListItem;
  degree: number;
  orphan: boolean;
}

export interface GraphRelationEdge {
  id: string;
  /** Canonical `from` (the edge's subject, typically the child). */
  fromId: string;
  /** Canonical `to` (the edge's object, typically the parent). */
  toId: string;
  relation: Relation;
}

export interface AlignmentGraphModel {
  nodes: GraphSubjectNode[];
  edges: GraphRelationEdge[];
}

export function isGraphSubject(
  subject: SubjectListItem,
  linkedIds: Set<string>,
): boolean {
  if (GRAPH_ANCHOR_TYPES.includes(subject.type)) return true;
  return linkedIds.has(subject.id);
}

export function linkedSubjectIds(edges: AlignmentEdge[]): Set<string> {
  const ids = new Set<string>();
  for (const e of edges) {
    ids.add(e.fromId);
    ids.add(e.toId);
  }
  return ids;
}

export function matchesLens(type: SubjectType, lens: MapLens): boolean {
  if (lens === "all") return true;
  return type === lens || type === "Value";
}

export function buildAlignmentGraph(
  subjects: SubjectListItem[],
  edges: AlignmentEdge[],
  opts?: {
    lens?: MapLens;
    area?: string | null;
    orphans?: OrphanFilter;
  },
): AlignmentGraphModel {
  const lens = opts?.lens ?? "all";
  const area = opts?.area ?? null;
  const orphans = opts?.orphans ?? "all";
  const linked = linkedSubjectIds(edges);
  const byId = new Map(subjects.map((s) => [s.id, s]));

  const degree = new Map<string, number>();
  const bump = (id: string) => degree.set(id, (degree.get(id) ?? 0) + 1);
  for (const e of edges) {
    if (!byId.has(e.fromId) || !byId.has(e.toId)) continue;
    bump(e.fromId);
    bump(e.toId);
  }

  let nodes: GraphSubjectNode[] = subjects
    .filter((s) => isGraphSubject(s, linked))
    .filter((s) => matchesLens(s.type, lens))
    .filter((s) => !area || s.area === area)
    .map((subject) => ({
      subject,
      degree: degree.get(subject.id) ?? 0,
      orphan: (degree.get(subject.id) ?? 0) === 0,
    }));

  if (orphans === "only") nodes = nodes.filter((n) => n.orphan);
  if (orphans === "hide") nodes = nodes.filter((n) => !n.orphan);

  const visible = new Set(nodes.map((n) => n.subject.id));
  const graphEdges: GraphRelationEdge[] = [];
  for (const e of edges) {
    if (!visible.has(e.fromId) || !visible.has(e.toId)) continue;
    graphEdges.push({
      id: `${e.fromId}:${e.relation}:${e.toId}`,
      fromId: e.fromId,
      toId: e.toId,
      relation: e.relation,
    });
  }

  nodes.sort(
    (a, b) =>
      typeRank(a.subject.type) - typeRank(b.subject.type) ||
      a.subject.title.localeCompare(b.subject.title),
  );

  return { nodes, edges: graphEdges };
}

function typeRank(type: SubjectType): number {
  const order: SubjectType[] = [
    "Value",
    "Goal",
    "Project",
    "Task",
    "Problem",
    "Idea",
    "Decision",
    "Commitment",
    "Constraint",
  ];
  const i = order.indexOf(type);
  return i === -1 ? order.length : i;
}

export function estimateNodeSize(
  title: string,
  degree: number,
): { width: number; height: number } {
  const width = Math.min(
    292,
    Math.max(196, 168 + Math.min(title.length, 32) * 3.1 + Math.min(degree, 6) * 8),
  );
  return { width, height: 84 };
}

export const AREA_PALETTE = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
] as const;

export function areaAccent(
  area: string | null | undefined,
  areas: { id: string; urn: string }[],
): string {
  if (!area) return "var(--muted-foreground)";
  const i = areas.findIndex((a) => a.urn === area || a.id === area);
  if (i < 0) return "var(--muted-foreground)";
  return AREA_PALETTE[i % AREA_PALETTE.length];
}
