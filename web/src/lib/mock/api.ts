/**
 * Mock LifeOS read surface.
 *
 * This is the swap seam: `lib/client.ts` selects mock vs live from
 * `VITE_API_BASE_URL`. Return shapes stay the types in `production-ui-types.ts`.
 * Forest / dashboard composition lives in `lib/derive.ts` so it is shared with live.
 */

import { todayIso } from "@/lib/dates";
import {
  buildAlignmentForest,
  deriveDashboard,
  type AlignmentEdge,
  type DashboardRead,
  type MapLens,
} from "@/lib/derive";
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

export type { AlignmentEdge, AlignmentNode, DashboardRead, MapLens } from "@/lib/derive";

const wait = (ms = 80) => new Promise((r) => setTimeout(r, ms));

export interface SubjectRelations {
  parents: RelationEdge[];
  children: RelationEdge[];
}

function listOf(type?: SubjectListItem["type"], includeArchived = false): SubjectListItem[] {
  return storeState().subjects.filter((s) => {
    if (!includeArchived && s.archived) return false;
    if (type && s.type !== type) return false;
    return true;
  });
}

export interface LifeOsReads {
  listSubjects(opts?: {
    type?: SubjectListItem["type"];
    includeArchived?: boolean;
  }): Promise<SubjectListItem[]>;
  getSubject(id: string): Promise<SubjectDetail | null>;
  getListItem(id: string): Promise<SubjectListItem | null>;
  relations(id: string): Promise<SubjectRelations>;
  tags(id: string): Promise<string[]>;
  tagUniverse(): Promise<TagUniverseItem[]>;
  journal(id: string): Promise<JournalEntry[]>;
  history(id: string): Promise<StatusHistoryEntry[]>;
  inbox(): Promise<InboxItem[]>;
  areas(): Promise<AreaRow[]>;
  people(): Promise<PersonRow[]>;
  habits(): Promise<HabitRow[]>;
  occurrences(opts?: { habitId?: string; on?: string }): Promise<HabitOccurrenceRow[]>;
  /**
   * Bulk alignment edges (serves / results_in / supersedes) for Map outline + graph.
   * // TODO(api): live GET /api/edges — already sketched in OpenAPI; confirm it returns
   * the full graph (not per-subject) before relying on it in live mode.
   */
  edges(opts?: { includeArchived?: boolean }): Promise<AlignmentEdge[]>;
  dashboard(): Promise<DashboardRead>;
  alignmentForest(opts?: {
    includeArchived?: boolean;
    lens?: MapLens;
  }): Promise<import("@/lib/derive").AlignmentNode[]>;
}

export const mockReads: LifeOsReads = {
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

  async occurrences(opts?: { habitId?: string; on?: string }): Promise<HabitOccurrenceRow[]> {
    await wait();
    return storeState().occurrences.filter((o) => {
      if (opts?.habitId && o.habitId !== opts.habitId) return false;
      if (opts?.on && o.occurrenceDate !== opts.on) return false;
      return true;
    });
  },

  async edges(opts?: { includeArchived?: boolean }): Promise<AlignmentEdge[]> {
    await wait();
    const s = storeState();
    const allowed = new Set(
      s.subjects.filter((x) => opts?.includeArchived || !x.archived).map((x) => x.id),
    );
    return s.edges.filter((e) => allowed.has(e.fromId) && allowed.has(e.toId));
  },

  async dashboard(): Promise<DashboardRead> {
    await wait();
    const s = storeState();
    return deriveDashboard({
      subjects: s.subjects,
      edges: s.edges,
      inboxCount: s.inbox.length,
      habits: s.habits,
      occurrences: s.occurrences,
      today: todayIso(),
      primaryFocus: s.primaryFocus,
      objectives: s.objectives,
    });
  },

  async alignmentForest(opts?: {
    includeArchived?: boolean;
    lens?: MapLens;
  }) {
    await wait();
    const s = storeState();
    return buildAlignmentForest(s.subjects, s.edges, opts);
  },
};

/** @deprecated Prefer importing from `@/lib/client`. Kept so existing mock imports keep working. */
export const lifeOsReads = mockReads;
