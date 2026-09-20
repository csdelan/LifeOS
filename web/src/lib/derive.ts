/**
 * Shared composition for Map forest and Focus dashboard.
 *
 * Both the mock reader and the live API client call these so the tree /
 * dashboard logic is not duplicated or trapped in the mock. The API returns
 * primitives (subjects + relation edges + inbox/habits); this module folds them.
 */

import { isTerminal } from "@/lib/production-ui-types";
import type {
  HabitOccurrenceRow,
  HabitRow,
  Relation,
  SubjectListItem,
} from "@/lib/production-ui-types";
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

export interface DashboardRead {
  primaryFocus: { title: string; subjectId: string; kind: "goal" };
  objectives: { id: string; title: string; subjectId?: string }[];
  dueAndOverdue: SubjectListItem[];
  nextActions: SubjectListItem[];
  inboxCount: number;
  todayHabits: HabitOccurrenceRow[];
  activeGoals: SubjectListItem[];
  activeProjects: SubjectListItem[];
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

export function deriveDashboard(input: {
  subjects: SubjectListItem[];
  edges: AlignmentEdge[];
  inboxCount: number;
  habits: HabitRow[];
  occurrences: HabitOccurrenceRow[];
  today: string;
  primaryFocus?: { title: string; subjectId: string; kind: "goal" };
  objectives?: { id: string; title: string; subjectId?: string }[];
}): DashboardRead {
  const { subjects, edges, inboxCount, habits, occurrences, today } = input;
  const openTasks = subjects.filter(
    (x) => x.type === "Task" && !x.archived && !isTerminal(x.status),
  );
  const dueAndOverdue = openTasks
    .filter((x) => (x.due && x.due <= today) || x.scheduled === today)
    .sort((a, b) =>
      (a.due ?? a.scheduled ?? "").localeCompare(b.due ?? b.scheduled ?? ""),
    );
  const overdueIds = new Set(dueAndOverdue.map((x) => x.id));
  const nextActions = openTasks
    .filter((x) => !overdueIds.has(x.id) && x.status === "In progress")
    .slice(0, 4);

  const activeGoals = subjects.filter(
    (x) => x.type === "Goal" && x.status === "Active" && !x.archived,
  );
  const activeProjects = subjects.filter(
    (x) => x.type === "Project" && x.status === "Active" && !x.archived,
  );

  const primary =
    [...activeGoals].sort(
      (a, b) =>
        (a.targetDate ?? "9999").localeCompare(b.targetDate ?? "9999") ||
        a.title.localeCompare(b.title),
    )[0] ?? null;

  const childProjectIds = new Set(
    edges
      .filter((e) => primary && e.toId === primary.id && e.relation === "results_in")
      .map((e) => e.fromId),
  );
  const fromProjects = activeProjects.filter((p) => childProjectIds.has(p.id));
  const objectives = (fromProjects.length > 0 ? fromProjects : activeProjects)
    .slice(0, 3)
    .map((p) => ({ id: p.id, title: p.title, subjectId: p.id }));

  return {
    primaryFocus: input.primaryFocus ?? (primary
      ? { title: primary.title, subjectId: primary.id, kind: "goal" }
      : { title: "No active goal yet", subjectId: "", kind: "goal" }),
    objectives:
      input.objectives && input.objectives.length > 0 ? input.objectives : objectives,
    dueAndOverdue,
    nextActions,
    inboxCount,
    todayHabits: todayHabitRows(habits, occurrences, today),
    activeGoals,
    activeProjects,
  };
}

export function todayHabitRows(
  habits: HabitRow[],
  occurrences: HabitOccurrenceRow[],
  today: string,
): HabitOccurrenceRow[] {
  return habits
    .filter((h) => !h.archived)
    .map((h) => {
      const recorded = occurrences.find(
        (o) => o.habitId === h.id && o.occurrenceDate === today,
      );
      return (
        recorded ?? {
          habitId: h.id,
          habitUrn: h.urn,
          habitName: h.name,
          occurrenceDate: today,
          state: "unrecorded" as const,
          allowsPartial: h.allowsPartial,
        }
      );
    });
}

export function mapForestStatus(
  nodes: AlignmentNode[],
  subject: string,
  status: string,
): AlignmentNode[] {
  const match = (id: string, urn: string) => id === subject || urn === subject;
  const walk = (n: AlignmentNode): AlignmentNode => ({
    ...n,
    subject: match(n.subject.id, n.subject.urn)
      ? { ...n.subject, status }
      : n.subject,
    children: n.children.map(walk),
  });
  return nodes.map(walk);
}
