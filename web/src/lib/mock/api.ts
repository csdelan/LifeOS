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
  ArtifactRecord,
  HabitOccurrenceRow,
  HabitRow,
  InboxItem,
  JournalEntry,
  PersonRow,
  RelationEdge,
  ReviewDoc,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
} from "@/lib/production-ui-types";
import { composeRecap, type RecapSection } from "@/lib/review-recap";
import { tagUniverse, toRelationEdge } from "@/lib/mock/seed";
import {
  ensureReview,
  ensureReviewDocs,
  filesForSubject,
  getArtifact,
  getState as storeState,
} from "@/lib/mock/store";

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
  /**
   * Artifact metadata (no payload).
   * // TODO(api): GET /api/artifacts/{id} from `bsk.v_artifact`.
   */
  artifact(id: string): Promise<ArtifactRecord | null>;
  /**
   * URL that streams the managed bytes.
   * Mock: the retained object URL. // TODO(api): kernel bytes-fetch / `bsk artifact get`.
   */
  artifactBytesUrl(id: string): Promise<string | null>;
  /**
   * Binary artifacts related onto a subject (the Files section).
   * // TODO(api): GET /api/subjects/{id}/files
   */
  subjectFiles(subjectId: string): Promise<ArtifactRecord[]>;
  areas(): Promise<AreaRow[]>;
  people(): Promise<PersonRow[]>;
  habits(): Promise<HabitRow[]>;
  occurrences(opts?: { habitId?: string; on?: string }): Promise<HabitOccurrenceRow[]>;
  reviews(opts?: { kind?: "daily" | "weekly"; date?: string }): Promise<ReviewDoc[]>;
  recap(opts: { start: string; end: string; asOf: string }): Promise<RecapSection[]>;
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

  async artifact(id: string): Promise<ArtifactRecord | null> {
    await wait();
    return getArtifact(id) ?? null;
  },

  async artifactBytesUrl(id: string): Promise<string | null> {
    await wait();
    // TODO(api): stream from the kernel's bytes-fetch / export path.
    return getArtifact(id)?.bytesUrl ?? null;
  },

  async subjectFiles(subjectId: string): Promise<ArtifactRecord[]> {
    await wait();
    return filesForSubject(subjectId);
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

  async reviews(opts?: { kind?: "daily" | "weekly"; date?: string }): Promise<ReviewDoc[]> {
    await wait();
    ensureReviewDocs();
    if (opts?.kind && opts?.date) ensureReview(opts.kind, opts.date);
    return storeState().reviews;
  },

  async recap(opts: { start: string; end: string; asOf: string }): Promise<RecapSection[]> {
    await wait();
    const s = storeState();
    return composeRecap({
      start: opts.start,
      end: opts.end,
      asOf: opts.asOf,
      subjects: s.subjects,
      history: s.history,
      occurrences: s.occurrences,
      inbox: s.inbox,
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
