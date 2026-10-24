/**
 * The Map's Show filter: how much finished work the outline and the graph include.
 *
 * - Open hides a finished item together with everything under it.
 * - Open + done steps also shows a finished item that sits directly under an open parent
 *   (the Tasks ticked off in an active Project), but not the finished items under it.
 * - All shows everything.
 *
 * Whatever the choice, a finished item stays while open work sits under it, so open work
 * never drops out of sight, and the item being looked at (the selected one) stays too.
 */

import type { AlignmentEdge, AlignmentNode } from "@/lib/derive";
import { isTerminal } from "@/lib/production-ui-types";
import type { SubjectListItem } from "@/lib/production-ui-types";

export type MapShow = "open" | "open-done" | "all";

export const DEFAULT_MAP_SHOW: MapShow = "open";

export const MAP_SHOW_OPTIONS: { id: MapShow; label: string }[] = [
  { id: "open", label: "Open" },
  { id: "open-done", label: "Open + done steps" },
  { id: "all", label: "All" },
];

export function parseMapShow(value: unknown): MapShow | undefined {
  return value === "open" || value === "open-done" || value === "all" ? value : undefined;
}

/**
 * Finished: any done status (Completed, Abandoned, Cancelled, Missed …). A stopped
 * repeating Task is Cancelled. Identity Statements have no status, so they're never finished.
 */
export function isFinished(subject: SubjectListItem): boolean {
  return isTerminal(subject.status);
}

export interface ShownForest {
  nodes: AlignmentNode[];
  /** How many distinct items Show left out. */
  hidden: number;
}

/** The outline after Show. `keepId` (the selected item) stays, with its path. */
export function showForest(
  forest: AlignmentNode[],
  show: MapShow,
  keepId?: string,
): ShownForest {
  if (show === "all") return { nodes: forest, hidden: 0 };

  const prune = (nodes: AlignmentNode[], parentOpen: boolean): AlignmentNode[] => {
    const out: AlignmentNode[] = [];
    for (const n of nodes) {
      const finished = isFinished(n.subject);
      const children = prune(n.children, !finished);
      const doneStep = show === "open-done" && parentOpen;
      if (finished && n.id !== keepId && children.length === 0 && !doneStep) continue;
      out.push({ ...n, children });
    }
    return out;
  };

  const nodes = prune(forest, false);
  const before = collectIds(forest);
  const after = collectIds(nodes);
  let hidden = 0;
  for (const id of before) if (!after.has(id)) hidden++;
  return { nodes, hidden };
}

/**
 * The ids among `subjects` that Show leaves off the graph, by the outline's rules. Only
 * edges between these subjects count, so whatever the other filters dropped can't keep a
 * finished item on the graph. A `supersedes` edge isn't a parent link here: the item it
 * replaced is history, not a parent of the open work that replaced it.
 */
export function hiddenByShow(
  subjects: SubjectListItem[],
  edges: AlignmentEdge[],
  show: MapShow,
  keepId?: string,
): Set<string> {
  const hidden = new Set<string>();
  if (show === "all") return hidden;

  const byId = new Map(subjects.map((s) => [s.id, s]));
  const parentsOf = new Map<string, string[]>();
  for (const e of edges) {
    if (e.relation === "supersedes") continue;
    if (!byId.has(e.fromId) || !byId.has(e.toId)) continue;
    const parents = parentsOf.get(e.fromId) ?? [];
    parents.push(e.toId);
    parentsOf.set(e.fromId, parents);
  }

  // Everything above an open (or kept) item has open work under it.
  const hasOpenBelow = new Set<string>();
  const queue = subjects.filter((s) => !isFinished(s) || s.id === keepId).map((s) => s.id);
  while (queue.length > 0) {
    const id = queue.pop()!;
    for (const p of parentsOf.get(id) ?? []) {
      if (hasOpenBelow.has(p)) continue;
      hasOpenBelow.add(p);
      queue.push(p);
    }
  }

  for (const s of subjects) {
    if (!isFinished(s) || s.id === keepId || hasOpenBelow.has(s.id)) continue;
    const doneStep =
      show === "open-done" &&
      (parentsOf.get(s.id) ?? []).some((p) => !isFinished(byId.get(p)!));
    if (!doneStep) hidden.add(s.id);
  }
  return hidden;
}

function collectIds(nodes: AlignmentNode[], into: Set<string> = new Set()): Set<string> {
  for (const n of nodes) {
    into.add(n.id);
    collectIds(n.children, into);
  }
  return into;
}
