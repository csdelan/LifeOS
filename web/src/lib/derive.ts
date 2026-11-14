/**
 * The Map's alignment forest, folded from the subjects and the edges among them, which the mock
 * and the live reads both answer. Focus is worked out by the API (`GET /api/focus`, code review
 * CR106); the mock's copy of it is `lib/mock/dashboard.ts`.
 */

import type { Relation, SubjectListItem } from "@/lib/production-ui-types";
import { ALIGNMENT_TYPES } from "@/lib/subject-meta";

export type MapLens = "all" | "Goal" | "Project" | "Task";

export interface AlignmentEdge {
  fromId: string;
  toId: string;
  relation: Relation;
}

export interface AlignmentNode {
  id: string;
  instanceKey: string;
  subject: SubjectListItem;
  relationToParent: Relation | null;
  parentId: string | null;
  extraParents: { id: string; title: string; type: SubjectListItem["type"] }[];
  children: AlignmentNode[];
  depth: number;
}

export function buildAlignmentForest(
  allSubjects: SubjectListItem[],
  edges: AlignmentEdge[],
  opts?: { includeArchived?: boolean; lens?: MapLens },
): AlignmentNode[] {
  const includeArchived = opts?.includeArchived ?? false;
  const lens = opts?.lens ?? "all";
  const subjects = allSubjects.filter((x) => includeArchived || !x.archived);
  const by = new Map(subjects.map((x) => [x.id, x]));
  const childrenOf = new Map<string, { childId: string; relation: Relation }[]>();
  const parentsOf = new Map<string, { parentId: string; relation: Relation }[]>();

  for (const e of edges) {
    if (!by.has(e.fromId) || !by.has(e.toId)) continue;
    const kids = childrenOf.get(e.toId) ?? [];
    kids.push({ childId: e.fromId, relation: e.relation });
    childrenOf.set(e.toId, kids);
    const pars = parentsOf.get(e.fromId) ?? [];
    pars.push({ parentId: e.toId, relation: e.relation });
    parentsOf.set(e.fromId, pars);
  }

  const childIds = new Set(
    edges.filter((e) => by.has(e.fromId) && by.has(e.toId)).map((e) => e.fromId),
  );

  const roots = subjects.filter((x) => {
    if (x.type === "Value") return true;
    if (ALIGNMENT_TYPES.includes(x.type) && !childIds.has(x.id)) return true;
    return false;
  });

  roots.sort((a, b) => {
    const rank = (t: SubjectListItem) =>
      t.type === "Value" ? 0 : t.type === "Goal" ? 1 : t.type === "Project" ? 2 : 3;
    return rank(a) - rank(b) || a.title.localeCompare(b.title);
  });

  const walk = (
    id: string,
    parentId: string | null,
    relation: Relation | null,
    depth: number,
    path: Set<string>,
  ): AlignmentNode | null => {
    const subject = by.get(id);
    if (!subject) return null;
    if (path.has(id)) return null;
    const nextPath = new Set(path);
    nextPath.add(id);
    const extra = (parentsOf.get(id) ?? [])
      .filter((p) => p.parentId !== parentId)
      .map((p) => {
        const parent = by.get(p.parentId);
        return parent
          ? { id: parent.id, title: parent.title, type: parent.type }
          : null;
      })
      .filter((x): x is NonNullable<typeof x> => x !== null);

    const kids = (childrenOf.get(id) ?? [])
      .map((c) => walk(c.childId, id, c.relation, depth + 1, nextPath))
      .filter((n): n is AlignmentNode => n !== null);

    return {
      id,
      instanceKey: `${parentId ?? "root"}:${id}`,
      subject,
      relationToParent: relation,
      parentId,
      extraParents: extra,
      children: kids,
      depth,
    };
  };

  const forest = roots
    .map((r) => walk(r.id, null, null, 0, new Set()))
    .filter((n): n is AlignmentNode => n !== null);

  if (lens === "all") return forest;
  return filterLens(forest, lens);
}

function filterLens(nodes: AlignmentNode[], lens: MapLens): AlignmentNode[] {
  const keep = (n: AlignmentNode): AlignmentNode | null => {
    const kids = n.children.map(keep).filter((x): x is AlignmentNode => x !== null);
    const selfMatch = n.subject.type === lens || n.subject.type === "Value";
    if (!selfMatch && kids.length === 0) return null;
    return { ...n, children: kids };
  };
  return nodes.map(keep).filter((x): x is AlignmentNode => x !== null);
}
