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
  ActivityDayDetailRead,
  ActivityDayRow,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivityTagsRead,
  ActivityWorkoutsRead,
  AreaRow,
  ArtifactRecord,
  CoverSearchRead,
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
import { isTerminal } from "@/lib/production-ui-types";
import { composeRecap, type RecapSection } from "@/lib/review-recap";
import { mapSubjects } from "@/lib/task-series";
import { BOUNTY_DEFAULTS } from "@/lib/bounty";
import { tagUniverse, toRelationEdge } from "@/lib/mock/seed";
import {
  byUrn,
  detailWithBounty,
  ensureReview,
  ensureReviewDocs,
  filesForSubject,
  getArtifact,
  getState as storeState,
  listTimers,
  mockHabits,
  mockLedger,
  mockLedgerDaily,
  mockRewards,
  mockWallet,
  mockDomains,
  mockFoodDay,
  mockFoodDays,
  mockFoodRecent,
  mockDomainDays,
  mockDomainReport,
  mockExceptionDays,
  mockActivityPaid,
  isTaskSeries,
  mockTaskSeries,
  mockTaskSeriesHistory,
  withBounty,
  withCover,
  withRepeat,
} from "@/lib/mock/store";
import { mockSearchScenes } from "@/lib/mock/covers";
import {
  mockActivityDay,
  mockActivityDays,
  mockActivitySummary,
  mockActivityTags,
  mockActivityWorkouts,
  mockOuraStatus,
} from "@/lib/mock/activity-store";
import {
  mockEnvironment,
  mockHomeDays,
  mockSpacePhotoUrl,
  mockSpacePhotos,
  mockSpaceRatings,
  mockSpaces,
} from "@/lib/mock/environment-store";

export type { AlignmentEdge, AlignmentNode, DashboardRead, MapLens } from "@/lib/derive";

const wait = (ms = 80) => new Promise((r) => setTimeout(r, ms));

export interface SubjectRelations {
  parents: RelationEdge[];
  children: RelationEdge[];
}

/** An Inbox row's own tags: keyed by subject id for a subject, by event id otherwise. */
function inboxTags(row: InboxItem): string[] {
  const key =
    row.itemKind === "subject"
      ? row.subjectUrn
        ? byUrn(row.subjectUrn)?.id
        : undefined
      : row.itemId;
  return key ? (storeState().tagsByItem[key] ?? []) : [];
}

function listOf(type?: SubjectListItem["type"], includeArchived = false, includeSeries = false): SubjectListItem[] {
  return storeState()
    .subjects.filter((s) => {
      if (!includeArchived && s.archived) return false;
      if (type && s.type !== type) return false;
      // A repeating Task's series stays out of the lists: its repetitions are the Tasks (D16).
      if (!includeSeries && isTaskSeries(s.id)) return false;
      return true;
    })
    .map(listed);
}

/** A subject as the list reads give it: with its bounty, cover and repeat, as the live API does. */
function listed(s: SubjectListItem): SubjectListItem {
  return withRepeat(withCover(withBounty(s)));
}

/** Every subject the lists show: the repeating Tasks' series left out, as the live API does. */
function shown(subjects: SubjectListItem[]): SubjectListItem[] {
  return subjects.filter((s) => !isTaskSeries(s.id)).map(listed);
}

export interface LifeOsReads {
  /** Subjects by type; a repeating Task's series only with `includeSeries` (TASKS-3). */
  listSubjects(opts?: {
    type?: SubjectListItem["type"];
    includeArchived?: boolean;
    includeSeries?: boolean;
  }): Promise<SubjectListItem[]>;
  getSubject(id: string): Promise<SubjectDetail | null>;
  getListItem(id: string): Promise<SubjectListItem | null>;
  relations(id: string): Promise<SubjectRelations>;
  tags(id: string): Promise<string[]>;
  tagUniverse(): Promise<TagUniverseItem[]>;
  journal(id: string): Promise<JournalEntry[]>;
  history(id: string): Promise<StatusHistoryEntry[]>;
  inbox(): Promise<InboxItem[]>;
  /** Items that left the Inbox, most recently resolved first (Back to Inbox). */
  inboxResolved(): Promise<ResolvedInboxItem[]>;
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


/** The days with what the mock's ledger pays each (`ActivityPaidReads`). */
function withActivityPaid(days: ActivityDayRow[]): ActivityDayRow[] {
  if (days.length === 0) return days;
  const paid = mockActivityPaid(days[0].day, days[days.length - 1].day);
  return days.map((d) => ({ ...d, paid: paid.get(d.day) ?? d.paid }));
}

export const mockReads: LifeOsReads = {
  async listSubjects(opts?: {
    type?: SubjectListItem["type"];
    includeArchived?: boolean;
    includeSeries?: boolean;
  }): Promise<SubjectListItem[]> {
    await wait();
    return listOf(opts?.type, opts?.includeArchived, opts?.includeSeries);
  },

  async getSubject(id: string): Promise<SubjectDetail | null> {
    await wait();
    const detail = storeState().details[id];
    return detail ? withCover(detailWithBounty(detail)) : null;
  },

  async getListItem(id: string): Promise<SubjectListItem | null> {
    await wait();
    const item = storeState().subjects.find((s) => s.id === id);
    return item ? listed(item) : null;
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
    return [...storeState().inbox]
      .sort((a, b) => b.triagedAt.localeCompare(a.triagedAt))
      .map((row) => ({ ...row, tags: inboxTags(row) }));
  },

  async inboxResolved(): Promise<ResolvedInboxItem[]> {
    await wait();
    return [...storeState().resolved]
      .sort((a, b) => b.resolvedAt.localeCompare(a.resolvedAt))
      .flatMap(({ row, resolution, resolvedAt }) => {
        const subject = row.subjectUrn ? byUrn(row.subjectUrn) : undefined;
        if (subject?.archived) return [];
        return [
          {
            ...row,
            tags: inboxTags(row),
            resolution,
            resolvedAt,
            subjectStatus: subject?.status ?? null,
            subjectStatusTerminal: isTerminal(subject?.status),
          },
        ];
      });
  },

  async artifact(id: string): Promise<ArtifactRecord | null> {
    await wait();
    return getArtifact(id) ?? null;
  },

  async artifactBytesUrl(id: string): Promise<string | null> {
    await wait();
    // TODO(api): stream from the kernel's bytes-fetch / export path.
    return getArtifact(id)?.bytesUrl ?? mockSpacePhotoUrl(id);
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
    return mockHabits();
  },

  async occurrences(opts?: { habitId?: string; on?: string }): Promise<HabitOccurrenceRow[]> {
    await wait();
    return storeState().occurrences.filter((o) => {
      if (opts?.habitId && o.habitId !== opts.habitId) return false;
      if (opts?.on && o.occurrenceDate !== opts.on) return false;
      return true;
    });
  },

  async timers(): Promise<TaskTimer[]> {
    await wait();
    return listTimers();
  },

  async taskSeries(opts?: { includeArchived?: boolean }): Promise<TaskSeriesRow[]> {
    await wait();
    return mockTaskSeries(opts?.includeArchived);
  },

  async taskSeriesHistory(id: string): Promise<TaskSeriesHistory | null> {
    await wait();
    return mockTaskSeriesHistory(id);
  },

  async wallet(): Promise<Wallet> {
    await wait();
    return mockWallet();
  },

  async domains(): Promise<DomainsRead> {
    await wait();
    return mockDomains();
  },

  async domainDays(domain: string, days?: number): Promise<DomainDaysRead> {
    await wait();
    return mockDomainDays(domain, days);
  },

  async domainReport(days?: number): Promise<DomainReportRead> {
    await wait();
    return mockDomainReport(days);
  },

  async exceptionDays(days?: number): Promise<ExceptionDaysRead> {
    await wait();
    return mockExceptionDays(days);
  },

  async foodDay(date?: string): Promise<FoodDayRead> {
    await wait();
    return mockFoodDay(date);
  },

  async foodDays(from?: string, to?: string): Promise<FoodDaysRead> {
    await wait();
    return mockFoodDays(from, to);
  },

  async foodRecent(limit?: number): Promise<MealRow[]> {
    await wait();
    return mockFoodRecent(limit);
  },

  async activitySummary(): Promise<ActivitySummaryRead> {
    await wait();
    const summary = mockActivitySummary();
    return { ...summary, todaySoFar: withActivityPaid([summary.todaySoFar])[0] };
  },

  async activityDays(from?: string, to?: string): Promise<ActivityDaysRead> {
    await wait();
    const read = mockActivityDays(from, to);
    return { ...read, days: withActivityPaid(read.days) };
  },

  async activityWorkouts(from?: string, to?: string): Promise<ActivityWorkoutsRead> {
    await wait();
    return mockActivityWorkouts(from, to);
  },

  async activityTags(from?: string, to?: string): Promise<ActivityTagsRead> {
    await wait();
    return mockActivityTags(from, to);
  },

  async ouraStatus(): Promise<OuraStatus> {
    await wait();
    return mockOuraStatus();
  },

  async activityDay(date?: string): Promise<ActivityDayDetailRead> {
    await wait();
    const detail = mockActivityDay(date);
    return { ...detail, summary: withActivityPaid([detail.summary])[0] };
  },

  async environment(): Promise<EnvironmentRead> {
    await wait();
    return mockEnvironment(storeState().exceptionDays);
  },

  async environmentDays(days?: number): Promise<HomeDaysRead> {
    await wait();
    return mockHomeDays(days, storeState().exceptionDays);
  },

  async spaces(): Promise<SpaceRow[]> {
    await wait();
    return mockSpaces();
  },

  async spaceRatings(opts?: { space?: string; days?: number }): Promise<SpaceRatingsRead> {
    await wait();
    return mockSpaceRatings(opts);
  },

  async spacePhotos(space: string, shot?: string): Promise<SpacePhotosRead> {
    await wait();
    return mockSpacePhotos(space, shot, storeState().exceptionDays);
  },

  async ledger(opts?: { from?: string; to?: string; limit?: number }): Promise<LedgerRow[]> {
    await wait();
    return mockLedger(opts);
  },

  async ledgerDaily(opts: { from: string; to: string; tz: string }): Promise<LedgerDay[]> {
    await wait();
    return mockLedgerDaily(opts);
  },

  async rewards(opts?: { includeArchived?: boolean }): Promise<RewardRow[]> {
    await wait();
    return mockRewards(opts?.includeArchived);
  },

  async coverSearch(query: string, page = 1): Promise<CoverSearchRead> {
    await wait();
    return mockSearchScenes(query, page);
  },

  async bountyDefaults(): Promise<BountyTypeDefaults[]> {
    await wait();
    return BOUNTY_DEFAULTS;
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
      series: mockTaskSeries(false).flatMap((row) => mockTaskSeriesHistory(row.id) ?? []),
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
      subjects: shown(s.subjects),
      edges: s.edges,
      inboxCount: s.inbox.length,
      habits: mockHabits(),
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
    // A repeating Task is one node, its series; its repetitions stay off the Map (TASKS-3).
    return buildAlignmentForest(mapSubjects(s.subjects.map(listed)), s.edges, opts);
  },
};

/** @deprecated Prefer importing from `@/lib/client`. Kept so existing mock imports keep working. */
export const lifeOsReads = mockReads;
