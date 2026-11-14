/**
 * TanStack Query hooks. Live reads/writes go through `lib/live/http.ts`, which
 * attaches `Authorization: Bearer` from the Supabase session (refreshed on expiry).
 */
import {
  keepPreviousData,
  useInfiniteQuery,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useMemo } from "react";
import { getAgentsClient } from "@/lib/agents";
import { isLiveApi } from "@/lib/api-base";
import { createWriteClient, getReads } from "@/lib/client";
import { buildAlignmentForest, type MapLens } from "@/lib/derive";
import { mapSubjects } from "@/lib/task-series";
import { nextCoverPage } from "@/lib/cover";
import { fetchMe } from "@/lib/me";
import type { LifeOsReads } from "@/lib/reads";
import { qk } from "@/lib/query-keys";
import type { NewSubjectRequest, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { errorText } from "@/lib/write-action";
import { decorateWrites } from "@/lib/write-effects";

function reads(): LifeOsReads {
  return getReads();
}

export { qk };

/** What each earning type's triggers pay by default — fixed while the app runs. */
export function useBountyDefaults() {
  return useQuery({
    queryKey: qk.bountyDefaults,
    queryFn: () => reads().bountyDefaults(),
    staleTime: Infinity,
  });
}

/**
 * The cover picker's search (COVER-1), a page at a time for "More photos". Unsplash allows
 * 50 searches an hour, so a search is kept for half an hour and a failure isn't retried.
 */
export function useCoverSearch(query: string, enabled = true) {
  return useInfiniteQuery({
    queryKey: qk.coverSearch(query),
    queryFn: ({ pageParam }) => reads().coverSearch(query, pageParam),
    initialPageParam: 1,
    getNextPageParam: nextCoverPage,
    enabled: enabled && query.trim() !== "",
    staleTime: 30 * 60_000,
    gcTime: 30 * 60_000,
    retry: false,
  });
}

/** What a subject list reads beyond its type: open or closed items only, at most `limit` (code review CR76). */
export interface SubjectListScope {
  enabled?: boolean;
  /** `open`, `closed`, or one status. */
  status?: string;
  limit?: number;
}

function subjectListKey(
  type: SubjectType | undefined,
  includeArchived: boolean,
  includeSeries: boolean,
  scope: SubjectListScope,
) {
  return [...qk.subjects, type ?? "all", includeArchived, includeSeries, scope.status ?? "any", scope.limit ?? "all"];
}

/**
 * Subjects by type; a repeating Task's series only with `includeSeries` (TASKS-3). A page that
 * shows open items asks for `status: "open"`, so it doesn't download every repetition ever done
 * (code review CR76).
 */
export function useSubjects(
  type?: SubjectType,
  includeArchived = false,
  includeSeries = false,
  opts: SubjectListScope = {},
) {
  return useQuery({
    queryKey: subjectListKey(type, includeArchived, includeSeries, opts),
    queryFn: () =>
      reads().listSubjects({ type, includeArchived, includeSeries, status: opts.status, limit: opts.limit }),
    enabled: opts.enabled ?? true,
  });
}

function joinLists(results: { data?: SubjectListItem[]; isLoading: boolean }[]) {
  return {
    data: results.every((r) => r.data) ? results.flatMap((r) => r.data!) : undefined,
    isLoading: results.some((r) => r.isLoading),
  };
}

/**
 * The live subjects of `types`, read a type at a time, each sharing `useSubjects(type)`'s cache:
 * a picker that offers Goals and Projects reads those, not every Task there is (code review CR76).
 */
export function useSubjectsOfTypes(types: readonly SubjectType[], enabled = true) {
  return useQueries({
    queries: types.map((type) => ({
      queryKey: subjectListKey(type, false, false, {}),
      queryFn: () => reads().listSubjects({ type }),
      enabled,
    })),
    combine: joinLists,
  });
}

/**
 * The command palette's matches for `query` among `types`, searched on the server and ranked as
 * the palette ranked them (code review CR76). Kept under the subject lists' key, so a write that
 * changes a list refreshes it too.
 */
export function useSubjectSearch(query: string, types: readonly SubjectType[], enabled = true) {
  const q = query.trim();
  return useQuery({
    queryKey: [...qk.subjects, "search", q, types.join(",")],
    queryFn: () => reads().searchSubjects(q, { types }),
    enabled: enabled && q.length > 0,
    placeholderData: keepPreviousData,
  });
}

/** Every repeating Task (TASKS-3), with its repetitions counted. */
export function useTaskSeries(includeArchived = false) {
  return useQuery({
    queryKey: [...qk.taskSeries, "list", includeArchived],
    queryFn: () => reads().taskSeries({ includeArchived }),
  });
}

/** One repeating Task's repetitions, latest first. */
export function useTaskSeriesHistory(seriesId: string | undefined) {
  return useQuery({
    queryKey: qk.taskSeriesHistory(seriesId ?? ""),
    queryFn: () => reads().taskSeriesHistory(seriesId!),
    enabled: !!seriesId,
  });
}

export function useSubject(id: string | undefined) {
  return useQuery({
    queryKey: qk.subject(id ?? ""),
    queryFn: () => reads().getSubject(id!),
    enabled: !!id,
  });
}

export function useListItem(id: string | undefined) {
  return useQuery({
    queryKey: qk.listItem(id ?? ""),
    queryFn: () => reads().getListItem(id!),
    enabled: !!id,
  });
}

export function useRelations(id: string | undefined) {
  return useQuery({
    queryKey: qk.relations(id ?? ""),
    queryFn: () => reads().relations(id!),
    enabled: !!id,
  });
}

export function useSubjectTags(id: string | undefined) {
  return useQuery({
    queryKey: qk.tags(id ?? ""),
    queryFn: () => reads().tags(id!),
    enabled: !!id,
  });
}

export function useTagUniverse() {
  return useQuery({
    queryKey: qk.tagUniverse,
    queryFn: () => reads().tagUniverse(),
  });
}

export function useJournal(id: string | undefined) {
  return useQuery({
    queryKey: qk.journal(id ?? ""),
    queryFn: () => reads().journal(id!),
    enabled: !!id,
  });
}

export function useHistory(id: string | undefined) {
  return useQuery({
    queryKey: qk.history(id ?? ""),
    queryFn: () => reads().history(id!),
    enabled: !!id,
  });
}

export function useInbox() {
  return useQuery({
    queryKey: qk.inbox,
    queryFn: () => reads().inbox(),
  });
}

/** How many resolved items the Resolved view reads at first, and how many more each Load older adds (code review CR76). */
export const RESOLVED_PAGE = 50;
/** The most the API returns. */
export const RESOLVED_MAX = 1000;

/** The newest `limit` items to leave the Inbox; the rows already shown stay while more load. */
export function useResolvedInbox(enabled = true, limit = RESOLVED_PAGE) {
  return useQuery({
    queryKey: [...qk.inboxResolved, limit],
    queryFn: () => reads().inboxResolved(limit),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useArtifact(id: string | undefined) {
  return useQuery({
    queryKey: qk.artifact(id ?? ""),
    queryFn: () => reads().artifact(id!),
    enabled: !!id,
  });
}

/**
 * An object URL for an artifact's bytes (a food photo), or null when it has none. Bytes never
 * change, so the URL is never refetched.
 */
export function useArtifactBytesUrl(id: string | null | undefined) {
  return useQuery({
    queryKey: [...qk.artifact(id ?? ""), "bytes"] as const,
    queryFn: () => reads().artifactBytesUrl(id!),
    enabled: !!id,
    staleTime: Infinity,
  });
}

export function useSubjectFiles(id: string | undefined) {
  return useQuery({
    queryKey: qk.files(id ?? ""),
    queryFn: () => reads().subjectFiles(id!),
    enabled: !!id,
  });
}

export function useAreas() {
  return useQuery({
    queryKey: qk.areas,
    queryFn: () => reads().areas(),
  });
}

export function usePeople() {
  return useQuery({
    queryKey: qk.people,
    queryFn: () => reads().people(),
  });
}

/** LifeOS's allow list and log summary for Connected agents (`GET /api/agents`). */
export function useAgentsOverview() {
  return useQuery({
    queryKey: qk.agentsOverview,
    queryFn: () => getAgentsClient().overview(),
  });
}

/** The agents approved at Supabase (supabase-js `listGrants`). */
export function useAgentGrants() {
  return useQuery({
    queryKey: qk.agentGrants,
    queryFn: () => getAgentsClient().grants(),
    retry: false,
  });
}

/** An agent's latest calls, read when its list is opened. */
export function useAgentCalls(clientId: string, enabled: boolean) {
  return useQuery({
    queryKey: qk.agentCalls(clientId),
    queryFn: () => getAgentsClient().calls(clientId, 20),
    enabled,
  });
}

export type AgentAction =
  | { kind: "allow"; clientId: string; name: string; uri: string | null }
  | { kind: "revoke"; clientId: string; removeGrant: boolean }
  | { kind: "remove-grant"; clientId: string }
  | { kind: "link"; clientId: string; personId: string | null };

/**
 * Allow, revoke, remove or link an agent. Revoking takes it off LifeOS's list first, so `/mcp`
 * refuses its next call even if Supabase then fails to remove its grant.
 */
export function useAgentAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (action: AgentAction) => {
      const agents = getAgentsClient();
      switch (action.kind) {
        case "allow":
          await agents.allow(action.clientId, action.name, action.uri);
          return;
        case "revoke":
          await agents.revoke(action.clientId);
          if (action.removeGrant) {
            try {
              await agents.removeGrant(action.clientId);
            } catch (e) {
              throw new Error(
                `LifeOS refuses it now, but Supabase still lists it: ${errorText(e, "no answer")}`,
              );
            }
          }
          return;
        case "remove-grant":
          await agents.removeGrant(action.clientId);
          return;
        case "link":
          await agents.linkPerson(action.clientId, action.personId);
          return;
      }
    },
    onSettled: () => qc.invalidateQueries({ queryKey: qk.agents }),
  });
}

/** A day's appointments for Focus (CAL-1). */
export function useAppointments(on: string) {
  return useQuery({
    queryKey: qk.appointments(on),
    queryFn: () => reads().appointments({ on }),
  });
}

export function useDashboard() {
  return useQuery({
    queryKey: qk.dashboard,
    queryFn: () => reads().dashboard(),
  });
}

/**
 * The Map's outline, built from the cached subject list and edges rather than read whole, so the
 * Map page, which reads both for its graph too, asks for each once (code review CR37). A repeating
 * Task is one node, its series; its repetitions stay off the Map (TASKS-3).
 */
export function useAlignmentForest(includeArchived: boolean, lens: MapLens) {
  const subjects = useSubjects(undefined, includeArchived, true);
  const edges = useAlignmentEdges(includeArchived);
  const data = useMemo(
    () =>
      subjects.data && edges.data
        ? buildAlignmentForest(mapSubjects(subjects.data), edges.data, { includeArchived, lens })
        : undefined,
    [subjects.data, edges.data, includeArchived, lens],
  );
  return {
    data,
    isLoading: subjects.isLoading || edges.isLoading,
    isError: subjects.isError || edges.isError,
  };
}

export function useAlignmentEdges(includeArchived: boolean) {
  return useQuery({
    queryKey: qk.edges(includeArchived),
    queryFn: () => reads().edges({ includeArchived }),
  });
}

export function useHabits() {
  return useQuery({
    queryKey: qk.habits,
    queryFn: () => reads().habits(),
  });
}

/** Every habit's row for one day, as the dashboard reads them. */
export function useOccurrencesOn(on: string) {
  return useQuery({
    queryKey: [...qk.occurrences, "on", on],
    queryFn: () => reads().occurrences({ on }),
  });
}

export function useOccurrences(habitId?: string) {
  return useQuery({
    queryKey: [...qk.occurrences, habitId ?? "all"],
    queryFn: () => reads().occurrences(habitId ? { habitId } : undefined),
  });
}

/** Every timed Task's timer — one read feeds the detail widget, Tasks rows and the shell. */
export function useTimers() {
  return useQuery({
    queryKey: qk.timers,
    queryFn: () => reads().timers(),
  });
}

/** One task's timer, or null when it has never run. */
export function useTimer(taskId: string | undefined) {
  const { data, ...rest } = useTimers();
  const timer = useMemo(
    () => (taskId ? (data ?? []).find((t) => t.subjectId === taskId) ?? null : null),
    [data, taskId],
  );
  return { ...rest, data: timer };
}

/** Every currency's balance (REWARD-1). */
export function useWallet() {
  return useQuery({
    queryKey: qk.wallet,
    queryFn: () => reads().wallet(),
  });
}

/** Every life domain's progression (REWARD-6 / REWARD-7): the Character page. */
export function useDomains() {
  return useQuery({
    queryKey: qk.domains,
    queryFn: () => reads().domains(),
  });
}

/** One domain's last `days` days, for its daily chart; the last range stays up while the next loads. */
export function useDomainDays(domain: string, days: number, enabled = true) {
  return useQuery({
    queryKey: qk.domainDays(domain, days),
    queryFn: () => reads().domainDays(domain, days),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Every life domain over its last `days` days: the Character page's Reports. */
export function useDomainReport(days: number, enabled = true) {
  return useQuery({
    queryKey: qk.domainReport(days),
    queryFn: () => reads().domainReport(days),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** The days marked as exceptions among the last `days` (REWARD-8): the Character page's list. */
export function useExceptionDays(days = 90) {
  return useQuery({
    queryKey: qk.exceptionDays(days),
    queryFn: () => reads().exceptionDays(days),
  });
}

/**
 * One day of the Food Log (FOOD-4), today in the home zone when `date` is left out; the last
 * day stays up while the next loads.
 */
export function useFoodDay(date?: string) {
  return useQuery({
    queryKey: qk.foodDay(date ?? "today"),
    queryFn: () => reads().foodDay(date),
    placeholderData: keepPreviousData,
  });
}

/** Every day from `from` to `to`: the Food Log's Week and Month views. */
export function useFoodDays(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: qk.foodDays(from, to),
    queryFn: () => reads().foodDays(from, to),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Recent entries by name, for "Log again". */
export function useFoodRecent(enabled = true) {
  return useQuery({
    queryKey: qk.foodRecent,
    queryFn: () => reads().foodRecent(30),
    enabled,
  });
}

/** The Activity page's top: today so far, this week against last, and the Oura connection. */
export function useActivitySummary() {
  return useQuery({
    queryKey: qk.activitySummary,
    queryFn: () => reads().activitySummary(),
  });
}

/** The Oura connection: the Food Log and Character pages ask for the throttled sync when it's connected. */
export function useOuraStatus() {
  return useQuery({
    queryKey: qk.ouraStatus,
    queryFn: () => reads().ouraStatus(),
  });
}

/** Every day from `from` to `to`: the step chart; the last range stays up while the next loads. */
export function useActivityDays(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: qk.activityDays(from, to),
    queryFn: () => reads().activityDays(from, to),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Workouts from `from` to `to`, counted or not: the Exercise card. */
export function useActivityWorkouts(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: qk.activityWorkouts(from, to),
    queryFn: () => reads().activityWorkouts(from, to),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Tags from the Oura app starting from `from` to `to`, newest first: the Tags card. */
export function useActivityTags(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: qk.activityTags(from, to),
    queryFn: () => reads().activityTags(from, to),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** The home today (HOME-3): the Home Score, each space with its parts, and Fix next. */
export function useEnvironment() {
  return useQuery({
    queryKey: qk.environment,
    queryFn: () => reads().environment(),
  });
}

/** The Home Score on each of the last `days` days: the chart; the last range stays up while the next loads. */
export function useEnvironmentDays(days: number, enabled = true) {
  return useQuery({
    queryKey: qk.environmentDays(days),
    queryFn: () => reads().environmentDays(days),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Every space, archived ones too: the spaces editor. */
export function useSpaces(enabled = true) {
  return useQuery({
    queryKey: qk.spaces,
    queryFn: () => reads().spaces(),
    enabled,
  });
}

/** The ratings among the last `days`, of one space (its id) when given, newest first. */
export function useSpaceRatings(space: string | null, days = 90, enabled = true) {
  return useQuery({
    queryKey: qk.spaceRatings(space ?? "", days),
    queryFn: () => reads().spaceRatings({ space: space ?? undefined, days }),
    enabled,
  });
}

/** A space's photos, newest first, of one shot (its id) when given: the compare dialog's filmstrip. */
export function useSpacePhotos(space: string | null, shot: string | null = null, enabled = true) {
  return useQuery({
    queryKey: qk.spacePhotos(space ?? "", shot ?? ""),
    queryFn: () => reads().spacePhotos(space!, shot ?? undefined),
    enabled: enabled && space != null,
  });
}

/** One day in full: last night on the Sleep card, and the day sheet. */
export function useActivityDay(date: string | null, enabled = true) {
  return useQuery({
    queryKey: qk.activityDay(date ?? ""),
    queryFn: () => reads().activityDay(date!),
    enabled: enabled && date != null,
  });
}

/** The newest ledger entries, newest first. */
export function useLedger(limit = 100) {
  return useQuery({
    queryKey: [...qk.ledger, limit],
    queryFn: () => reads().ledger({ limit }),
  });
}

/** The most entries one history range lists (the API's cap). */
export const LEDGER_RANGE_LIMIT = 500;

/**
 * The ledger entries in [from, to) (ISO instants), newest first, up to
 * {@link LEDGER_RANGE_LIMIT}. Keyed under `ledger`, so every earning or spending write
 * refreshes it.
 */
export function useLedgerRange(from: string, to: string) {
  return useQuery({
    queryKey: [...qk.ledger, "range", from, to],
    queryFn: () => reads().ledger({ from, to, limit: LEDGER_RANGE_LIMIT }),
    placeholderData: keepPreviousData,
  });
}

/**
 * The history by local day (REWARD-5) for the days [from, to) in the viewer's time zone.
 * Keyed under `ledger`, so it refreshes with every earn, reversal and redemption.
 */
export function useLedgerDaily(from: string, to: string, tz: string) {
  return useQuery({
    queryKey: [...qk.ledger, "daily", from, to, tz],
    queryFn: () => reads().ledgerDaily({ from, to, tz }),
    placeholderData: keepPreviousData,
  });
}

/** The reward catalog (REWARD-3). */
export function useRewards(includeArchived = false) {
  return useQuery({
    queryKey: [...qk.rewards, includeArchived],
    queryFn: () => reads().rewards({ includeArchived }),
  });
}

/** The reviews whose day is from `from` to `to` (REVIEW-1, REVIEW-2). Reading never makes one. */
export function useReviews(from: string, to: string) {
  return useQuery({
    queryKey: [...qk.reviews, from, to],
    queryFn: () => reads().reviews({ from, to }),
  });
}

/** The habit days recorded from `from` to `to`, for a review's check-offs (REVIEW-5). */
export function useOccurrencesBetween(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: [...qk.occurrences, "between", from, to],
    queryFn: () => reads().occurrences({ from, to }),
    enabled,
  });
}

export function useRecap(start: string, end: string, asOf: string) {
  return useQuery({
    queryKey: [...qk.recap, start, end, asOf],
    queryFn: () => reads().recap({ start, end, asOf }),
  });
}

export function useMe() {
  return useQuery({
    queryKey: qk.me,
    queryFn: fetchMe,
    enabled: isLiveApi(),
  });
}

export function useWrites() {
  const qc = useQueryClient();
  return useMemo(() => decorateWrites(createWriteClient(), qc), [qc]);
}

export function useCreateSubject() {
  const writes = useWrites();
  return useMutation({
    mutationFn: (req: NewSubjectRequest) => writes.newSubject(req),
  });
}
