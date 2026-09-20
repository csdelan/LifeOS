/**
 * Mock LifeOS read surface.
 *
 * This is the swap seam: replace these fetchers with the OpenAPI-generated
 * LifeOs.Api client. Return shapes stay exactly the types in
 * `production-ui-types.ts`. Writes go through `LifeOsWriteClient`.
 */

import { todayIso } from "@/lib/dates";
import { isTerminal } from "@/lib/production-ui-types";
import type {
  AreaRow,
  HabitOccurrenceRow,
  HabitRow,
  InboxItem,
  JournalEntry,
  PersonRow,
  RelationEdge,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
} from "@/lib/production-ui-types";
import { tagUniverse, toRelationEdge } from "@/lib/mock/seed";
import { getState as storeState } from "@/lib/mock/store";
import { ALIGNMENT_TYPES } from "@/lib/subject-meta";

const wait = (ms = 80) => new Promise((r) => setTimeout(r, ms));

export interface SubjectRelations {
  parents: RelationEdge[];
  children: RelationEdge[];
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

export type MapLens = "all" | "Goal" | "Project" | "Task";

export interface AlignmentNode {
  id: string;
  instanceKey: string;
  subject: SubjectListItem;
  relationToParent: RelationEdge["relation"] | null;
  parentId: string | null;
  extraParents: { id: string; title: string; type: SubjectListItem["type"] }[];
  children: AlignmentNode[];
  depth: number;
}

function listOf(type?: SubjectListItem["type"], includeArchived = false): SubjectListItem[] {
  return storeState().subjects.filter((s) => {
    if (!includeArchived && s.archived) return false;
    if (type && s.type !== type) return false;
    return true;
  });
}

export const lifeOsReads = {
  async listSubjects(opts?: {
    type?: SubjectListItem["type"];
    includeArchived?: boolean;
  }): Promise<SubjectListItem[]> {
    await wait();
    return listOf(opts?.type, opts?.includeArchived);
  },

  async getSubject(id: string): Promise<SubjectDetail | null> {
    await wait();
    return storeState().details[id] ?? null;
  },

  async getListItem(id: string): Promise<SubjectListItem | null> {
    await wait();
    return storeState().subjects.find((s) => s.id === id) ?? null;
  },

  async relations(id: string): Promise<SubjectRelations> {
    await wait();
    const { edges, subjects } = storeState();
    const by = Object.fromEntries(subjects.map((s) => [s.id, s]));
    const parents: RelationEdge[] = [];
    const children: RelationEdge[] = [];
    for (const e of edges) {
      if (e.fromId === id && by[e.toId]) {
        parents.push(toRelationEdge(by[e.toId], e.relation));
      }
      if (e.toId === id && by[e.fromId]) {
        children.push(toRelationEdge(by[e.fromId], e.relation));
      }
    }
    return { parents, children };
  },

  async tags(id: string): Promise<string[]> {
    await wait();
    return storeState().tagsByItem[id] ?? [];
  },

  async tagUniverse(): Promise<TagUniverseItem[]> {
    await wait();
    return tagUniverse(storeState());
  },

  async journal(id: string): Promise<JournalEntry[]> {
    await wait();
    return storeState().journals[id] ?? [];
  },

  async history(id: string): Promise<StatusHistoryEntry[]> {
    await wait();
    return storeState().history[id] ?? [];
  },

  async inbox(): Promise<InboxItem[]> {
    await wait();
    return [...storeState().inbox].sort((a, b) =>
      b.triagedAt.localeCompare(a.triagedAt),
    );
  },

  async areas(): Promise<AreaRow[]> {
    await wait();
    return storeState().areas;
  },

  async people(): Promise<PersonRow[]> {
    await wait();
    return storeState().people;
  },

  async habits(): Promise<HabitRow[]> {
    await wait();
    return storeState().habits;
  },

  async dashboard(): Promise<DashboardRead> {
    await wait();
    const s = storeState();
    const today = todayIso();
    const openTasks = s.subjects.filter(
      (x) => x.type === "Task" && !x.archived && !isTerminal(x.status),
    );
    const dueAndOverdue = openTasks
      .filter((x) => (x.due && x.due <= today) || x.scheduled === today)
      .sort((a, b) => (a.due ?? a.scheduled ?? "").localeCompare(b.due ?? b.scheduled ?? ""));
    const overdueIds = new Set(dueAndOverdue.map((x) => x.id));
    const nextActions = openTasks
      .filter((x) => !overdueIds.has(x.id) && x.status === "In progress")
      .slice(0, 4);
    return {
      primaryFocus: s.primaryFocus,
      objectives: s.objectives,
      dueAndOverdue,
      nextActions,
      inboxCount: s.inbox.length,
      todayHabits: s.occurrences.filter((o) => o.occurrenceDate === today),
      activeGoals: s.subjects.filter(
        (x) => x.type === "Goal" && x.status === "Active" && !x.archived,
      ),
      activeProjects: s.subjects.filter(
        (x) => x.type === "Project" && x.status === "Active" && !x.archived,
      ),
    };
  },

  async alignmentForest(opts?: {
    includeArchived?: boolean;
    lens?: MapLens;
  }): Promise<AlignmentNode[]> {
    await wait();
    return buildForest(opts?.includeArchived ?? false, opts?.lens ?? "all");
  },
};

function buildForest(includeArchived: boolean, lens: MapLens): AlignmentNode[] {
  const s = storeState();
  const subjects = s.subjects.filter((x) => includeArchived || !x.archived);
  const by = new Map(subjects.map((x) => [x.id, x]));
  const childrenOf = new Map<string, { childId: string; relation: RelationEdge["relation"] }[]>();
  const parentsOf = new Map<string, { parentId: string; relation: RelationEdge["relation"] }[]>();

  for (const e of s.edges) {
    if (!by.has(e.fromId) || !by.has(e.toId)) continue;
    const kids = childrenOf.get(e.toId) ?? [];
    kids.push({ childId: e.fromId, relation: e.relation });
    childrenOf.set(e.toId, kids);
    const pars = parentsOf.get(e.fromId) ?? [];
    pars.push({ parentId: e.toId, relation: e.relation });
    parentsOf.set(e.fromId, pars);
  }

  const childIds = new Set(s.edges.filter((e) => by.has(e.fromId) && by.has(e.toId)).map((e) => e.fromId));

  const roots = subjects.filter((x) => {
    if (x.type === "Value") return true;
    if (ALIGNMENT_TYPES.includes(x.type) && !childIds.has(x.id)) return true;
    return false;
  });

  // Keep Values first, then unaligned work.
  roots.sort((a, b) => {
    const rank = (t: SubjectListItem) =>
      t.type === "Value" ? 0 : t.type === "Goal" ? 1 : t.type === "Project" ? 2 : 3;
    return rank(a) - rank(b) || a.title.localeCompare(b.title);
  });

  const walk = (
    id: string,
    parentId: string | null,
    relation: RelationEdge["relation"] | null,
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
