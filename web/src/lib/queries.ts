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
  type QueryClient,
} from "@tanstack/react-query";
import { useMemo } from "react";
import { toast } from "sonner";
import { getAgentsClient } from "@/lib/agents";
import { materializedMessage } from "@/lib/appointments";
import { isLiveApi } from "@/lib/api-base";
import { createWriteClient, getReads } from "@/lib/client";
import { todayIso } from "@/lib/dates";
import { buildAlignmentForest, type DashboardRead, type MapLens } from "@/lib/derive";
import { exceptionMarkedMessage, exceptionUnmarkedMessage } from "@/lib/domains";
import { ratedMessage } from "@/lib/environment";
import { foodWriteMessage } from "@/lib/food";
import { redemptionMessage } from "@/lib/rewards";
import { mapSubjects } from "@/lib/task-series";
import { announceEarned } from "@/lib/earn-feedback";
import { nextCoverPage } from "@/lib/cover";
import { fetchMe, meQueryKey } from "@/lib/me";
import type { LifeOsReads } from "@/lib/reads";
import type {
  AdherenceState,
  Earned,
  FoodWriteResult,
  HabitOccurrenceRow,
  InboxItem,
  LifeOsWriteClient,
  NewSubjectRequest,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
} from "@/lib/production-ui-types";
import { renamedMessage } from "@/lib/production-ui-types";
import { errorText } from "@/lib/write-action";

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
  // Every Connected agents read sits under ["agents"], so one invalidation refreshes them all.
  agents: ["agents"] as const,
  agentsOverview: ["agents", "overview"] as const,
  agentGrants: ["agents", "grants"] as const,
  agentCalls: (clientId: string) => ["agents", "calls", clientId] as const,
  habits: ["habits"] as const,
  occurrences: ["occurrences"] as const,
  timers: ["timers"] as const,
  // Every repeating-Task read sits under ["task-series"], so a status change refreshes them all.
  taskSeries: ["task-series"] as const,
  taskSeriesHistory: (id: string) => ["task-series", "history", id] as const,
  wallet: ["wallet"] as const,
  domains: ["domains"] as const,
  // Nested under ["domains"], so every domains invalidation refreshes the chart too.
  domainDays: (domain: string, days: number) => ["domains", "days", domain, days] as const,
  domainReport: (days: number) => ["domains", "report", days] as const,
  exceptionDays: (days: number) => ["domains", "exceptions", days] as const,
  food: ["food"] as const,
  foodDay: (date: string) => ["food", "day", date] as const,
  foodDays: (from: string, to: string) => ["food", "days", from, to] as const,
  foodRecent: ["food", "recent"] as const,
  // Every Activity read sits under ["activity"], so a sync, a connect or a note refreshes them all.
  activity: ["activity"] as const,
  activitySummary: ["activity", "summary"] as const,
  activityDays: (from: string, to: string) => ["activity", "days", from, to] as const,
  activityWorkouts: (from: string, to: string) => ["activity", "workouts", from, to] as const,
  activityTags: (from: string, to: string) => ["activity", "tags", from, to] as const,
  activityDay: (date: string) => ["activity", "day", date] as const,
  ouraStatus: ["activity", "oura"] as const,
  // Every Environment read sits under ["environment"], so a rating or a space write refreshes them all.
  environment: ["environment"] as const,
  environmentDays: (days: number) => ["environment", "days", days] as const,
  spaces: ["environment", "spaces"] as const,
  spaceRatings: (space: string, days: number) => ["environment", "ratings", space, days] as const,
  spacePhotos: (space: string, shot: string) => ["environment", "photos", space, shot] as const,
  // Every Measurements read sits under ["measurements"], so a metric or a reading write refreshes them all.
  measurements: ["measurements"] as const,
  metrics: ["measurements", "metrics"] as const,
  metric: (metric: string, days: number | null) => ["measurements", "metric", metric, days ?? "all"] as const,
  labVisits: ["measurements", "visits"] as const,
  ledger: ["ledger"] as const,
  rewards: ["rewards"] as const,
  bountyDefaults: ["bounty-defaults"] as const,
  // Outside ["measurements"]: the starter list is the kernel's, so no write changes it.
  metricCatalog: ["metric-catalog"] as const,
  coverSearch: (query: string) => ["cover-search", query] as const,
  reviews: ["reviews"] as const,
  recap: ["recap"] as const,
  dashboard: ["dashboard"] as const,
  // Under the dashboard, so every subject write that refreshes Focus refreshes these too.
  appointments: (on: string) => ["dashboard", "appointments", on] as const,
  edges: (archived: boolean) => ["edges", archived] as const,
};

type QueryKeys = readonly (readonly unknown[])[];

function invalidate(qc: QueryClient, keys: QueryKeys) {
  return Promise.all(keys.map((queryKey) => qc.invalidateQueries({ queryKey })));
}

/**
 * The reads a kind of write moves, by name. A write refreshes the groups it moves rather than a
 * list of keys of its own, so a new read joins its group once and every write that moves it
 * refreshes it, and writes that move the same things (archive and restore) share one list.
 */
const moves = {
  /** One subject's own reads: its detail, its list row and its history (renames and attribute changes leave events). */
  subject: (id: string): QueryKeys => [qk.subject(id), qk.listItem(id), qk.history(id)],
  /** Everywhere subjects are listed: the lists, Focus, and the Map's edges (its outline is built from the lists). */
  subjectLists: [qk.subjects, qk.dashboard, ["edges"]] as QueryKeys,
  /** What an earn or a give-back moves (REWARD-1): the wallet, the ledger and the life domains. */
  earnings: [qk.wallet, qk.ledger, qk.domains] as QueryKeys,
  /**
   * The lists of one type that leave archived subjects out: the Inbox's Ideas and Problems, Habits
   * and their days, Areas, People, Rewards, repeating Tasks and their timers, Spaces (which leave
   * the Home Score, HOME-1) and Metrics.
   */
  typedLists: [
    qk.inbox,
    qk.habits,
    qk.occurrences,
    qk.areas,
    qk.people,
    qk.rewards,
    qk.taskSeries,
    qk.timers,
    qk.environment,
    qk.measurements,
  ] as QueryKeys,
};

/** What archiving or restoring a subject moves: the same reads either way. */
function archiveMoves(subject: string): QueryKeys {
  return [...moves.subject(subject), ...moves.subjectLists, ...moves.typedLists, qk.domains];
}

/**
 * A write's success, said here once for both write clients, live and mock (code review CR55), so
 * the two can't drift into saying different things. A refusal says nothing here: `useWriteAction`
 * says it where the write was asked for.
 */
function said(message: string) {
  toast.success(message);
}

/** A food write: say what it did and what it earned, gave back or re-scored, then refresh what it moved. */
async function foodWrite(qc: QueryClient, write: () => Promise<FoodWriteResult>): Promise<FoodWriteResult> {
  try {
    const result = await write();
    said(foodWriteMessage(result));
    announceEarned(result);
    return result;
  } finally {
    await invalidate(qc, [qk.food, ...moves.earnings]);
  }
}

/**
 * A rating write (HOME-2): announce any level or rank it moved, then refresh the home and what
 * it moved. Ratings feed Environment's level, so the domains, wallet and ledger refresh too.
 */
async function ratingWrite<T extends Earned>(
  qc: QueryClient,
  write: () => Promise<T>,
  say: (result: T) => string,
): Promise<T> {
  try {
    const result = await write();
    said(say(result));
    announceEarned(result);
    return result;
  } finally {
    await invalidate(qc, [qk.environment, ...moves.earnings]);
  }
}

/**
 * A photo or shot write (HOME-4): say what it did, unless it's one of a batch the sheet announces
 * once, then refresh the home's reads, where Then and Now live.
 */
async function photoWrite<T>(qc: QueryClient, write: () => Promise<T>, message?: string): Promise<T> {
  try {
    const result = await write();
    if (message) said(message);
    return result;
  } finally {
    await invalidate(qc, [qk.environment]);
  }
}

/** The write client with each write refreshing what it moved. Exported for its tests. */
export function decorateWrites(client: LifeOsWriteClient, qc: QueryClient): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = await client.newSubject(req);
      said(`Created ${created.title}`);
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
      // Made under a parent, it's on the parent's Relationships tab too (a replacement Decision's original).
      const parent = req.parent ? [qk.relations(req.parent)] : [];
      await invalidate(qc, [...moves.subjectLists, ...extra, ...parent]);
      return created;
    },

    async promote(source, type, title) {
      const created = await client.promote(source, type, title);
      said(`Promoted to ${type}`);
      await invalidate(qc, [qk.inbox, ...moves.subjectLists]);
      return created;
    },

    async setAttributes(subject, attrs) {
      await client.setAttributes(subject, attrs);
      said("Saved");
      await invalidate(qc, [
        ...moves.subject(subject),
        ...moves.subjectLists,
        qk.habits,
        qk.areas,
        qk.people,
        qk.rewards,
      ]);
    },

    async rename(subject, title) {
      const renamed = await client.rename(subject, title);
      if (renamed.changed) said(renamedMessage(renamed));
      // Titles show up in nearly every read (lists, relations, inbox rows, people,
      // habits, the graph), so refresh everything rather than chase each copy.
      if (renamed.changed) await qc.invalidateQueries();
      return renamed;
    },

    async setStatus(subject, status) {
      optimisticStatus(qc, subject, status);
      try {
        const earned = await client.setStatus(subject, status);
        said(`Status → ${status}`);
        announceEarned(earned);
        return earned;
      } finally {
        // A status change can earn or give back (REWARD-1), so the wallet moves too; a
        // habit's graduates it off the daily list; closing a Task stops its timer.
        await invalidate(qc, [
          ...moves.subject(subject),
          ...moves.subjectLists,
          ...moves.earnings,
          qk.timers,
          qk.inbox,
          qk.habits,
          qk.taskSeries,
        ]);
      }
    },

    async archive(subject) {
      await client.archive(subject);
      said("Archived");
      await invalidate(qc, archiveMoves(subject));
    },

    async restore(subject) {
      await client.restore(subject);
      said("Restored");
      await invalidate(qc, archiveMoves(subject));
    },

    async tag(item, opts) {
      await client.tag(item, opts);
      await invalidate(qc, [qk.tags(item), qk.tagUniverse, qk.subjects, qk.inbox]);
    },

    async renameTag(from, to) {
      const renamed = await client.renameTag(from, to);
      if (renamed.renamed + renamed.merged > 0) said(`Renamed #${renamed.from} → #${renamed.to}`);
      // Every item's tag list may have changed, and the tag-universe counts with it.
      await invalidate(qc, [["tags"], qk.tagUniverse, qk.subjects, qk.inbox]);
      return renamed;
    },

    async link(from, relation, to, remove) {
      await (remove ? client.link(from, relation, to, true) : client.link(from, relation, to));
      said(remove ? "Unlinked" : "Linked");
      await invalidate(qc, [
        qk.relations(from),
        qk.relations(to),
        ["edges"],
        qk.dashboard,
      ]);
    },

    async relate(eventId, subject, as) {
      await client.relate(eventId, subject, as);
      said("Related");
      await invalidate(qc, [
        qk.inbox,
        qk.journal(subject),
        qk.relations(subject),
        qk.files(subject),
      ]);
    },

    async flag(item) {
      await client.flag(item);
      said("Flagged to Inbox");
      await invalidate(qc, [qk.inbox, qk.dashboard]);
    },

    // Says nothing here: the Inbox shows "Dismissed" with an Undo action.
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
        said("Dropped");
      } finally {
        await invalidate(qc, [qk.inbox, ...moves.subjectLists]);
      }
    },

    async adhere(habit, state, opts) {
      optimisticAdhere(qc, habit, state === "missed" ? "not_followed" : state, opts?.on);
      try {
        const earned = await client.adhere(habit, state, opts);
        said(state === "followed" ? "Marked followed" : state === "partial" ? "Partial credit" : "Marked missed");
        announceEarned(earned);
        return earned;
      } finally {
        await invalidate(qc, [qk.habits, qk.occurrences, qk.dashboard, ...moves.earnings]);
      }
    },

    async settleDomains() {
      const earned = await client.settleDomains();
      announceEarned(earned);
      // The settle pays food days that closed themselves and activity XP that's due, too.
      if (earned.awards.length > 0) await invalidate(qc, [...moves.earnings, qk.food, qk.activity]);
      return earned;
    },

    async markException(comment, day) {
      try {
        const result = await client.markException(comment, day);
        said(exceptionMarkedMessage(result.exception?.day ?? day));
        announceEarned(result, { lostBecause: "Without that day, your last 90 days support a lower level." });
        return result;
      } finally {
        // The exception list sits under ["domains"], so this refreshes it too. An exception
        // day also pauses the home's fades (HOME-3).
        await invalidate(qc, [...moves.earnings, qk.environment]);
      }
    },

    async unmarkException(day) {
      try {
        const result = await client.unmarkException(day);
        said(exceptionUnmarkedMessage(day));
        announceEarned(result, { lostBecause: "The day counts again, and your last 90 days support a lower level." });
        return result;
      } finally {
        await invalidate(qc, [...moves.earnings, qk.environment]);
      }
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
      if (earned.awards.length > 0) await invalidate(qc, [qk.food, ...moves.earnings]);
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
        if (paid) await invalidate(qc, [qk.activity, ...moves.earnings]);
        else if (ran) await invalidate(qc, [qk.activity]);
      }
    },

    async setWorkoutNote(workout, text) {
      const row = await client.setWorkoutNote(workout, text);
      await invalidate(qc, [qk.activity]);
      return row;
    },

    async addSpace(req) {
      try {
        const row = await client.addSpace(req);
        said(`Added ${row.name}`);
        return row;
      } finally {
        await invalidate(qc, [qk.environment, qk.domains]);
      }
    },

    async setSpace(space, req) {
      try {
        const row = await client.setSpace(space, req);
        said(`Saved ${row.name}`);
        return row;
      } finally {
        // A weight or pace applies to all of history, so every day's score can move.
        await invalidate(qc, [qk.environment, qk.domains]);
      }
    },

    async rateSpaces(ratings, day) {
      return ratingWrite(qc, () => client.rateSpaces(ratings, day), ratedMessage);
    },

    async editSpaceRating(rating, req) {
      return ratingWrite(qc, () => client.editSpaceRating(rating, req), () => "Rating saved");
    },

    async deleteSpaceRating(rating) {
      return ratingWrite(qc, () => client.deleteSpaceRating(rating), () => "Rating deleted");
    },

    // Photos and shots (HOME-4) never move a level or the ledger: only the home's reads refresh.
    // An added photo says nothing here: a batch is announced once, by the sheet that sends it.
    async addSpacePhoto(space, photo, req) {
      return photoWrite(qc, () => client.addSpacePhoto(space, photo, req));
    },

    async changeSpacePhoto(photo, req, opts) {
      return photoWrite(qc, () => client.changeSpacePhoto(photo, req, opts), opts?.quiet ? undefined : "Photo moved");
    },

    async removeSpacePhoto(photo) {
      return photoWrite(qc, () => client.removeSpacePhoto(photo), "Photo removed");
    },

    async setSpaceBaseline(photo) {
      return photoWrite(qc, () => client.setSpaceBaseline(photo), "Made the baseline");
    },

    async addShot(space, name, opts) {
      return photoWrite(qc, () => client.addShot(space, name, opts), opts?.quiet ? undefined : `Added the shot ${name.trim()}`);
    },

    async renameShot(space, shot, name) {
      return photoWrite(qc, () => client.renameShot(space, shot, name), "Shot renamed");
    },

    async removeShot(space, shot) {
      return photoWrite(qc, () => client.removeShot(space, shot), "Shot removed");
    },

    // Readings are information only (D20): a write moves no level, so only Measurements refreshes.
    async addMetric(req) {
      try {
        const row = await client.addMetric(req);
        said(`Added ${row.name}`);
        return row;
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async setMetric(metric, req) {
      try {
        const row = await client.setMetric(metric, req);
        said(`Saved ${row.name}`);
        return row;
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    // A reading write says nothing here: the sheet announces it with the metric's decimals.
    async logReading(req) {
      try {
        return await client.logReading(req);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async logReadings(req) {
      try {
        return await client.logReadings(req);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async editReading(reading, req) {
      try {
        return await client.editReading(reading, req);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async deleteReading(reading) {
      try {
        return await client.deleteReading(reading);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    // A visit write makes tests and moves their ranges, so all of Measurements refreshes too. It
    // says nothing here either: the sheet announces it once its report is in.
    async logLabVisit(req) {
      try {
        return await client.logLabVisit(req);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async saveLabVisit(visit, req) {
      try {
        return await client.saveLabVisit(visit, req);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async deleteLabVisit(visit) {
      try {
        return await client.deleteLabVisit(visit);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async attachLabReport(visit, report, filename) {
      try {
        return await client.attachLabReport(visit, report, filename);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async removeLabReport(visit) {
      try {
        return await client.removeLabReport(visit);
      } finally {
        await invalidate(qc, [qk.measurements]);
      }
    },

    async recur(subject, spec, opts) {
      await client.recur(subject, spec, opts);
      // On a Task it makes (or re-dates) a repetition, which shows in the lists (TASKS-3).
      await invalidate(qc, [...moves.subject(subject), ...moves.subjectLists, qk.habits, qk.taskSeries]);
    },

    async materialize(series, through) {
      const result = await client.materialize(series, through);
      said(materializedMessage(result, through));
      await invalidate(qc, moves.subjectLists);
      return result;
    },

    async settleTasks() {
      const result = await client.settleTasks();
      if (result.made.length > 0) {
        await invalidate(qc, [...moves.subjectLists, qk.taskSeries]);
      }
      return result;
    },

    async timer(task, action) {
      try {
        const result = await client.timer(task, action);
        announceEarned(result);
        if (action === "complete") {
          // Complete also moves the Task's Status.
          await invalidate(qc, [...moves.subject(task), ...moves.subjectLists, ...moves.earnings, qk.taskSeries]);
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
      said("Journal appended");
      await invalidate(qc, [qk.journal(subject)]);
    },

    async capture(text, type) {
      await client.capture(text, type);
      said("Captured to Inbox");
      const made = type === "Idea" || type === "Problem" ? [qk.subjects] : [];
      await invalidate(qc, [qk.inbox, qk.dashboard, ...made]);
    },

    async captureDocument(req) {
      const result = await client.captureDocument(req);
      said(result.deduplicated ? "That file is already in LifeOS" : "Captured to Inbox");
      const extra = [
        ...(req.type === "Idea" || req.type === "Problem" ? [qk.subjects] : []),
        ...(result.subject ? [qk.files(result.subject.id)] : []),
      ];
      await invalidate(qc, [qk.inbox, qk.dashboard, ...extra]);
      return result;
    },

    async captureVoice(req) {
      const result = await client.captureVoice(req);
      said(req.concerning ? "Dictated. The nightly sweep files it in your vault." : "Captured to Inbox");
      const extra = [
        ...(req.type === "Idea" || req.type === "Problem" ? [qk.subjects] : []),
        ...(result.subject ? [qk.files(result.subject.id)] : []),
      ];
      await invalidate(qc, [qk.inbox, qk.dashboard, ...extra]);
      return result;
    },

    async openReview(kind, date) {
      const doc = await client.openReview(kind, date);
      await invalidate(qc, [qk.reviews]);
      return doc;
    },

    async saveReview(id, body) {
      await client.saveReview(id, body);
      said("Review saved");
      await invalidate(qc, [qk.reviews]);
    },

    async completeReview(id) {
      await client.completeReview(id);
      said("Review complete");
      await invalidate(qc, [qk.reviews]);
    },

    async redeem(req) {
      const redemption = await client.redeem(req);
      said(redemptionMessage(redemption));
      // The response carries the wallet after the spend: show it at once, and refresh the
      // rest without waiting, so the celebration lands with the click.
      qc.setQueryData(qk.wallet, redemption.wallet);
      void invalidate(qc, [qk.wallet, qk.ledger, qk.rewards]);
      return redemption;
    },

    async addReward(req) {
      const reward = await client.addReward(req);
      said(`Added ${reward.name}`);
      await invalidate(qc, [qk.rewards, qk.subjects]);
      return reward;
    },

    async priceReward(reward, price) {
      const row = await client.priceReward(reward, price);
      said("Price saved");
      await invalidate(qc, [qk.rewards, qk.subject(reward)]);
      return row;
    },

    async targetReward(reward, targetUsd) {
      const row = await client.targetReward(reward, targetUsd);
      said(targetUsd === null ? "Target removed" : "Target saved");
      await invalidate(qc, [qk.rewards, qk.subject(reward)]);
      return row;
    },

    async setBounty(req) {
      const result = await client.setBounty(req);
      said("Bounty saved");
      await invalidate(qc, [...moves.subject(result.subject.id), qk.subjects, qk.habits, qk.dashboard]);
      return result;
    },

    async setHabitTarget(req) {
      const result = await client.setHabitTarget(req);
      said(req.clear ? "Target back on the default" : "Target saved");
      await invalidate(qc, [qk.habits, qk.subject(result.habit.id)]);
      return result;
    },

    async setCover(req) {
      const result = await client.setCover(req);
      said("Cover set");
      if (result.downloadProblem) toast.warning(result.downloadProblem);
      await invalidate(qc, coverKeys(result.subject.id));
      return result;
    },

    async moveCover(subject, positionY) {
      const result = await client.moveCover(subject, positionY);
      said("Cover moved");
      await invalidate(qc, coverKeys(result.subject.id));
      return result;
    },

    async clearCover(subject) {
      const result = await client.clearCover(subject);
      said(result.removed ? "Cover removed" : "No cover to remove");
      await invalidate(qc, coverKeys(result.subject.id));
      return result;
    },
  };
}

/** What a cover change moves: the subject, and the lists and cards it shows on (the Map's among them). */
function coverKeys(id: string): QueryKeys {
  return [...moves.subject(id), qk.subjects, qk.dashboard];
}

function matchesRef(id: string, urn: string, ref: string) {
  return id === ref || urn === ref;
}

function optimisticStatus(qc: QueryClient, subject: string, status: string) {
  // The palette's search is cached under the same key and isn't a list; it refreshes when the write lands.
  qc.setQueriesData<SubjectListItem[]>({ queryKey: qk.subjects }, (old) =>
    Array.isArray(old) ? old.map((s) => (matchesRef(s.id, s.urn, subject) ? { ...s, status } : s)) : old,
  );
  qc.setQueryData<SubjectDetail | null>(qk.subject(subject), (old) =>
    old ? { ...old, status } : old,
  );
  qc.setQueryData<SubjectListItem | null>(qk.listItem(subject), (old) =>
    old ? { ...old, status } : old,
  );
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
  const date = on ?? todayIso();
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

/** The starter list, from the kernel (code review CR73): fixed while the app runs. */
export function useMetricCatalog() {
  return useQuery({
    queryKey: qk.metricCatalog,
    queryFn: () => reads().metricCatalog(),
    staleTime: Infinity,
  });
}

/** Every metric, archived ones too: the Measurements page finds Weight among them and groups the rest. */
export function useMetrics(enabled = true) {
  return useQuery({
    queryKey: qk.metrics,
    queryFn: () => reads().metrics(),
    enabled,
  });
}

/**
 * One metric over the last `days` days (all history when null): the chart, the table and the
 * goal; the last range stays up while the next loads.
 */
export function useMetric(metric: string | null, days: number | null, enabled = true) {
  return useQuery({
    queryKey: qk.metric(metric ?? "", days),
    queryFn: () => reads().metric(metric!, days),
    enabled: enabled && metric != null,
    placeholderData: keepPreviousData,
  });
}

/**
 * Several metrics over the same range, read together: blood pressure's two on one card and in
 * one row (MEASURE-3). Each shares `useMetric`'s cache, so a card and its row read once.
 */
export function useMetricsOver(metrics: readonly string[], days: number | null, enabled = true) {
  return useQueries({
    queries: metrics.map((metric) => ({
      queryKey: qk.metric(metric, days),
      queryFn: () => reads().metric(metric, days),
      enabled,
      placeholderData: keepPreviousData,
    })),
    combine: (results) => ({
      data: results.every((r) => r.data) ? results.map((r) => r.data!) : undefined,
      isLoading: results.some((r) => r.isLoading),
      isError: results.some((r) => r.isError),
      isPlaceholderData: results.some((r) => r.isPlaceholderData),
    }),
  });
}

/** Every lab visit with its results, newest first (MEASURE-4): the Lab results section and the visit sheet. */
export function useLabVisits(enabled = true) {
  return useQuery({
    queryKey: qk.labVisits,
    queryFn: () => reads().labVisits(),
    enabled,
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
