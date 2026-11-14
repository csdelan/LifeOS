import { todayIso } from "@/lib/dates";
import { deriveDashboard, type AlignmentEdge } from "@/lib/derive";
import { fits, type ArtifactAppSide } from "@/lib/live/fits";
import { api, ApiRequestError, unwrap, unwrapBlob, unwrapOrNull } from "@/lib/live/http";
import { normalizeCoverSearch, withNormalCover } from "@/lib/cover";
import { repeatingSection, type RecapSection } from "@/lib/review-recap";
import type { LifeOsReads } from "@/lib/reads";
import { liveMeasurementReads } from "@/lib/features/measurements/reads";
import type { SubjectSearch } from "@/lib/browse";
import { seriesFromDue } from "@/lib/task-series";
import type {
  ActivityDayDetailRead,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivityTagsRead,
  ActivityWorkoutsRead,
  AppointmentRow,
  ArtifactRecord,
  CoverSearchRead,
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
  ReviewDoc,
  RewardRow,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
  SeriesRepetitionDue,
  TaskSeriesHistory,
  TaskSeriesRow,
  TaskTimer,
  Wallet,
  EnvironmentRead,
  HomeDaysRead,
  SpacePhotosRead,
  SpaceRatingsRead,
  SpaceRow,
} from "@/lib/production-ui-types";

// An artifact's bytes never change, so each is fetched once a session and kept as an object
// URL: an <img> can't send the bearer token. A failed fetch is forgotten, so the next look
// tries again; one that isn't there is null.
const objectUrls = new Map<string, Promise<string | null>>();

function artifactObjectUrl(id: string): Promise<string | null> {
  const known = objectUrls.get(id);
  if (known) return known;
  const url = unwrapBlob(api().GET("/api/artifacts/{id}/content", { params: { path: { id } }, parseAs: "blob" })).then(
    (blob) => URL.createObjectURL(blob),
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
  ...liveMeasurementReads,

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
    return fits<SubjectListItem[]>()(data).map(withNormalCover);
  },

  async searchSubjects(query, opts) {
    const data = await unwrap(
      api().GET("/api/subjects/search", {
        params: { query: { q: query, types: opts?.types ? [...opts.types] : undefined, limit: opts?.limit } },
      }),
    );
    const found = fits<SubjectSearch>()(data);
    return { ...found, matches: found.matches.map(withNormalCover) };
  },

  async inboxCount() {
    return fits<{ count: number }>()(await unwrap(api().GET("/api/inbox/count"))).count;
  },

  async getSubject(id) {
    const data = await unwrapOrNull(api().GET("/api/subjects/{id}", { params: { path: { id } } }));
    return data ? withNormalCover(fits<SubjectDetail>()(data)) : null;
  },

  async getListItem(id) {
    const data = await unwrapOrNull(api().GET("/api/subjects/{id}/list-item", { params: { path: { id } } }));
    return data ? withNormalCover(fits<SubjectListItem>()(data)) : null;
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
    return fits<InboxItem[], ArtifactAppSide>()(data);
  },

  async inboxResolved(limit) {
    const data = await unwrap(api().GET("/api/inbox/resolved", { params: { query: { limit } } }));
    return fits<ResolvedInboxItem[], ArtifactAppSide>()(data);
  },

  async artifact(id) {
    const data = await unwrapOrNull(api().GET("/api/artifacts/{id}", { params: { path: { id } } }));
    return data ? fits<ArtifactRecord, ArtifactAppSide>()(data) : null;
  },

  async artifactBytesUrl(id) {
    return artifactObjectUrl(id);
  },

  async subjectFiles(subjectId) {
    const data = await unwrap(
      api().GET("/api/subjects/{id}/files", { params: { path: { id: subjectId } } }),
    );
    return fits<ArtifactRecord[], ArtifactAppSide>()(data);
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
    return fits<HabitRow[]>()(data);
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
    return fits<TaskTimer[]>()(data);
  },

  async taskSeries(opts) {
    const data = await unwrap(
      api().GET("/api/tasks/series", { params: { query: { includeArchived: opts?.includeArchived } } }),
    );
    return fits<TaskSeriesRow[]>()(data);
  },

  async taskSeriesHistory(id) {
    const data = await unwrapOrNull(api().GET("/api/tasks/series/{id}", { params: { path: { id } } }));
    return data ? fits<TaskSeriesHistory>()(data) : null;
  },

  async wallet() {
    const data = await unwrap(api().GET("/api/wallet"));
    return fits<Wallet>()(data);
  },

  async domains() {
    const data = await unwrap(api().GET("/api/domains"));
    return fits<DomainsRead>()(data);
  },

  async domainDays(domain, days) {
    const data = await unwrap(
      api().GET("/api/domains/{key}/days", { params: { path: { key: domain }, query: { days } } }),
    );
    return fits<DomainDaysRead>()(data);
  },

  async domainReport(days) {
    const data = await unwrap(api().GET("/api/domains/report", { params: { query: { days } } }));
    return fits<DomainReportRead>()(data);
  },

  async exceptionDays(days) {
    const data = await unwrap(api().GET("/api/domains/exceptions", { params: { query: { days } } }));
    return fits<ExceptionDaysRead>()(data);
  },

  async foodDay(date) {
    const data = await unwrap(api().GET("/api/food/day", { params: { query: { date } } }));
    return fits<FoodDayRead>()(data);
  },

  async foodDays(from, to) {
    const data = await unwrap(api().GET("/api/food/days", { params: { query: { from, to } } }));
    return fits<FoodDaysRead>()(data);
  },

  async foodRecent(limit) {
    const data = await unwrap(api().GET("/api/food/recent", { params: { query: { limit } } }));
    return fits<MealRow[]>()(data);
  },

  async activitySummary() {
    const data = await unwrap(api().GET("/api/activity/summary"));
    return fits<ActivitySummaryRead>()(data);
  },

  async activityDays(from, to) {
    const data = await unwrap(api().GET("/api/activity/days", { params: { query: { from, to } } }));
    return fits<ActivityDaysRead>()(data);
  },

  async activityWorkouts(from, to) {
    const data = await unwrap(api().GET("/api/activity/workouts", { params: { query: { from, to } } }));
    return fits<ActivityWorkoutsRead>()(data);
  },

  async activityTags(from, to) {
    const data = await unwrap(api().GET("/api/activity/tags", { params: { query: { from, to } } }));
    return fits<ActivityTagsRead>()(data);
  },

  async ouraStatus() {
    return fits<OuraStatus>()(await unwrap(api().GET("/api/oura/status")));
  },

  async environment() {
    return fits<EnvironmentRead>()(await unwrap(api().GET("/api/environment")));
  },

  async environmentDays(days) {
    const data = await unwrap(api().GET("/api/environment/days", { params: { query: { days } } }));
    return fits<HomeDaysRead>()(data);
  },

  async spaces() {
    return fits<SpaceRow[]>()(await unwrap(api().GET("/api/environment/spaces")));
  },

  async spaceRatings(opts) {
    const data = await unwrap(
      api().GET("/api/environment/ratings", { params: { query: { space: opts?.space, days: opts?.days } } }),
    );
    return fits<SpaceRatingsRead>()(data);
  },

  async spacePhotos(space, shot) {
    const data = await unwrap(
      api().GET("/api/environment/spaces/{id}/photos", { params: { path: { id: space }, query: { shot } } }),
    );
    return fits<SpacePhotosRead>()(data);
  },

  async activityDay(date) {
    const data = await unwrap(api().GET("/api/activity/day", { params: { query: { date } } }));
    return fits<ActivityDayDetailRead>()(data);
  },

  async ledger(opts) {
    const data = await unwrap(
      api().GET("/api/ledger", {
        params: { query: { from: opts?.from, to: opts?.to, limit: opts?.limit } },
      }),
    );
    return fits<LedgerRow[]>()(data);
  },

  async ledgerDaily(opts) {
    const data = await unwrap(
      api().GET("/api/ledger/daily", {
        params: { query: { from: opts.from, to: opts.to, tz: opts.tz } },
      }),
    );
    return fits<LedgerDay[]>()(data);
  },

  async rewards(opts) {
    const data = await unwrap(
      api().GET("/api/rewards", {
        params: { query: { includeArchived: opts?.includeArchived } },
      }),
    );
    return fits<RewardRow[]>()(data);
  },

  async coverSearch(query, page = 1) {
    const data = await unwrap(api().GET("/api/covers/search", { params: { query: { q: query, page } } }));
    return normalizeCoverSearch(fits<CoverSearchRead>()(data));
  },

  async bountyDefaults() {
    const data = await unwrap(api().GET("/api/bounty/defaults"));
    return fits<BountyTypeDefaults[]>()(data);
  },

  async reviews(opts) {
    const data = await unwrap(
      api().GET("/api/reviews", {
        params: { query: { kind: opts?.kind, from: opts?.from, to: opts?.to } },
      }),
    );
    return fits<ReviewDoc[]>()(data);
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
