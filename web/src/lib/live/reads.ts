import { todayIso } from "@/lib/dates";
import {
  buildAlignmentForest,
  deriveDashboard,
  type AlignmentEdge,
} from "@/lib/derive";
import { api, ApiRequestError, unwrap } from "@/lib/live/http";
import { normalizeCoverSearch, withNormalCover } from "@/lib/cover";
import { repeatingSection } from "@/lib/review-recap";
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
import { mapSubjects, normalizeRepeat, normalizeSeriesRow } from "@/lib/task-series";
import {
  normalizeActivityDays,
  normalizeDayDetail,
  normalizeOuraStatus,
  normalizeSummary,
  normalizeTags,
  normalizeWorkouts,
} from "@/lib/activity";
import type {
  ActivityDayDetailRead,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivityTagsRead,
  ActivityWorkoutsRead,
  AreaRow,
  ArtifactRecord,
  DomainDaysRead,
  DomainReportRead,
  DomainsRead,
  BountyTypeDefaults,
  FoodDayRead,
  FoodDaysRead,
  MealRow,
  OuraStatus,
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
  TaskRepetitionRow,
  TaskSeriesHistory,
  TaskSeriesRow,
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

// An artifact's bytes never change, so each is fetched once a session and kept as an object
// URL: an <img> can't send the bearer token. A failed fetch is forgotten, so the next look
// tries again; one that isn't there is null.
const objectUrls = new Map<string, Promise<string | null>>();

function artifactObjectUrl(id: string): Promise<string | null> {
  const known = objectUrls.get(id);
  if (known) return known;
  const url = unwrap(api().GET("/api/artifacts/{id}/content", { params: { path: { id } }, parseAs: "blob" })).then(
    (blob) => URL.createObjectURL(blob as Blob),
    (error: unknown) => {
      if (error instanceof ApiRequestError && error.status === 404) return null;
      objectUrls.delete(id);
      throw error;
    },
  );
  objectUrls.set(id, url);
  return url;
}

export const liveReads: LifeOsReads = {
  async listSubjects(opts) {
    const data = await unwrap(
      api().GET("/api/subjects", {
        params: {
          query: {
            type: opts?.type,
            includeArchived: opts?.includeArchived,
            includeSeries: opts?.includeSeries,
          },
        },
      }),
    );
    return asType<SubjectListItem[]>(data).map((s) => withNormalCover(normalizeListBounty(s)));
  },

  async getSubject(id) {
    const { data, response } = await api().GET("/api/subjects/{id}", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    const detail = withNormalCover(normalizeDetailBounty(asType<SubjectDetail>(data)));
    return { ...detail, repeating: normalizeRepeat(detail.repeating) };
  },

  async getListItem(id) {
    const { data, response } = await api().GET("/api/subjects/{id}/list-item", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    return withNormalCover(normalizeListBounty(asType<SubjectListItem>(data)));
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
    return artifactObjectUrl(id);
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

  async taskSeries(opts) {
    const data = await unwrap(
      api().GET("/api/tasks/series", { params: { query: { includeArchived: opts?.includeArchived } } }),
    );
    return asType<TaskSeriesRow[]>(data).map(normalizeSeriesRow);
  },

  async taskSeriesHistory(id) {
    const { data, response } = await api().GET("/api/tasks/series/{id}", { params: { path: { id } } });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    const read = asType<TaskSeriesHistory>(data);
    return {
      series: normalizeSeriesRow(read.series),
      repetitions: asType<TaskRepetitionRow[]>(read.repetitions ?? []),
    };
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

  async activitySummary() {
    const data = await unwrap(api().GET("/api/activity/summary"));
    return normalizeSummary(asType<ActivitySummaryRead>(data));
  },

  async activityDays(from, to) {
    const data = await unwrap(api().GET("/api/activity/days", { params: { query: { from, to } } }));
    return normalizeActivityDays(asType<ActivityDaysRead>(data));
  },

  async activityWorkouts(from, to) {
    const data = await unwrap(api().GET("/api/activity/workouts", { params: { query: { from, to } } }));
    return normalizeWorkouts(asType<ActivityWorkoutsRead>(data));
  },

  async activityTags(from, to) {
    const data = await unwrap(api().GET("/api/activity/tags", { params: { query: { from, to } } }));
    return normalizeTags(asType<ActivityTagsRead>(data));
  },

  async ouraStatus() {
    return normalizeOuraStatus(asType<OuraStatus>(await unwrap(api().GET("/api/oura/status"))));
  },

  async activityDay(date) {
    const data = await unwrap(api().GET("/api/activity/day", { params: { query: { date } } }));
    return normalizeDayDetail(asType<ActivityDayDetailRead>(data));
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

  async coverSearch(query, page = 1) {
    const data = await unwrap(api().GET("/api/covers/search", { params: { query: { q: query, page } } }));
    return normalizeCoverSearch(data);
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

  async recap(opts) {
    // TODO(api): the rest of the recap is a composed read over the day's/week's events. The
    // repeating Tasks' section is made here from their histories (TASKS-3).
    const series = await this.taskSeries({});
    const histories = await Promise.all(series.map((row) => this.taskSeriesHistory(row.id)));
    const section = repeatingSection(
      histories.filter((h): h is TaskSeriesHistory => h !== null),
      opts.start,
      opts.end,
      opts.asOf,
    );
    return section.items.length > 0 ? [section] : [];
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
      this.listSubjects({ includeArchived, includeSeries: true }),
      this.edges({ includeArchived }),
    ]);
    // A repeating Task is one node, its series; its repetitions stay off the Map (TASKS-3).
    return buildAlignmentForest(mapSubjects(subjects), edges, opts);
  },
};
