/**
 * TanStack Query hooks. Live reads/writes go through `lib/live/http.ts`, which
 * attaches `Authorization: Bearer` from the Supabase session (refreshed on expiry).
 */
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query";
import { useMemo } from "react";
import { isLiveApi } from "@/lib/api-base";
import { createWriteClient, getReads } from "@/lib/client";
import { mapForestStatus, type DashboardRead, type MapLens } from "@/lib/derive";
import { announceEarned } from "@/lib/earn-feedback";
import { fetchMe, meQueryKey } from "@/lib/me";
import type { LifeOsReads } from "@/lib/mock/api";
import type {
  AdherenceState,
  Earned,
  HabitOccurrenceRow,
  InboxItem,
  LifeOsWriteClient,
  NewSubjectRequest,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
} from "@/lib/production-ui-types";

function reads(): LifeOsReads {
  return getReads();
}

export const qk = {
  me: meQueryKey,
  subjects: ["subjects"] as const,
  subject: (id: string) => ["subject", id] as const,
  listItem: (id: string) => ["list-item", id] as const,
  relations: (id: string) => ["relations", id] as const,
  tags: (id: string) => ["tags", id] as const,
  tagUniverse: ["tag-universe"] as const,
  journal: (id: string) => ["journal", id] as const,
  history: (id: string) => ["history", id] as const,
  inbox: ["inbox"] as const,
  // Nested under ["inbox"], so every Inbox invalidation refreshes it too.
  inboxResolved: ["inbox", "resolved"] as const,
  artifact: (id: string) => ["artifact", id] as const,
  files: (id: string) => ["files", id] as const,
  areas: ["areas"] as const,
  people: ["people"] as const,
  habits: ["habits"] as const,
  occurrences: ["occurrences"] as const,
  timers: ["timers"] as const,
  wallet: ["wallet"] as const,
  domains: ["domains"] as const,
  // Nested under ["domains"], so every domains invalidation refreshes the chart too.
  domainDays: (domain: string, days: number) => ["domains", "days", domain, days] as const,
  domainReport: (days: number) => ["domains", "report", days] as const,
  food: ["food"] as const,
  foodDay: (date: string) => ["food", "day", date] as const,
  foodDays: (from: string, to: string) => ["food", "days", from, to] as const,
  foodRecent: ["food", "recent"] as const,
  // Every Activity read sits under ["activity"], so a sync, a connect or a note refreshes them all.
  activity: ["activity"] as const,
  activitySummary: ["activity", "summary"] as const,
  activityDays: (from: string, to: string) => ["activity", "days", from, to] as const,
  activityWorkouts: (from: string, to: string) => ["activity", "workouts", from, to] as const,
  activityDay: (date: string) => ["activity", "day", date] as const,
  ouraStatus: ["activity", "oura"] as const,
  ledger: ["ledger"] as const,
  rewards: ["rewards"] as const,
  bountyDefaults: ["bounty-defaults"] as const,
  reviews: ["reviews"] as const,
  recap: ["recap"] as const,
  dashboard: ["dashboard"] as const,
  forest: (archived: boolean, lens: MapLens) =>
    ["forest", archived, lens] as const,
  edges: (archived: boolean) => ["edges", archived] as const,
};

function invalidate(qc: QueryClient, keys: readonly (readonly unknown[])[]) {
  return Promise.all(keys.map((queryKey) => qc.invalidateQueries({ queryKey })));
}

/** A food write: announce what it earned, gave back or re-scored, then refresh what it moved. */
async function foodWrite<T extends Earned>(qc: QueryClient, write: () => Promise<T>): Promise<T> {
  try {
    const result = await write();
    announceEarned(result);
    return result;
  } finally {
    await invalidate(qc, [qk.food, qk.wallet, qk.ledger, qk.domains]);
  }
}

function decorateWrites(client: LifeOsWriteClient, qc: QueryClient): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = await client.newSubject(req);
      const extra =
        req.type === "Idea" || req.type === "Problem"
          ? [qk.inbox]
          : req.type === "Area"
            ? [qk.areas]
            : req.type === "Person"
              ? [qk.people]
              : req.type === "Habit"
                ? [qk.habits]
                : [];
      await invalidate(qc, [qk.subjects, qk.dashboard, ["forest"], ["edges"], ...extra]);
      return created;
    },

    async promote(source, type, title) {
      const created = await client.promote(source, type, title);
      await invalidate(qc, [qk.inbox, qk.subjects, qk.dashboard, ["forest"], ["edges"]]);
      return created;
    },

    async setAttributes(subject, attrs) {
      await client.setAttributes(subject, attrs);
      await invalidate(qc, [
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        qk.habits,
        qk.areas,
        qk.people,
        qk.rewards,
        ["forest"],
        ["edges"],
      ]);
    },

    async rename(subject, title) {
      const renamed = await client.rename(subject, title);
      // Titles show up in nearly every read (lists, relations, inbox rows, people,
      // habits, the graph), so refresh everything rather than chase each copy.
      if (renamed.changed) await qc.invalidateQueries();
      return renamed;
    },

    async setStatus(subject, status) {
      optimisticStatus(qc, subject, status);
      try {
        const earned = await client.setStatus(subject, status);
        announceEarned(earned);
        return earned;
      } finally {
        // A status change can earn or give back (REWARD-1), so the wallet moves too; a
        // habit's graduates it off the daily list.
        await invalidate(qc, [
          qk.subject(subject),
          qk.listItem(subject),
          qk.history(subject),
          qk.subjects,
          qk.dashboard,
          qk.inbox,
          qk.habits,
          qk.wallet,
          qk.ledger,
          qk.domains,
          ["forest"],
          ["edges"],
        ]);
      }
    },

    async archive(subject) {
      await client.archive(subject);
      await invalidate(qc, [
        qk.timers,
        qk.rewards,
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        qk.inbox,
        ["forest"],
        ["edges"],
      ]);
    },

    async restore(subject) {
      await client.restore(subject);
      await invalidate(qc, [
        qk.timers,
        qk.rewards,
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        ["forest"],
        ["edges"],
      ]);
    },

    async tag(item, opts) {
      await client.tag(item, opts);
      await invalidate(qc, [qk.tags(item), qk.tagUniverse, qk.subjects, qk.inbox]);
    },

    async renameTag(from, to) {
      const renamed = await client.renameTag(from, to);
      // Every item's tag list may have changed, and the tag-universe counts with it.
      await invalidate(qc, [["tags"], qk.tagUniverse, qk.subjects, qk.inbox]);
      return renamed;
    },

    async link(from, relation, to) {
      await client.link(from, relation, to);
      await invalidate(qc, [
        qk.relations(from),
        qk.relations(to),
        ["forest"],
        ["edges"],
        qk.dashboard,
      ]);
    },

    async relate(eventId, subject, as) {
      await client.relate(eventId, subject, as);
      await invalidate(qc, [
        qk.inbox,
        qk.journal(subject),
        qk.relations(subject),
        qk.files(subject),
      ]);
    },

    async flag(item) {
      await client.flag(item);
      await invalidate(qc, [qk.inbox, qk.dashboard]);
    },

    async dismiss(item) {
      optimisticInboxRemove(qc, item);
      try {
        await client.dismiss(item);
      } finally {
        await invalidate(qc, [qk.inbox, qk.dashboard]);
      }
    },

    async drop(item) {
      optimisticInboxRemove(qc, item);
      try {
        await client.drop(item);
      } finally {
        await invalidate(qc, [qk.inbox, qk.dashboard, qk.subjects, ["forest"], ["edges"]]);
      }
    },

    async adhere(habit, state, opts) {
      optimisticAdhere(qc, habit, state === "missed" ? "not_followed" : state, opts?.on);
      try {
        const earned = await client.adhere(habit, state, opts);
        announceEarned(earned);
        return earned;
      } finally {
        await invalidate(qc, [qk.habits, qk.occurrences, qk.dashboard, qk.wallet, qk.ledger, qk.domains]);
      }
    },

    async settleDomains() {
      const earned = await client.settleDomains();
      announceEarned(earned);
      // The settle pays food days that closed themselves and activity XP that's due, too.
      if (earned.awards.length > 0) await invalidate(qc, [qk.wallet, qk.ledger, qk.domains, qk.food, qk.activity]);
      return earned;
    },

    async logFood(req) {
      return foodWrite(qc, () => client.logFood(req));
    },

    async editFood(meal, req) {
      return foodWrite(qc, () => client.editFood(meal, req));
    },

    async setFoodPhoto(meal, photo, filename) {
      return foodWrite(qc, () => client.setFoodPhoto(meal, photo, filename));
    },

    async removeFoodPhoto(meal) {
      return foodWrite(qc, () => client.removeFoodPhoto(meal));
    },

    async deleteFood(meal) {
      return foodWrite(qc, () => client.deleteFood(meal));
    },

    async fast(req) {
      return foodWrite(qc, () => client.fast(req));
    },

    async closeFoodDay(day) {
      return foodWrite(qc, () => client.closeFoodDay(day));
    },

    async settleFood() {
      const earned = await client.settleFood();
      announceEarned(earned);
      if (earned.awards.length > 0) await invalidate(qc, [qk.food, qk.wallet, qk.ledger, qk.domains]);
      return earned;
    },

    async authorizeOura(redirectUri) {
      return client.authorizeOura(redirectUri);
    },

    async connectOura(code, state) {
      try {
        return await client.connectOura(code, state);
      } finally {
        await invalidate(qc, [qk.activity]);
      }
    },

    async syncActivity(force) {
      let ran = true;
      let paid = false;
      try {
        const result = await client.syncActivity(force);
        ran = result.ran;
        // It settles activity XP even when the sync itself is skipped (activity plan A8).
        paid = result.awards.length > 0;
        announceEarned(result);
        return result;
      } finally {
        // A skipped sync that paid nothing changed nothing; a refused one may have marked the connection.
        if (paid) await invalidate(qc, [qk.activity, qk.wallet, qk.ledger, qk.domains]);
        else if (ran) await invalidate(qc, [qk.activity]);
      }
    },

    async setWorkoutNote(workout, text) {
      const row = await client.setWorkoutNote(workout, text);
      await invalidate(qc, [qk.activity]);
      return row;
    },

    async recur(habit, spec) {
      await client.recur(habit, spec);
      await invalidate(qc, [qk.habits, qk.subject(habit), qk.dashboard]);
    },

    async timer(task, action) {
      try {
        const result = await client.timer(task, action);
        announceEarned(result);
        if (action === "complete") {
          // Complete also moves the Task's Status.
          await invalidate(qc, [
            qk.subject(task),
            qk.listItem(task),
            qk.history(task),
            qk.subjects,
            qk.dashboard,
            qk.wallet,
            qk.ledger,
            qk.domains,
            ["forest"],
          ]);
        }
        return result;
      } finally {
        // Refresh even on a refusal: a 409 means another window changed the timer.
        await invalidate(qc, [qk.timers]);
      }
    },

    async involve(subject, person, role, remove) {
      await client.involve(subject, person, role, remove);
      await invalidate(qc, [qk.subject(subject), qk.people]);
    },

    async appendJournal(subject, text) {
      await client.appendJournal(subject, text);
      await invalidate(qc, [qk.journal(subject)]);
    },

    async capture(text) {
      await client.capture(text);
      await invalidate(qc, [qk.inbox, qk.dashboard]);
    },

    async captureDocument(req) {
      const result = await client.captureDocument(req);
      const extra = [
        ...(req.type === "Idea" || req.type === "Problem" ? [qk.subjects] : []),
        ...(result.subject ? [qk.files(result.subject.id)] : []),
      ];
      await invalidate(qc, [qk.inbox, qk.dashboard, ...extra]);
      return result;
    },

    async captureVoice(req) {
      const result = await client.captureVoice(req);
      const extra = [
        ...(req.type === "Idea" || req.type === "Problem" ? [qk.subjects] : []),
        ...(result.subject ? [qk.files(result.subject.id)] : []),
      ];
      await invalidate(qc, [qk.inbox, qk.dashboard, ...extra]);
      return result;
    },

    async saveReview(id, body) {
      await client.saveReview(id, body);
      await invalidate(qc, [qk.reviews]);
    },

    async completeReview(id) {
      await client.completeReview(id);
      await invalidate(qc, [qk.reviews]);
    },

    async redeem(req) {
      const redemption = await client.redeem(req);
      // The response carries the wallet after the spend: show it at once, and refresh the
      // rest without waiting, so the celebration lands with the click.
      qc.setQueryData(qk.wallet, redemption.wallet);
      void invalidate(qc, [qk.wallet, qk.ledger, qk.rewards]);
      return redemption;
    },

    async addReward(req) {
      const reward = await client.addReward(req);
      await invalidate(qc, [qk.rewards, qk.subjects]);
      return reward;
    },

    async priceReward(reward, price) {
      const row = await client.priceReward(reward, price);
      await invalidate(qc, [qk.rewards, qk.subject(reward)]);
      return row;
    },

    async targetReward(reward, targetUsd) {
      const row = await client.targetReward(reward, targetUsd);
      await invalidate(qc, [qk.rewards, qk.subject(reward)]);
      return row;
    },

    async setBounty(req) {
      const result = await client.setBounty(req);
      await invalidate(qc, [
        qk.subject(result.subject.id),
        qk.listItem(result.subject.id),
        qk.subjects,
        qk.habits,
        qk.dashboard,
      ]);
      return result;
    },

    async setHabitTarget(req) {
      const result = await client.setHabitTarget(req);
      await invalidate(qc, [qk.habits, qk.subject(result.habit.id)]);
      return result;
    },
  };
}

function matchesRef(id: string, urn: string, ref: string) {
  return id === ref || urn === ref;
}

function optimisticStatus(qc: QueryClient, subject: string, status: string) {
  qc.setQueriesData<SubjectListItem[]>(
    { queryKey: qk.subjects },
    (old) =>
      old?.map((s) =>
        matchesRef(s.id, s.urn, subject) ? { ...s, status } : s,
      ),
  );
  qc.setQueryData<SubjectDetail | null>(qk.subject(subject), (old) =>
    old ? { ...old, status } : old,
  );
  qc.setQueryData<SubjectListItem | null>(qk.listItem(subject), (old) =>
    old ? { ...old, status } : old,
  );
  qc.setQueriesData({ queryKey: ["forest"] }, (old) => {
    if (!Array.isArray(old)) return old;
    return mapForestStatus(old, subject, status);
  });
}

function optimisticInboxRemove(qc: QueryClient, item: string) {
  qc.setQueryData<InboxItem[]>(qk.inbox, (old) =>
    old?.filter(
      (i) => i.itemId !== item && i.subjectUrn !== item && i.subjectTitle !== item,
    ),
  );
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) =>
    old ? { ...old, inboxCount: Math.max(0, old.inboxCount - 1) } : old,
  );
}

function optimisticAdhere(
  qc: QueryClient,
  habit: string,
  state: Exclude<AdherenceState, "unrecorded">,
  on?: string,
) {
  const today = new Date();
  const y = today.getFullYear();
  const m = String(today.getMonth() + 1).padStart(2, "0");
  const d = String(today.getDate()).padStart(2, "0");
  const date = on ?? `${y}-${m}-${d}`;
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) => {
    if (!old) return old;
    const mark = (h: HabitOccurrenceRow) =>
      (h.habitId === habit || h.habitUrn === habit) && h.occurrenceDate === date
        ? { ...h, state }
        : h;
    return {
      ...old,
      todayHabits: old.todayHabits.map(mark),
      maintainingHabits: old.maintainingHabits.map(mark),
    };
  });
  qc.setQueriesData<HabitOccurrenceRow[]>({ queryKey: qk.occurrences }, (old) => {
    if (!old) return old;
    return old.map((o) =>
      (o.habitId === habit || o.habitUrn === habit) && o.occurrenceDate === date
        ? { ...o, state }
        : o,
    );
  });
}

/** What each earning type's triggers pay by default — fixed while the app runs. */
export function useBountyDefaults() {
  return useQuery({
    queryKey: qk.bountyDefaults,
    queryFn: () => reads().bountyDefaults(),
    staleTime: Infinity,
  });
}

export function useSubjects(type?: SubjectType, includeArchived = false) {
  return useQuery({
    queryKey: [...qk.subjects, type ?? "all", includeArchived],
    queryFn: () => reads().listSubjects({ type, includeArchived }),
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

export function useResolvedInbox(enabled = true) {
  return useQuery({
    queryKey: qk.inboxResolved,
    queryFn: () => reads().inboxResolved(),
    enabled,
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

export function useDashboard() {
  return useQuery({
    queryKey: qk.dashboard,
    queryFn: () => reads().dashboard(),
  });
}

export function useAlignmentForest(includeArchived: boolean, lens: MapLens) {
  return useQuery({
    queryKey: qk.forest(includeArchived, lens),
    queryFn: () => reads().alignmentForest({ includeArchived, lens }),
  });
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

export function useReviews(kind?: "daily" | "weekly", date?: string) {
  return useQuery({
    queryKey: [...qk.reviews, kind ?? "all", date ?? ""],
    queryFn: () => reads().reviews(kind && date ? { kind, date } : undefined),
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
