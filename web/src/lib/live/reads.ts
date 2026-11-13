import { todayIso } from "@/lib/dates";
import { deriveDashboard, type AlignmentEdge } from "@/lib/derive";
import { fits, type ArtifactAppSide } from "@/lib/live/fits";
import { api, ApiRequestError, unwrap } from "@/lib/live/http";
import { normalizeCoverSearch, withNormalCover } from "@/lib/cover";
import { repeatingSection, type RecapSection } from "@/lib/review-recap";
import { normalizeReview } from "@/lib/reviews";
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
import type { LifeOsReads } from "@/lib/reads";
import type { SubjectSearch } from "@/lib/browse";
import { normalizeDomainDays, normalizeDomainReport, normalizeDomains, normalizeExceptionDays } from "@/lib/domains";
import { normalizeFoodDay, normalizeFoodDays, normalizeMeal } from "@/lib/food";
import { normalizeRepeat, normalizeSeriesRow, seriesFromDue } from "@/lib/task-series";
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
  AppointmentRow,
  AreaRow,
  DomainDaysRead,
  DomainReportRead,
  DomainsRead,
  ExceptionDaysRead,
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
  RewardRow,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
  SeriesRepetitionDue,
  TaskRepetitionRow,
  TaskSeriesHistory,
  TaskSeriesRow,
  TaskTimer,
  Wallet,
  EnvironmentRead,
  HomeDaysRead,
  SpacePhotosRead,
  SpaceRatingsRead,
  SpaceRow,
  LabVisitsRead,
  MetricDetailRead,
  MetricRow,
  MetricTemplate,
} from "@/lib/production-ui-types";
import {
  normalizeEnvironment,
  normalizeHomeDays,
  normalizeSpace,
  normalizeSpacePhotos,
  normalizeSpaceRatings,
} from "@/lib/environment";
import { normalizeLabVisits } from "@/lib/labs";
import { normalizeMetric, normalizeMetricDetail } from "@/lib/measurements";
import { normalizeArtifact } from "@/lib/artifacts";
/** An Inbox row with its file's metadata in numbers (CAP-2 / CAP-4); null when it has none. */
function withNormalAttachment<T extends InboxItem>(item: T): T {
  return { ...item, attachment: item.attachment ? normalizeArtifact(item.attachment) : null };
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
            status: opts?.status,
            limit: opts?.limit,
          },
        },
      }),
    );
    return fits<SubjectListItem[]>()(data).map((s) => withNormalCover(normalizeListBounty(s)));
  },

  async searchSubjects(query, opts) {
    const data = await unwrap(
      api().GET("/api/subjects/search", {
        params: { query: { q: query, types: opts?.types ? [...opts.types] : undefined, limit: opts?.limit } },
      }),
    );
    const found = fits<SubjectSearch>()(data);
    return { ...found, matches: found.matches.map((s) => withNormalCover(normalizeListBounty(s))) };
  },

  async inboxCount() {
    return fits<{ count: number }>()(await unwrap(api().GET("/api/inbox/count"))).count;
  },

  async getSubject(id) {
    const { data, response } = await api().GET("/api/subjects/{id}", {
      params: { path: { id } },
    });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    const detail = withNormalCover(normalizeDetailBounty(fits<SubjectDetail>()(data)));
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
    return withNormalCover(normalizeListBounty(fits<SubjectListItem>()(data)));
  },

  async relations(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/relations", { params: { path: { id } } }),
    );
    return fits<{ parents: RelationEdge[]; children: RelationEdge[] }>()(data);
  },

  async tags(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/tags", { params: { path: { id } } }),
    );
    return fits<string[]>()(data);
  },

  async tagUniverse() {
    const data = await unwrap(api().GET("/api/tags"));
    return fits<TagUniverseItem[]>()(data);
  },

  async journal(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/journal", { params: { path: { id } } }),
    );
    return fits<JournalEntry[]>()(data);
  },

  async history(id) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/history", { params: { path: { id } } }),
    );
    return fits<StatusHistoryEntry[]>()(data);
  },

  async inbox() {
    const data = await unwrap(api().GET("/api/inbox"));
    return fits<InboxItem[], ArtifactAppSide>()(data).map(withNormalAttachment);
  },

  async inboxResolved(limit) {
    const data = await unwrap(api().GET("/api/inbox/resolved", { params: { query: { limit } } }));
    return fits<ResolvedInboxItem[], ArtifactAppSide>()(data).map(withNormalAttachment);
  },

  async artifact(id) {
    const { data, response } = await api().GET("/api/artifacts/{id}", { params: { path: { id } } });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) throw new ApiRequestError(`API ${response.status}`, response.status);
    return normalizeArtifact(data);
  },

  async artifactBytesUrl(id) {
    return artifactObjectUrl(id);
  },

  async subjectFiles(subjectId) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/files", { params: { path: { id: subjectId } } }),
    );
    return (data as unknown[]).map(normalizeArtifact);
  },

  async areas() {
    const data = await unwrap(api().GET("/api/areas"));
    return fits<AreaRow[]>()(data);
  },

  async people() {
    const data = await unwrap(api().GET("/api/people"));
    return fits<PersonRow[]>()(data);
  },

  async habits() {
    const data = await unwrap(api().GET("/api/habits"));
    return fits<HabitRow[]>()(data).map(normalizeHabit);
  },

  async appointments(opts) {
    const data = await unwrap(api().GET("/api/appointments", { params: { query: { on: opts.on } } }));
    return fits<AppointmentRow[]>()(data);
  },

  async occurrences(opts) {
    const data = await unwrap(
      api().GET("/api/habits/occurrences", {
        params: {
          query: {
            habitId: opts?.habitId,
            on: opts?.on,
            from: opts?.from,
            to: opts?.to,
          },
        },
      }),
    );
    return fits<HabitOccurrenceRow[]>()(data);
  },

  async timers() {
    const data = await unwrap(api().GET("/api/timers"));
    return fits<TaskTimer[]>()(data).map(normalizeTimer);
  },

  async taskSeries(opts) {
    const data = await unwrap(
      api().GET("/api/tasks/series", { params: { query: { includeArchived: opts?.includeArchived } } }),
    );
    return fits<TaskSeriesRow[]>()(data).map(normalizeSeriesRow);
  },

  async taskSeriesHistory(id) {
    const { data, response } = await api().GET("/api/tasks/series/{id}", { params: { path: { id } } });
    if (response.status === 404) return null;
    if (!response.ok || data === undefined) {
      throw new Error(`API ${response.status}`);
    }
    const read = fits<TaskSeriesHistory>()(data);
    return {
      series: normalizeSeriesRow(read.series),
      repetitions: fits<TaskRepetitionRow[]>()(read.repetitions ?? []),
    };
  },

  async wallet() {
    const data = await unwrap(api().GET("/api/wallet"));
    return normalizeWallet(fits<Wallet>()(data));
  },

  async domains() {
    const data = await unwrap(api().GET("/api/domains"));
    return normalizeDomains(fits<DomainsRead>()(data));
  },

  async domainDays(domain, days) {
    const data = await unwrap(
      api().GET("/api/domains/{key}/days", { params: { path: { key: domain }, query: { days } } }),
    );
    return normalizeDomainDays(fits<DomainDaysRead>()(data));
  },

  async domainReport(days) {
    const data = await unwrap(api().GET("/api/domains/report", { params: { query: { days } } }));
    return normalizeDomainReport(fits<DomainReportRead>()(data));
  },

  async exceptionDays(days) {
    const data = await unwrap(api().GET("/api/domains/exceptions", { params: { query: { days } } }));
    return normalizeExceptionDays(fits<ExceptionDaysRead>()(data));
  },

  async foodDay(date) {
    const data = await unwrap(api().GET("/api/food/day", { params: { query: { date } } }));
    return normalizeFoodDay(fits<FoodDayRead>()(data));
  },

  async foodDays(from, to) {
    const data = await unwrap(api().GET("/api/food/days", { params: { query: { from, to } } }));
    return normalizeFoodDays(fits<FoodDaysRead>()(data));
  },

  async foodRecent(limit) {
    const data = await unwrap(api().GET("/api/food/recent", { params: { query: { limit } } }));
    return fits<MealRow[]>()(data).map(normalizeMeal);
  },

  async activitySummary() {
    const data = await unwrap(api().GET("/api/activity/summary"));
    return normalizeSummary(fits<ActivitySummaryRead>()(data));
  },

  async activityDays(from, to) {
    const data = await unwrap(api().GET("/api/activity/days", { params: { query: { from, to } } }));
    return normalizeActivityDays(fits<ActivityDaysRead>()(data));
  },

  async activityWorkouts(from, to) {
    const data = await unwrap(api().GET("/api/activity/workouts", { params: { query: { from, to } } }));
    return normalizeWorkouts(fits<ActivityWorkoutsRead>()(data));
  },

  async activityTags(from, to) {
    const data = await unwrap(api().GET("/api/activity/tags", { params: { query: { from, to } } }));
    return normalizeTags(fits<ActivityTagsRead>()(data));
  },

  async ouraStatus() {
    return normalizeOuraStatus(fits<OuraStatus>()(await unwrap(api().GET("/api/oura/status"))));
  },

  async environment() {
    return normalizeEnvironment(fits<EnvironmentRead>()(await unwrap(api().GET("/api/environment"))));
  },

  async environmentDays(days) {
    const data = await unwrap(api().GET("/api/environment/days", { params: { query: { days } } }));
    return normalizeHomeDays(fits<HomeDaysRead>()(data));
  },

  async spaces() {
    return fits<SpaceRow[]>()(await unwrap(api().GET("/api/environment/spaces"))).map(normalizeSpace);
  },

  async spaceRatings(opts) {
    const data = await unwrap(
      api().GET("/api/environment/ratings", { params: { query: { space: opts?.space, days: opts?.days } } }),
    );
    return normalizeSpaceRatings(fits<SpaceRatingsRead>()(data));
  },

  async spacePhotos(space, shot) {
    const data = await unwrap(
      api().GET("/api/environment/spaces/{id}/photos", { params: { path: { id: space }, query: { shot } } }),
    );
    return normalizeSpacePhotos(fits<SpacePhotosRead>()(data));
  },

  async metricCatalog() {
    return fits<MetricTemplate[]>()(await unwrap(api().GET("/api/measurements/catalog")));
  },

  async metrics() {
    return fits<MetricRow[]>()(await unwrap(api().GET("/api/measurements/metrics"))).map(normalizeMetric);
  },

  async metric(metric, days) {
    const data = await unwrap(
      api().GET("/api/measurements/metrics/{id}", {
        params: { path: { id: metric }, query: { days: days ?? undefined } },
      }),
    );
    return normalizeMetricDetail(fits<MetricDetailRead>()(data));
  },

  async labVisits() {
    return normalizeLabVisits(fits<LabVisitsRead>()(await unwrap(api().GET("/api/measurements/visits"))));
  },

  async activityDay(date) {
    const data = await unwrap(api().GET("/api/activity/day", { params: { query: { date } } }));
    return normalizeDayDetail(fits<ActivityDayDetailRead>()(data));
  },

  async ledger(opts) {
    const data = await unwrap(
      api().GET("/api/ledger", {
        params: { query: { from: opts?.from, to: opts?.to, limit: opts?.limit } },
      }),
    );
    return fits<LedgerRow[]>()(data).map(normalizeLedgerRow);
  },

  async ledgerDaily(opts) {
    const data = await unwrap(
      api().GET("/api/ledger/daily", {
        params: { query: { from: opts.from, to: opts.to, tz: opts.tz } },
      }),
    );
    return fits<LedgerDay[]>()(data).map(normalizeLedgerDay);
  },

  async rewards(opts) {
    const data = await unwrap(
      api().GET("/api/rewards", {
        params: { query: { includeArchived: opts?.includeArchived } },
      }),
    );
    return fits<RewardRow[]>()(data).map(normalizeReward);
  },

  async coverSearch(query, page = 1) {
    const data = await unwrap(api().GET("/api/covers/search", { params: { query: { q: query, page } } }));
    return normalizeCoverSearch(data);
  },

  async bountyDefaults() {
    const data = await unwrap(api().GET("/api/bounty/defaults"));
    return normalizeBountyDefaults(fits<BountyTypeDefaults[]>()(data));
  },

  async reviews(opts) {
    const data = await unwrap(
      api().GET("/api/reviews", {
        params: { query: { kind: opts?.kind, from: opts?.from, to: opts?.to } },
      }),
    );
    return fits<unknown[]>()(data).map(normalizeReview);
  },

  async recap(opts) {
    // The server reads the recorded days in one call, and the repetitions due in the period in
    // another (code review CR54: not every series' whole history); the repeating Tasks' section is
    // made here from those (TASKS-3), after the overdue Tasks, as the mock places it.
    const [sections, due] = await Promise.all([
      unwrap(
        api().GET("/api/reviews/recap", {
          params: { query: { from: opts.start, to: opts.end, asOf: opts.asOf } },
        }),
      ),
      unwrap(
        api().GET("/api/tasks/series/due", { params: { query: { from: opts.start, to: opts.end } } }),
      ),
    ]);
    const repeating = repeatingSection(
      seriesFromDue(fits<SeriesRepetitionDue[]>()(due)),
      opts.start,
      opts.end,
      opts.asOf,
    );
    const recorded: RecapSection[] = fits<RecapSection[]>()(sections).map((section) => ({
      ...section,
      items: (section.items ?? []).map((item) => ({
        ...item,
        detail: item.detail ?? undefined,
        subjectId: item.subjectId ?? undefined,
      })),
    }));
    const at = recorded.findIndex((section) => section.id === "overdue");
    recorded.splice(at < 0 ? recorded.length : at + 1, 0, repeating);
    return recorded;
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
    return fits<AlignmentEdge[]>()(data);
  },

  async dashboard() {
    const today = todayIso();
    // Focus shows open items only and the Inbox's size, so it reads just those (code review CR76).
    const [subjects, edges, inboxCount, habits, occurrences] = await Promise.all([
      this.listSubjects({ status: "open" }),
      this.edges(),
      this.inboxCount(),
      this.habits(),
      this.occurrences({ on: today }),
    ]);
    return deriveDashboard({
      subjects,
      edges,
      inboxCount,
      habits,
      occurrences,
      today,
    });
  },
};
