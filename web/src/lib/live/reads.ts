import { todayIso } from "@/lib/dates";
import {
  buildAlignmentForest,
  deriveDashboard,
  type AlignmentEdge,
} from "@/lib/derive";
import { api, unwrap } from "@/lib/live/http";
import type { RecapSection } from "@/lib/review-recap";
import type { LifeOsReads } from "@/lib/mock/api";
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

function asType<T>(value: unknown): T {
  return value as T;
}

export const liveReads: LifeOsReads = {
  async listSubjects(opts) {
    const data = await unwrap(
      api().GET("/api/subjects", {
        params: {
          query: {
            type: opts?.type,
            includeArchived: opts?.includeArchived,
          },
        },
      }),
    );
    return asType<SubjectListItem[]>(data);
  },

  async getSubject(id) {
    const { data, response } = await api().GET("/api/subjects/{id}", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    return asType<SubjectDetail>(data);
  },

  async getListItem(id) {
    const { data, response } = await api().GET("/api/subjects/{id}/list-item", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    return asType<SubjectListItem>(data);
  },

  async relations(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/relations", { params: { path: { id } } }),
    );
    return asType<{ parents: RelationEdge[]; children: RelationEdge[] }>(data);
  },

  async tags(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/tags", { params: { path: { id } } }),
    );
    return asType<string[]>(data);
  },

  async tagUniverse() {
    const data = await unwrap(api().GET("/api/tags"));
    return asType<TagUniverseItem[]>(data);
  },

  async journal(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/journal", { params: { path: { id } } }),
    );
    return asType<JournalEntry[]>(data);
  },

  async history(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/history", { params: { path: { id } } }),
    );
    return asType<StatusHistoryEntry[]>(data);
  },

  async inbox() {
    const data = await unwrap(api().GET("/api/inbox"));
    return asType<InboxItem[]>(data);
  },

  async artifact(_id) {
    // TODO(api): GET /api/artifacts/{id} — metadata from `bsk.v_artifact`.
    void _id;
    return asType<ArtifactRecord | null>(null);
  },

  async artifactBytesUrl(id) {
    // TODO(api): stream from the kernel's bytes-fetch / `bsk artifact get` export path.
    void id;
    return null;
  },

  async subjectFiles(_subjectId) {
    // TODO(api): GET /api/subjects/{id}/files — artifacts related onto the subject.
    void _subjectId;
    return asType<ArtifactRecord[]>([]);
  },

  async areas() {
    const data = await unwrap(api().GET("/api/areas"));
    return asType<AreaRow[]>(data);
  },

  async people() {
    const data = await unwrap(api().GET("/api/people"));
    return asType<PersonRow[]>(data);
  },

  async habits() {
    const data = await unwrap(api().GET("/api/habits"));
    return asType<HabitRow[]>(data);
  },

  async occurrences(opts) {
    const data = await unwrap(
      api().GET("/api/habits/occurrences", {
        params: {
          query: {
            habitId: opts?.habitId,
            on: opts?.on,
          },
        },
      }),
    );
    return asType<HabitOccurrenceRow[]>(data);
  },

  async reviews(opts) {
    // TODO(api): Review documents (D3) are not on the live API yet.
    void opts;
    return asType<ReviewDoc[]>([]);
  },

  async recap() {
    // TODO(api): recap is a composed read over the day's/week's events.
    return asType<RecapSection[]>([]);
  },

  async edges(opts) {
    // TODO(api): GET /api/edges is the bulk alignment-edge read for BROWSE-1.
    // Confirm it returns serves / results_in / supersedes for every subject (not a
    // per-subject walk). Do not add a second graph-specific store.
    const data = await unwrap(
      api().GET("/api/edges", {
        params: { query: { includeArchived: opts?.includeArchived } },
      }),
    );
    return asType<AlignmentEdge[]>(data);
  },

  async dashboard() {
    const today = todayIso();
    const [subjects, edges, inbox, habits, occurrences] = await Promise.all([
      this.listSubjects(),
      this.edges(),
      this.inbox(),
      this.habits(),
      this.occurrences({ on: today }),
    ]);
    return deriveDashboard({
      subjects,
      edges,
      inboxCount: inbox.length,
      habits,
      occurrences,
      today,
    });
  },

  async alignmentForest(opts) {
    const includeArchived = opts?.includeArchived ?? false;
    const [subjects, edges] = await Promise.all([
      this.listSubjects({ includeArchived }),
      this.edges({ includeArchived }),
    ]);
    return buildAlignmentForest(subjects, edges, opts);
  },
};
