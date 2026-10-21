import { todayIso } from "@/lib/dates";
import {
  buildAlignmentForest,
  deriveDashboard,
  type AlignmentEdge,
} from "@/lib/derive";
import { api, unwrap } from "@/lib/live/http";
import type { RecapSection } from "@/lib/review-recap";
import {
  normalizeBountyDefaults,
  normalizeDetailBounty,
  normalizeHabit,
  normalizeLedgerDay,
  normalizeLedgerRow,
  normalizeListBounty,
  normalizeReward,
  normalizeWallet,
} from "@/lib/rewards";
import type { LifeOsReads } from "@/lib/mock/api";
import { normalizeDomainDays, normalizeDomainReport, normalizeDomains } from "@/lib/domains";
import { normalizeFoodDay, normalizeFoodDays, normalizeMeal } from "@/lib/food";
import type {
  AreaRow,
  ArtifactRecord,
  DomainDaysRead,
  DomainReportRead,
  DomainsRead,
  BountyTypeDefaults,
  FoodDayRead,
  FoodDaysRead,
  MealRow,
  HabitOccurrenceRow,
  HabitRow,
  InboxItem,
  JournalEntry,
  LedgerDay,
  LedgerRow,
  PersonRow,
  RelationEdge,
  ResolvedInboxItem,
  ReviewDoc,
  RewardRow,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
  TaskTimer,
  Wallet,
} from "@/lib/production-ui-types";

function asType<T>(value: unknown): T {
  return value as T;
}

/** The API types int64 as `number | string`; the timer maths needs numbers. */
export function normalizeTimer(timer: TaskTimer): TaskTimer {
  return {
    ...timer,
    totalSeconds: Number(timer.totalSeconds) || 0,
    sessionSeconds: Number(timer.sessionSeconds) || 0,
    sessions: Number(timer.sessions) || 0,
  };
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
    return asType<SubjectListItem[]>(data).map(normalizeListBounty);
  },

  async getSubject(id) {
    const { data, response } = await api().GET("/api/subjects/{id}", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    return normalizeDetailBounty(asType<SubjectDetail>(data));
  },

  async getListItem(id) {
    const { data, response } = await api().GET("/api/subjects/{id}/list-item", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    return normalizeListBounty(asType<SubjectListItem>(data));
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

  async inboxResolved() {
    const data = await unwrap(api().GET("/api/inbox/resolved"));
    return asType<ResolvedInboxItem[]>(data);
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
    return asType<HabitRow[]>(data).map(normalizeHabit);
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

  async timers() {
    const data = await unwrap(api().GET("/api/timers"));
    return asType<TaskTimer[]>(data).map(normalizeTimer);
  },

  async wallet() {
    const data = await unwrap(api().GET("/api/wallet"));
    return normalizeWallet(asType<Wallet>(data));
  },

  async domains() {
    const data = await unwrap(api().GET("/api/domains"));
    return normalizeDomains(asType<DomainsRead>(data));
  },

  async domainDays(domain, days) {
    const data = await unwrap(
      api().GET("/api/domains/{key}/days", { params: { path: { key: domain }, query: { days } } }),
    );
    return normalizeDomainDays(asType<DomainDaysRead>(data));
  },

  async domainReport(days) {
    const data = await unwrap(api().GET("/api/domains/report", { params: { query: { days } } }));
    return normalizeDomainReport(asType<DomainReportRead>(data));
  },

  async foodDay(date) {
    const data = await unwrap(api().GET("/api/food/day", { params: { query: { date } } }));
    return normalizeFoodDay(asType<FoodDayRead>(data));
  },

  async foodDays(from, to) {
    const data = await unwrap(api().GET("/api/food/days", { params: { query: { from, to } } }));
    return normalizeFoodDays(asType<FoodDaysRead>(data));
  },

  async foodRecent(limit) {
    const data = await unwrap(api().GET("/api/food/recent", { params: { query: { limit } } }));
    return asType<MealRow[]>(data).map(normalizeMeal);
  },

  async ledger(opts) {
    const data = await unwrap(
      api().GET("/api/ledger", {
        params: { query: { from: opts?.from, to: opts?.to, limit: opts?.limit } },
      }),
    );
    return asType<LedgerRow[]>(data).map(normalizeLedgerRow);
  },

  async ledgerDaily(opts) {
    const data = await unwrap(
      api().GET("/api/ledger/daily", {
        params: { query: { from: opts.from, to: opts.to, tz: opts.tz } },
      }),
    );
    return asType<LedgerDay[]>(data).map(normalizeLedgerDay);
  },

  async rewards(opts) {
    const data = await unwrap(
      api().GET("/api/rewards", {
        params: { query: { includeArchived: opts?.includeArchived } },
      }),
    );
    return asType<RewardRow[]>(data).map(normalizeReward);
  },

  async bountyDefaults() {
    const data = await unwrap(api().GET("/api/bounty/defaults"));
    return normalizeBountyDefaults(asType<BountyTypeDefaults[]>(data));
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
