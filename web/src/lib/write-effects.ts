/**
 * What each write does besides itself (code review CR83, with CR36's and CR55's rules): the reads
 * it moves, what it says when it lands, whether it earns, and what it shows before the server
 * answers. One table for both write clients, live and mock, and one wrapper that runs every entry
 * the same way:
 *
 * - it shows its optimistic change, if it has one, then writes;
 * - once it lands it says its success (a refusal says nothing here: `useWriteAction` says that
 *   where the write was asked for), announces what it earned, and refreshes what it moved before
 *   it answers;
 * - after a failure it refreshes only when it showed something first, when the server answered
 *   409 (another window changed it), or when its entry gives a reason to.
 *
 * A write the table leaves out fails the build.
 */
import type { QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { materializedMessage } from "@/lib/appointments";
import { todayIso } from "@/lib/dates";
import type { DashboardRead } from "@/lib/derive";
import { exceptionMarkedMessage, exceptionUnmarkedMessage } from "@/lib/domains";
import { announceEarned } from "@/lib/earn-feedback";
import { ratedMessage } from "@/lib/environment";
import { foodWriteMessage } from "@/lib/food";
import { ApiRequestError } from "@/lib/live/http";
import { renamedMessage } from "@/lib/production-ui-types";
import type {
  AdherenceState,
  Award,
  DomainChange,
  FoodWriteResult,
  HabitOccurrenceRow,
  InboxItem,
  LifeOsWriteClient,
  SubjectDetail,
  SubjectListItem,
} from "@/lib/production-ui-types";
import { qk } from "@/lib/query-keys";
import { measurementEffects } from "@/lib/features/measurements/writes";
import { peopleEffects } from "@/lib/features/people/writes";
import { redemptionMessage } from "@/lib/rewards";

type Writes = LifeOsWriteClient;
type Name = keyof Writes;
type Args<K extends Name> = Parameters<Writes[K]>;
type Answer<K extends Name> = Awaited<ReturnType<Writes[K]>>;
type QueryKeys = readonly (readonly unknown[])[];

/** Titles show up in nearly every read, so a rename refreshes everything rather than chase each copy. */
const EVERYTHING = "everything";

export interface Effect<K extends Name> {
  /** The reads it moves, refreshed once it lands; after a failure it has no answer. */
  refreshes?: (args: Args<K>, answer?: Answer<K>) => QueryKeys | typeof EVERYTHING;
  /** What it says when it lands, or nothing. */
  says?: (answer: Answer<K>, args: Args<K>) => string | null | undefined;
  /** A warning it lands with, beside its success. */
  warns?: (answer: Answer<K>) => string | null | undefined;
  /** It can earn or give back: its answer's awards and level changes are announced. */
  earns?: true | { lostBecause: string };
  /** What it changes in the cache before the server answers; a failure refreshes to put it back. */
  optimistic?: (qc: QueryClient, args: Args<K>) => void;
  /** What its answer goes straight into the cache as. */
  lands?: (qc: QueryClient, answer: Answer<K>) => void;
  /** Why it refreshes after any failure, not only an optimistic write's or a 409. */
  refreshesOnFailure?: string;
  /** Its refresh runs on behind it, so it answers at once. */
  refreshesBehind?: true;
}

/**
 * The reads a kind of write moves, by name. A write refreshes the groups it moves rather than a
 * list of keys of its own, so a new read joins its group once and every write that moves it
 * refreshes it, and writes that move the same things (archive and restore) share one list.
 */
export const moves = {
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

/** What a cover change moves: the subject, and the lists and cards it shows on (the Map's among them). */
function coverMoves(id: string): QueryKeys {
  return [...moves.subject(id), qk.subjects, qk.dashboard];
}

/** A capture of an Idea or a Problem makes a subject, so the lists move too. */
function capturedMoves(type: string | undefined, subject: { id: string } | null | undefined): QueryKeys {
  return [
    qk.inbox,
    qk.dashboard,
    ...(type === "Idea" || type === "Problem" ? [qk.subjects] : []),
    ...(subject ? [qk.files(subject.id)] : []),
  ];
}

/** A food write: it says what it did and what it earned, gave back or re-scored. */
const foodWrite = {
  says: (result: FoodWriteResult) => foodWriteMessage(result),
  earns: true,
  refreshes: () => [qk.food, ...moves.earnings],
} as const;

/** Ratings feed Environment's level (HOME-2), so the domains, wallet and ledger refresh too. */
const ratingMoves = () => [qk.environment, ...moves.earnings];

/** Photos and shots (HOME-4) never move a level or the ledger: only the home's reads refresh. */
const photoMoves = () => [qk.environment];

export const writeEffects: { [K in Name]: Effect<K> } = {
  ...measurementEffects,
  ...peopleEffects,
  newSubject: {
    says: (created) => `Created ${created.title}`,
    // Made under a parent, it's on the parent's Relationships tab too (a replacement Decision's original).
    refreshes: ([req]) => [
      ...moves.subjectLists,
      ...(req.type === "Idea" || req.type === "Problem"
        ? [qk.inbox]
        : req.type === "Area"
          ? [qk.areas]
          : req.type === "Person"
            ? [qk.people]
            : req.type === "Habit"
              ? [qk.habits]
              : []),
      ...(req.parent ? [qk.relations(req.parent)] : []),
    ],
  },
  promote: { says: (_, [, type]) => `Promoted to ${type}`, refreshes: () => [qk.inbox, ...moves.subjectLists] },
  setAttributes: {
    says: () => "Saved",
    refreshes: ([subject]) => [...moves.subject(subject), ...moves.subjectLists, qk.habits, qk.areas, qk.people, qk.rewards],
  },
  rename: {
    says: (renamed) => (renamed.changed ? renamedMessage(renamed) : null),
    refreshes: (_, renamed) => (renamed?.changed ? EVERYTHING : []),
  },
  setStatus: {
    optimistic: (qc, [subject, status]) => optimisticStatus(qc, subject, status),
    says: (_, [, status]) => `Status → ${status}`,
    earns: true,
    // A status change can earn or give back (REWARD-1), so the wallet moves too; a habit's
    // graduates it off the daily list; closing a Task stops its timer.
    refreshes: ([subject]) => [
      ...moves.subject(subject),
      ...moves.subjectLists,
      ...moves.earnings,
      qk.timers,
      qk.inbox,
      qk.habits,
      qk.taskSeries,
    ],
  },
  archive: { says: () => "Archived", refreshes: ([subject]) => archiveMoves(subject) },
  restore: { says: () => "Restored", refreshes: ([subject]) => archiveMoves(subject) },
  tag: { refreshes: ([item]) => [qk.tags(item), qk.tagUniverse, qk.subjects, qk.inbox] },
  renameTag: {
    says: (renamed) => (renamed.renamed + renamed.merged > 0 ? `Renamed #${renamed.from} → #${renamed.to}` : null),
    // Every item's tag list may have changed, and the tag-universe counts with it.
    refreshes: () => [["tags"], qk.tagUniverse, qk.subjects, qk.inbox],
  },
  link: {
    says: (_, [, , , remove]) => (remove ? "Unlinked" : "Linked"),
    refreshes: ([from, , to]) => [qk.relations(from), qk.relations(to), ["edges"], qk.dashboard],
  },
  relate: {
    says: () => "Related",
    refreshes: ([, subject]) => [qk.inbox, qk.journal(subject), qk.relations(subject), qk.files(subject)],
  },
  flag: { says: () => "Flagged to Inbox", refreshes: () => [qk.inbox, qk.dashboard] },
  // Says nothing here: the Inbox shows "Dismissed" with an Undo action.
  dismiss: { optimistic: (qc, [item]) => optimisticInboxRemove(qc, item), refreshes: () => [qk.inbox, qk.dashboard] },
  drop: {
    optimistic: (qc, [item]) => optimisticInboxRemove(qc, item),
    says: () => "Dropped",
    refreshes: () => [qk.inbox, ...moves.subjectLists],
  },
  adhere: {
    optimistic: (qc, [habit, state, opts]) =>
      optimisticAdhere(qc, habit, state === "missed" ? "not_followed" : state, opts?.on),
    says: (_, [, state]) => (state === "followed" ? "Marked followed" : state === "partial" ? "Partial credit" : "Marked missed"),
    earns: true,
    refreshes: () => [qk.habits, qk.occurrences, qk.dashboard, ...moves.earnings],
  },
  settleDomains: {
    earns: true,
    // The settle pays food days that closed themselves and activity XP that's due, too.
    refreshes: (_, earned) => (earned && earned.awards.length > 0 ? [...moves.earnings, qk.food, qk.activity] : []),
  },
  markException: {
    says: (result, [, day]) => exceptionMarkedMessage(result.exception?.day ?? day),
    earns: { lostBecause: "Without that day, your last 90 days support a lower level." },
    // The exception list sits under ["domains"], so this refreshes it too. An exception day also
    // pauses the home's fades (HOME-3).
    refreshes: () => [...moves.earnings, qk.environment],
  },
  unmarkException: {
    says: (_, [day]) => exceptionUnmarkedMessage(day),
    earns: { lostBecause: "The day counts again, and your last 90 days support a lower level." },
    refreshes: () => [...moves.earnings, qk.environment],
  },
  logFood: foodWrite,
  editFood: foodWrite,
  setFoodPhoto: foodWrite,
  removeFoodPhoto: foodWrite,
  deleteFood: foodWrite,
  fast: foodWrite,
  closeFoodDay: foodWrite,
  settleFood: {
    earns: true,
    refreshes: (_, earned) => (earned && earned.awards.length > 0 ? [qk.food, ...moves.earnings] : []),
  },
  authorizeOura: {},
  connectOura: { refreshes: () => [qk.activity] },
  syncActivity: {
    earns: true,
    // It settles activity XP even when the sync itself is skipped (activity plan A8); a skipped
    // sync that paid nothing changed nothing.
    refreshes: (_, result) =>
      !result ? [qk.activity] : result.awards.length > 0 ? [qk.activity, ...moves.earnings] : result.ran ? [qk.activity] : [],
    refreshesOnFailure: "A refused sync may have marked the Oura connection for a reconnect.",
  },
  setWorkoutNote: { refreshes: () => [qk.activity] },
  addSpace: { says: (row) => `Added ${row.name}`, refreshes: () => [qk.environment, qk.domains] },
  // A weight or pace applies to all of history, so every day's score can move.
  setSpace: { says: (row) => `Saved ${row.name}`, refreshes: () => [qk.environment, qk.domains] },
  rateSpaces: { says: (result) => ratedMessage(result), earns: true, refreshes: ratingMoves },
  editSpaceRating: { says: () => "Rating saved", earns: true, refreshes: ratingMoves },
  deleteSpaceRating: { says: () => "Rating deleted", earns: true, refreshes: ratingMoves },
  // An added photo says nothing here: a batch is announced once, by the sheet that sends it.
  addSpacePhoto: { refreshes: photoMoves },
  changeSpacePhoto: { says: (_, [, , opts]) => (opts?.quiet ? null : "Photo moved"), refreshes: photoMoves },
  removeSpacePhoto: { says: () => "Photo removed", refreshes: photoMoves },
  setSpaceBaseline: { says: () => "Made the baseline", refreshes: photoMoves },
  addShot: { says: (_, [, name, opts]) => (opts?.quiet ? null : `Added the shot ${name.trim()}`), refreshes: photoMoves },
  renameShot: { says: () => "Shot renamed", refreshes: photoMoves },
  removeShot: { says: () => "Shot removed", refreshes: photoMoves },
  // On a Task it makes (or re-dates) a repetition, which shows in the lists (TASKS-3).
  recur: { refreshes: ([subject]) => [...moves.subject(subject), ...moves.subjectLists, qk.habits, qk.taskSeries] },
  materialize: { says: (result, [, through]) => materializedMessage(result, through), refreshes: () => moves.subjectLists },
  settleTasks: { refreshes: (_, result) => (result && result.made.length > 0 ? [...moves.subjectLists, qk.taskSeries] : []) },
  timer: {
    earns: true,
    // Complete also moves the Task's Status. A refusal's 409 means another window changed the timer.
    refreshes: ([task, action], result) => [
      ...(result && action === "complete"
        ? [...moves.subject(task), ...moves.subjectLists, ...moves.earnings, qk.taskSeries]
        : []),
      qk.timers,
    ],
  },
  involve: { refreshes: ([subject]) => [qk.subject(subject), qk.people] },
  appendJournal: { says: () => "Journal appended", refreshes: ([subject]) => [qk.journal(subject)] },
  capture: { says: () => "Captured to Inbox", refreshes: ([, type]) => capturedMoves(type, null) },
  captureDocument: {
    says: (result) => (result.deduplicated ? "That file is already in LifeOS" : "Captured to Inbox"),
    refreshes: ([req], result) => capturedMoves(req.type, result?.subject),
  },
  captureVoice: {
    says: (_, [req]) => (req.concerning ? "Dictated. The nightly sweep files it in your vault." : "Captured to Inbox"),
    refreshes: ([req], result) => capturedMoves(req.type, result?.subject),
  },
  openReview: { refreshes: () => [qk.reviews] },
  saveReview: { says: () => "Review saved", refreshes: () => [qk.reviews] },
  completeReview: { says: () => "Review complete", refreshes: () => [qk.reviews] },
  redeem: {
    says: (redemption) => redemptionMessage(redemption),
    // The answer carries the wallet after the spend: it shows at once, and the rest refreshes
    // behind it, so the celebration lands with the click.
    lands: (qc, redemption) => qc.setQueryData(qk.wallet, redemption.wallet),
    refreshes: () => [qk.wallet, qk.ledger, qk.rewards],
    refreshesBehind: true,
  },
  addReward: { says: (reward) => `Added ${reward.name}`, refreshes: () => [qk.rewards, qk.subjects] },
  priceReward: { says: () => "Price saved", refreshes: ([reward]) => [qk.rewards, qk.subject(reward)] },
  targetReward: {
    says: (_, [, targetUsd]) => (targetUsd === null ? "Target removed" : "Target saved"),
    refreshes: ([reward]) => [qk.rewards, qk.subject(reward)],
  },
  setBounty: {
    says: () => "Bounty saved",
    refreshes: (_, result) => (result ? [...moves.subject(result.subject.id), qk.subjects, qk.habits, qk.dashboard] : []),
  },
  setHabitTarget: {
    says: (_, [req]) => (req.clear ? "Target back on the default" : "Target saved"),
    refreshes: (_, result) => (result ? [qk.habits, qk.subject(result.habit.id)] : [qk.habits]),
  },
  setCover: {
    says: () => "Cover set",
    warns: (result) => result.downloadProblem,
    refreshes: (_, result) => (result ? coverMoves(result.subject.id) : []),
  },
  moveCover: { says: () => "Cover moved", refreshes: (_, result) => (result ? coverMoves(result.subject.id) : []) },
  clearCover: {
    says: (result) => (result.removed ? "Cover removed" : "No cover to remove"),
    refreshes: (_, result) => (result ? coverMoves(result.subject.id) : []),
  },
};

/** A write's effect with its types left open, as the wrapper runs every one. */
type AnyEffect = {
  refreshes?: (args: unknown[], answer?: unknown) => QueryKeys | typeof EVERYTHING;
  says?: (answer: unknown, args: unknown[]) => string | null | undefined;
  warns?: (answer: unknown) => string | null | undefined;
  earns?: true | { lostBecause: string };
  optimistic?: (qc: QueryClient, args: unknown[]) => void;
  lands?: (qc: QueryClient, answer: unknown) => void;
  refreshesOnFailure?: string;
  refreshesBehind?: true;
};

function refresh(qc: QueryClient, keys: QueryKeys | typeof EVERYTHING) {
  if (keys === EVERYTHING) return qc.invalidateQueries();
  return Promise.all(keys.map((queryKey) => qc.invalidateQueries({ queryKey })));
}

/** Another window changed what the write was about: what's shown is out of date. */
function isConflict(error: unknown): boolean {
  return error instanceof ApiRequestError && error.status === 409;
}

async function run(qc: QueryClient, effect: AnyEffect, write: () => Promise<unknown>, args: unknown[]): Promise<unknown> {
  effect.optimistic?.(qc, args);
  let answer: unknown;
  try {
    answer = await write();
  } catch (error) {
    if (effect.refreshes && (effect.optimistic || effect.refreshesOnFailure || isConflict(error))) {
      await refresh(qc, effect.refreshes(args));
    }
    throw error;
  }
  effect.lands?.(qc, answer);
  const message = effect.says?.(answer, args);
  if (message) toast.success(message);
  const warning = effect.warns?.(answer);
  if (warning) toast.warning(warning);
  if (effect.earns) {
    announceEarned(
      answer as { awards?: Award[]; progress?: DomainChange[] },
      effect.earns === true ? {} : { lostBecause: effect.earns.lostBecause },
    );
  }
  if (effect.refreshes) {
    const refreshed = refresh(qc, effect.refreshes(args, answer));
    if (!effect.refreshesBehind) await refreshed;
  }
  return answer;
}

/** The write client with each write running its effect from the table. Exported for its tests. */
export function decorateWrites(client: Writes, qc: QueryClient): Writes {
  const effects = writeEffects as unknown as Record<Name, AnyEffect>;
  const decorated: Partial<Record<Name, (...args: unknown[]) => Promise<unknown>>> = {};
  for (const name of Object.keys(effects) as Name[]) {
    // The client's write is looked up on each call, as a test's spy may stand in for it later.
    const write = () => client[name] as unknown as (...args: unknown[]) => Promise<unknown>;
    decorated[name] = (...args) => run(qc, effects[name], () => write().apply(client, args), args);
  }
  return decorated as unknown as Writes;
}

function matchesRef(id: string, urn: string, ref: string) {
  return id === ref || urn === ref;
}

function optimisticStatus(qc: QueryClient, subject: string, status: string) {
  // The palette's search is cached under the same key and isn't a list; it refreshes when the write lands.
  qc.setQueriesData<SubjectListItem[]>({ queryKey: qk.subjects }, (old) =>
    Array.isArray(old) ? old.map((s) => (matchesRef(s.id, s.urn, subject) ? { ...s, status } : s)) : old,
  );
  qc.setQueryData<SubjectDetail | null>(qk.subject(subject), (old) => (old ? { ...old, status } : old));
  qc.setQueryData<SubjectListItem | null>(qk.listItem(subject), (old) => (old ? { ...old, status } : old));
}

function optimisticInboxRemove(qc: QueryClient, item: string) {
  qc.setQueryData<InboxItem[]>(qk.inbox, (old) =>
    old?.filter((i) => i.itemId !== item && i.subjectUrn !== item && i.subjectTitle !== item),
  );
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) =>
    old ? { ...old, inboxCount: Math.max(0, old.inboxCount - 1) } : old,
  );
}

function optimisticAdhere(qc: QueryClient, habit: string, state: Exclude<AdherenceState, "unrecorded">, on?: string) {
  const date = on ?? todayIso();
  const mark = <T extends HabitOccurrenceRow>(h: T): T =>
    (h.habitId === habit || h.habitUrn === habit) && h.occurrenceDate === date ? { ...h, state } : h;
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) =>
    old ? { ...old, todayHabits: old.todayHabits.map(mark), maintainingHabits: old.maintainingHabits.map(mark) } : old,
  );
  qc.setQueriesData<HabitOccurrenceRow[]>({ queryKey: qk.occurrences }, (old) => old?.map(mark));
}
