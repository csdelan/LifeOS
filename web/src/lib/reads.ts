/**
 * The read seam (code review CR23): what every screen reads, which `lib/client.ts` answers from
 * the live API or, in mock mode, from the in-memory mock. It lives outside `lib/mock` so the live
 * code never imports the mock, and the production bundle can leave the mock out.
 */

import type { AlignmentEdge, DashboardRead } from "@/lib/derive";
import type {
  ActivityDayDetailRead,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivityTagsRead,
  ActivityWorkoutsRead,
  AppointmentRow,
  AreaRow,
  ArtifactRecord,
  BountyTypeDefaults,
  MetricTemplate,
  CoverSearchRead,
  DomainDaysRead,
  DomainReportRead,
  DomainsRead,
  EnvironmentRead,
  ExceptionDaysRead,
  FoodDayRead,
  FoodDaysRead,
  HabitOccurrenceRow,
  HabitRow,
  HomeDaysRead,
  InboxItem,
  JournalEntry,
  LabVisitsRead,
  LedgerDay,
  LedgerRow,
  MealRow,
  MetricDetailRead,
  MetricRow,
  OuraStatus,
  PersonRow,
  RelationEdge,
  ResolvedInboxItem,
  ReviewDoc,
  ReviewKind,
  RewardRow,
  SpacePhotosRead,
  SpaceRatingsRead,
  SpaceRow,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  TagUniverseItem,
  TaskSeriesHistory,
  TaskSeriesRow,
  TaskTimer,
  Wallet,
} from "@/lib/production-ui-types";
import type { RecapSection } from "@/lib/review-recap";
import type { SubjectSearch } from "@/lib/browse";
import type { SubjectType } from "@/lib/production-ui-types";

/** A subject's parent and child edges. */
export interface SubjectRelations {
  parents: RelationEdge[];
  children: RelationEdge[];
}

/** What `listSubjects` reads (code review CR76). */
export interface ListSubjectsOptions {
  type?: SubjectListItem["type"];
  includeArchived?: boolean;
  includeSeries?: boolean;
  /** `open`, `closed`, or one status; every status when left out. */
  status?: string;
  /** At most this many, from 1 to 1000. */
  limit?: number;
}

export interface LifeOsReads {
  /**
   * Subjects by type; a repeating Task's series only with `includeSeries` (TASKS-3). `status` is
   * `open` (any status not finished), `closed`, or one status; `limit` cuts the list (code review
   * CR76), so a page that shows open items reads only those.
   */
  listSubjects(opts?: ListSubjectsOptions): Promise<SubjectListItem[]>;
  /**
   * The command palette's search (`GET /api/subjects/search`, code review CR76): subjects with
   * every word of `query` in their title, type, URN, area or tags, archived ones too, best first;
   * at most `limit`, with how many match in all.
   */
  searchSubjects(query: string, opts?: { types?: readonly SubjectType[]; limit?: number }): Promise<SubjectSearch>;
  /** How many items wait in the Inbox (`GET /api/inbox/count`): Focus shows the number, not the rows. */
  inboxCount(): Promise<number>;
  getSubject(id: string): Promise<SubjectDetail | null>;
  getListItem(id: string): Promise<SubjectListItem | null>;
  relations(id: string): Promise<SubjectRelations>;
  tags(id: string): Promise<string[]>;
  tagUniverse(): Promise<TagUniverseItem[]>;
  journal(id: string): Promise<JournalEntry[]>;
  history(id: string): Promise<StatusHistoryEntry[]>;
  inbox(): Promise<InboxItem[]>;
  /** The newest `limit` (50 when left out) items to leave the Inbox, most recently resolved first (Back to Inbox). */
  inboxResolved(limit?: number): Promise<ResolvedInboxItem[]>;
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
  /** Habit days, of one habit, on one day or over a range (a weekly review's check-offs). */
  occurrences(opts?: { habitId?: string; on?: string; from?: string; to?: string }): Promise<HabitOccurrenceRow[]>;
  /**
   * The appointments on a day (CAL-1, `GET /api/appointments?on=`): one-time ones and a series'
   * occurrences, never the series itself; all-day first, then by start time.
   */
  appointments(opts: { on: string }): Promise<AppointmentRow[]>;
  /** Every timed Task's timer (TASKS-2), running first. Archived tasks are left out. */
  timers(): Promise<TaskTimer[]>;
  /** Every repeating Task (TASKS-3), by title, with its repetitions counted. */
  taskSeries(opts?: { includeArchived?: boolean }): Promise<TaskSeriesRow[]>;
  /** A repeating Task (by the series' id) and its repetitions, latest first; null when it isn't one. */
  taskSeriesHistory(id: string): Promise<TaskSeriesHistory | null>;
  /** Every currency's balance (REWARD-1), and what a Coin is worth. */
  wallet(): Promise<Wallet>;
  /** Every life domain's progression (REWARD-6 / REWARD-7, xp-domains plan §3.11). */
  domains(): Promise<DomainsRead>;
  /** One life domain's last `days` days (30 by default), today included: the daily chart. */
  domainDays(domain: string, days?: number): Promise<DomainDaysRead>;
  /** Every life domain over its last `days` days (30 by default): the Reports view. */
  domainReport(days?: number): Promise<DomainReportRead>;
  /** The days marked as exceptions among the last `days` (90 by default), newest first (REWARD-8). */
  exceptionDays(days?: number): Promise<ExceptionDaysRead>;
  /** One day of the Food Log (today by default): its entries, fasts and diet score (FOOD-4). */
  foodDay(date?: string): Promise<FoodDayRead>;
  /** Every day of a range, at most 62 (the week so far by default): the Week and Month views. */
  foodDays(from?: string, to?: string): Promise<FoodDaysRead>;
  /** The newest version of each recent entry by name, for "Log again". */
  foodRecent(limit?: number): Promise<MealRow[]>;
  /**
   * The Activity page's top (ACTIVITY-1..4): today so far with last night, today's workouts,
   * this week against last, and the Oura connection.
   */
  activitySummary(): Promise<ActivitySummaryRead>;
  /** Every day of a range (the 30 days through today by default), oldest first: the charts. */
  activityDays(from?: string, to?: string): Promise<ActivityDaysRead>;
  /** Workouts on those days (the last 12 whole weeks by default), newest first, counted or not. */
  activityWorkouts(from?: string, to?: string): Promise<ActivityWorkoutsRead>;
  /** Tags from the Oura app starting on those days (the 30 days through today by default), newest first. */
  activityTags(from?: string, to?: string): Promise<ActivityTagsRead>;
  /** One day in full (today by default): both scores' contributors, the night, naps, workouts and tags. */
  activityDay(date?: string): Promise<ActivityDayDetailRead>;
  /** The Oura connection (`GET /api/oura/status`): other pages ask for the throttled sync when it's connected. */
  ouraStatus(): Promise<OuraStatus>;
  /** The home today (HOME-3): the Home Score, each space with its parts, and Fix next. */
  environment(): Promise<EnvironmentRead>;
  /** The Home Score on each of the last `days` days (90 by default), oldest first, with exception days. */
  environmentDays(days?: number): Promise<HomeDaysRead>;
  /** Every space (HOME-1), archived ones included, by name. */
  spaces(): Promise<SpaceRow[]>;
  /** The ratings among the last `days` (90 by default), of one space (its id) when given, newest first. */
  spaceRatings(opts?: { space?: string; days?: number }): Promise<SpaceRatingsRead>;
  /**
   * A space's photos (HOME-4, `GET /api/environment/spaces/{id}/photos`), newest first, of one
   * shot (its id or name) when given, each with the space's score on its day; its shots too.
   */
  spacePhotos(space: string, shot?: string): Promise<SpacePhotosRead>;
  /**
   * The starter list (measurements plan §2.1, `GET /api/measurements/catalog`): what making each
   * metric and lab test by its name fills in, in the list's order (code review CR73).
   */
  metricCatalog(): Promise<MetricTemplate[]>;
  /** Every metric (MEASURE-1, `GET /api/measurements/metrics`), archived ones included, by name. */
  metrics(): Promise<MetricRow[]>;
  /**
   * One metric (its id or name) over the last `days` days, all history when null
   * (`GET /api/measurements/metrics/{id}`): readings newest first, day values with the trend,
   * and the goal.
   */
  metric(metric: string, days: number | null): Promise<MetricDetailRead>;
  /**
   * Every lab visit with its results, newest collected first (MEASURE-4,
   * `GET /api/measurements/visits`). A report's bytes come from `artifactBytesUrl`.
   */
  labVisits(): Promise<LabVisitsRead>;
  /** Ledger entries newest first, in [from, to) (ISO timestamps); `limit` defaults to 100. */
  ledger(opts?: { from?: string; to?: string; limit?: number }): Promise<LedgerRow[]>;
  /**
   * The history by local day (REWARD-5): the days [from, to) (`YYYY-MM-DD`) as they read in
   * `tz`, the viewer's IANA time zone, oldest first; days without entries are left out.
   */
  ledgerDaily(opts: { from: string; to: string; tz: string }): Promise<LedgerDay[]>;
  /** The reward catalog (REWARD-3), with each Reward's redemptions and savings progress. */
  rewards(opts?: { includeArchived?: boolean }): Promise<RewardRow[]>;
  /** What each trigger of each earning type pays by default (REWARD-2). */
  bountyDefaults(): Promise<BountyTypeDefaults[]>;
  /**
   * A page of Unsplash photos for the cover picker (COVER-1), searched through the server.
   * Without an Access Key there, `configured` is false and there are no photos.
   */
  coverSearch(query: string, page?: number): Promise<CoverSearchRead>;
  /**
   * The reviews whose day is in the range (REVIEW-1, REVIEW-2), as they read today. Reading
   * never makes one: `openReview` does, the first time a review is looked at.
   */
  reviews(opts?: { kind?: ReviewKind; from?: string; to?: string }): Promise<ReviewDoc[]>;
  recap(opts: { start: string; end: string; asOf: string }): Promise<RecapSection[]>;
  /**
   * Bulk alignment edges (serves / results_in / supersedes) for Map outline + graph.
   * // TODO(api): live GET /api/edges — already sketched in OpenAPI; confirm it returns
   * the full graph (not per-subject) before relying on it in live mode.
   */
  edges(opts?: { includeArchived?: boolean }): Promise<AlignmentEdge[]>;
  dashboard(): Promise<DashboardRead>;
}
