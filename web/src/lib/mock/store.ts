import { objectUrlFor, revokeIfBlobUrl } from "@/lib/artifacts";
import { deriveTitle } from "@/lib/capture";
import {
  ACTIVITY_RULES,
  exerciseSeconds,
  exerciseXp,
  sleepXp,
  stepsXp,
} from "@/lib/activity-score";
import { ouraDayAt } from "@/lib/activity-xp";
import { mockActivityRecords, resetActivityStore } from "@/lib/mock/activity-store";
import { mockHomeScoreUnits, resetEnvironmentStore } from "@/lib/mock/environment-store";
import { resetMeasurementsStore } from "@/lib/mock/measurements-store";
import {
  addDays,
  nowIso,
  slugify,
  todayIso,
  startOfWeekMonday,
  endOfWeekSunday,
} from "@/lib/dates";
import { noteDay } from "@/lib/mock/food-fold";
import type {
  ActivityPaid,
  Amounts,
  AppointmentRow,
  ArtifactRecord,
  Award,
  BountyRequest,
  BountyResult,
  CoverCleared,
  CoverRead,
  CoverRequest,
  CoverResult,
  HabitEstablishment,
  HabitTargetRequest,
  HabitTargetResult,
  HabitRow,
  BinaryCaptureResult,
  CaptureKind,
  CreatedSubject,
  DomainDaysRead,
  DomainReportRead,
  DomainsRead,
  Earned,
  ExceptionDayResult,
  ExceptionDaysRead,
  EventRelation,
  InboxItem,
  InboxResolution,
  LedgerDay,
  LedgerRow,
  NewRewardRequest,
  NewSubjectRequest,
  RecurrenceSpec,
  RedeemRequest,
  Redemption,
  Relation,
  RenamedSubject,
  RenamedTag,
  ReviewBody,
  ReviewDoc,
  RewardRow,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
  TaskSeriesHistory,
  TaskSeriesRow,
  TaskSettleResult,
  TaskTimer,
  TimerAction,
  TimerActionResult,
  Wallet,
} from "@/lib/production-ui-types";
import { initialStatus, isTerminal } from "@/lib/production-ui-types";
import { canHaveCover, DEFAULT_POSITION_Y } from "@/lib/cover";
import { readStoredCover, sceneById, storedCoverOf } from "@/lib/mock/covers";
import {
  BOUNTY_TRIGGERS,
  STREAK_MILESTONES,
  TIMED,
  bountyJson,
  earns,
  linesFor,
  parseBounty,
  parseEveryMinutes,
  priceOf,
  priceTimed,
} from "@/lib/bounty";
import {
  applyMultiplier,
  domainChanges,
  DomainProgress,
  gemsFor,
  RANK_TRIGGER,
  rankAwardKey,
  rulesFor,
  toDomainDays,
  toDomainReportRow,
  toDomainRow,
} from "@/lib/mock/domain-fold";
import { applyStreakToHabit } from "@/lib/mock/habit-streak";
import { isOccurrenceDate, parseRecurrence, serializeRecurrence } from "@/lib/recurrence";
import { sortAppointments } from "@/lib/appointments";
import { taskPatternProblem } from "@/lib/task-series";
import {
  afterPattern,
  checkSeriesStatus,
  COPIED_KEYS,
  copiedValues,
  ensureSeries,
  guardPattern,
  isSeries,
  openRepetitionIds,
  repeatOf,
  repetitionRows,
  seriesOf,
  seriesRow,
  setStart,
  settleAll,
  valueOf,
} from "@/lib/mock/task-series";
import {
  creditDays,
  creditedOnFor,
  foldDomains,
  localDay,
  settledThrough,
} from "@/lib/mock/domain-credit";
import { CURRENCIES, coinsToUsd, currency, orderedAmounts, shortfall } from "@/lib/rewards";
import { foldTimer, timedSecondsBefore, type TimerEvent } from "@/lib/timer";
import {
  createSeed,
  type CanonicalEdge,
  type MockLedgerEntry,
  type MockReview,
  type MockState,
} from "@/lib/mock/seed";
import { reviewPeriod, reviewStateAsOf, whyNotOpen } from "@/lib/reviews";
import { heldKeyed, latestBase, planKeyed, type DueFood } from "@/lib/mock/food-store";

let state: MockState = createSeed();
const listeners = new Set<() => void>();

export function getState(): MockState {
  return state;
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function emit() {
  for (const listener of listeners) listener();
}

export function resetStore() {
  revokeArtifacts(state);
  state = createSeed();
  resetActivityStore();
  resetEnvironmentStore();
  resetMeasurementsStore();
  emit();
}

function revokeArtifacts(s: MockState) {
  for (const artifact of Object.values(s.artifacts)) {
    revokeIfBlobUrl(artifact.bytesUrl);
  }
}

export function byId(id: string): SubjectListItem | undefined {
  return state.subjects.find((s) => s.id === id);
}

export function byUrn(urn: string): SubjectListItem | undefined {
  return state.subjects.find((s) => s.urn === urn);
}

export function byRef(ref: string): SubjectListItem | undefined {
  return byId(ref) ?? byUrn(ref);
}

export function newId(): string {
  return crypto.randomUUID();
}

function shortId(): string {
  return Math.random().toString(36).slice(2, 8);
}

export function makeUrn(type: SubjectType, title: string): string {
  return `urn:bsk:${type.toLowerCase()}:${slugify(title)}-${shortId()}`;
}

export function createSubject(req: NewSubjectRequest): CreatedSubject {
  const id = newId();
  const urn = makeUrn(req.type, req.title);
  const area = req.area ? state.areas.find((a) => a.urn === req.area || a.id === req.area) : undefined;
  // A new Habit is Planned, as the kernel writes it; the rest start at their default.
  const status = initialStatus(req.type) || null;
  const createdAt = nowIso();
  const list: SubjectListItem = {
    id,
    urn,
    type: req.type,
    title: req.title,
    status,
    archived: false,
    createdAt,
    area: area?.urn ?? req.area,
    areaName: area?.name,
  };
  const attrs = req.attrs ?? {};
  if (attrs.due) list.due = attrs.due;
  if (attrs.scheduled) list.scheduled = attrs.scheduled;
  if (attrs.target_date) list.targetDate = attrs.target_date;
  if (attrs.date && req.type === "Appointment") list.due = attrs.date;
  if (attrs.person_kind) list.personKind = attrs.person_kind;
  const detail: SubjectDetail = {
    id,
    urn,
    type: req.type,
    title: req.title,
    status,
    createdAt,
    archived: false,
    area: list.area,
    areaName: list.areaName,
    due: list.due,
    targetDate: list.targetDate,
    statement: attrs.statement,
    scope: attrs.scope ?? attrs.description,
    attributes: JSON.stringify(attrs),
  };
  state.subjects = [list, ...state.subjects];
  state.details[id] = detail;
  state.tagsByItem[id] = [];
  state.journals[id] = [];
  state.history[id] = status
    ? [{ id: newId(), status, occurredAt: createdAt }]
    : [];

  if (req.parent) {
    const parent = byRef(req.parent);
    if (parent && req.relation) {
      addEdge(id, req.relation, parent.id);
    }
  }

  applyAttrs(id, attrs);
  if (req.type === "Area") {
    state.areas = [
      {
        id,
        urn,
        name: req.title,
        description: attrs.description,
        notes: attrs.notes,
        createdAt,
      },
      ...state.areas,
    ];
  }
  if (req.type === "Person") {
    state.people = [
      {
        id,
        urn,
        title: req.title,
        personKind: attrs.person_kind ?? "human",
        role: attrs.role,
        archived: false,
      },
      ...state.people,
    ];
  }
  if (req.type === "Habit") {
    const allowsPartial = attrs.allows_partial !== "false";
    state.habits = [
      {
        id,
        urn,
        name: req.title,
        cue: attrs.cue,
        routine: attrs.routine,
        reward: attrs.reward,
        startDate: attrs.start,
        endDate: attrs.end,
        allowsPartial,
        recurrence: attrs.recurrence ?? JSON.stringify({ freq: "daily" }),
        archived: false,
        createdAt,
        currentStreak: 0,
        lastState: "unrecorded",
      },
      ...state.habits,
    ];
    // Planned: nothing is scheduled until it starts being tracked (see earnStatus).
  }
  if (req.type === "Idea" || req.type === "Problem") {
    state.inbox = [
      {
        itemId: newId(),
        itemKind: "subject",
        triagedAt: createdAt,
        subjectUrn: urn,
        subjectType: req.type,
        subjectTitle: req.title,
      },
      ...state.inbox,
    ];
  }
  emit();
  return { id, urn, type: req.type, title: req.title };
}

export function addEdge(fromId: string, relation: Relation, toId: string) {
  const exists = state.edges.some(
    (e) => e.fromId === fromId && e.toId === toId && e.relation === relation,
  );
  if (exists) return;
  state.edges = [...state.edges, { fromId, relation, toId }];
}

export function removeEdge(fromId: string, relation: Relation, toId: string) {
  state.edges = state.edges.filter(
    (e) => !(e.fromId === fromId && e.toId === toId && e.relation === relation),
  );
}

export function applyAttrs(subjectId: string, attrs: Record<string, string>) {
  if (Object.keys(attrs).some((key) => key.trim().toLowerCase() === "cover")) {
    throw new Error("Cover is set with `bsk cover` (a photo from Unsplash), not `bsk set`.");
  }
  const list = byId(subjectId);
  const detail = state.details[subjectId];
  if (!list || !detail) return;
  // A repeating Task's edit follows to its open repetitions still holding the old value (D16).
  const before = isSeries(state, subjectId) ? copiedValues(state, subjectId) : null;
  const next = { ...list };
  const nextDetail = { ...detail };
  // The whole bag, not just its strings: a Reward's price is an object.
  const bag = rawAttrs(detail.attributes);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === "") {
      delete bag[key];
      if (key === "due" || key === "date") next.due = null;
      if (key === "scheduled") next.scheduled = null;
      if (key === "target_date") next.targetDate = null;
      if (key === "statement") nextDetail.statement = null;
      if (key === "scope" || key === "description" || key === "desired_end_state") {
        nextDetail.scope = null;
      }
      if (key === "area") {
        next.area = null;
        next.areaName = null;
      }
      if (key === "person_kind") next.personKind = null;
      continue;
    }
    bag[key] = value;
    if (key === "due") next.due = value;
    if (key === "date" && next.type === "Appointment") next.due = value;
    if (key === "scheduled") next.scheduled = value;
    if (key === "target_date") next.targetDate = value;
    if (key === "statement") nextDetail.statement = value;
    if (key === "scope" || key === "description" || key === "desired_end_state") {
      nextDetail.scope = value;
    }
    if (key === "area") {
      const area = state.areas.find((a) => a.urn === value || a.id === value);
      next.area = area?.urn ?? value;
      next.areaName = area?.name ?? next.areaName;
    }
    if (key === "person_kind") next.personKind = value;
  }
  nextDetail.due = next.due ?? null;
  nextDetail.targetDate = next.targetDate ?? null;
  nextDetail.area = next.area;
  nextDetail.areaName = next.areaName;
  nextDetail.attributes = JSON.stringify(bag);
  state.subjects = state.subjects.map((s) => (s.id === subjectId ? next : s));
  state.details[subjectId] = nextDetail;
  syncSpecial(subjectId);
  if (before) {
    for (const key of COPIED_KEYS) {
      if (!(key in attrs) || (attrs[key] || undefined) === before[key]) continue;
      for (const id of openRepetitionIds(state, list.urn)) {
        if (valueOf(state, id, key) === before[key]) applyAttrs(id, { [key]: attrs[key] });
      }
    }
  }
}

function syncSpecial(subjectId: string) {
  const list = byId(subjectId);
  const detail = state.details[subjectId];
  if (!list || !detail) return;
  const attrs = safeAttrs(detail.attributes);
  if (list.type === "Habit") {
    state.habits = state.habits.map((h) =>
      h.id === subjectId
        ? {
            ...h,
            name: list.title,
            cue: attrs.cue ?? h.cue,
            routine: attrs.routine ?? h.routine,
            reward: attrs.reward ?? h.reward,
            startDate: attrs.start ?? h.startDate,
            endDate: attrs.end ?? h.endDate,
            allowsPartial: attrs.allows_partial ? attrs.allows_partial !== "false" : h.allowsPartial,
            recurrence: attrs.recurrence ?? h.recurrence,
          }
        : h,
    );
  }
  if (list.type === "Person") {
    state.people = state.people.map((p) =>
      p.id === subjectId
        ? { ...p, title: list.title, personKind: attrs.person_kind ?? p.personKind, role: attrs.role ?? p.role }
        : p,
    );
  }
  if (list.type === "Area") {
    state.areas = state.areas.map((a) =>
      a.id === subjectId
        ? { ...a, name: list.title, description: attrs.description, notes: attrs.notes }
        : a,
    );
  }
}

function rawAttrs(raw?: string | null): Record<string, unknown> {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? { ...(parsed as Record<string, unknown>) }
      : {};
  } catch {
    return {};
  }
}

function safeAttrs(raw?: string | null): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(parsed)) {
      if (typeof v === "string") out[k] = v;
    }
    return out;
  } catch {
    return {};
  }
}

/** A status change, answering what it earned or gave back. */
export function setStatus(subject: string, status: string): Award[] {
  return earnStatus(subject, status).awards;
}

/** A status change, answering what it earned or gave back and each domain it moved (§3.8). */
export function earnStatus(subject: string, status: string): Earned {
  const item = byRef(subject);
  if (!item) return { awards: [], progress: [] };
  // A repeating Task's series is Not started or Cancelled; moving it back restarts it from today.
  checkSeriesStatus(state, item.id, status);
  const series = isSeries(state, item.id) ? item : seriesOf(state, item.id);
  const restarting = series?.id === item.id && isTerminal(item.status) && !isTerminal(status);
  // A habit leaving Planned starts being tracked today (migration 0049).
  if (item.type === "Habit" && isPlanned(item.status) && !isPlanned(status)) scheduleToday(item.id);
  const occurredAt = nowIso();
  // Closing a Task stops its timer first, as StatusService does (TASKS-2).
  if (item.type === "Task" && isTerminal(status) && (timerFor(item.id)?.state ?? "stopped") !== "stopped") {
    state.timerEvents = [...state.timerEvents, { subjectId: item.id, action: "stop", occurredAt }];
  }
  state.subjects = state.subjects.map((s) =>
    s.id === item.id ? { ...s, status } : s,
  );
  const detail = state.details[item.id];
  if (detail) state.details[item.id] = { ...detail, status };
  state.history[item.id] = [
    { id: newId(), status, occurredAt },
    ...(state.history[item.id] ?? []),
  ];
  const earned = earning(() => reconcileStatus(item.id, occurredAt));
  // A repetition that closed makes the next (TASKS-3).
  if (series) {
    if (restarting) setStart(state, series.id, todayIso());
    ensureSeries(state, series.id, todayIso());
  }
  emit();
  return earned;
}

/** A task's timer as the kernel folds it, or null when it was never started. */
export function timerFor(subjectId: string): TaskTimer | null {
  const item = byId(subjectId);
  return item ? foldTimer(state.timerEvents, item) : null;
}

/** Every timed, unarchived task's timer — running first, then most recently changed. */
export function listTimers(): TaskTimer[] {
  const rank = (t: TaskTimer) => (t.state === "running" ? 0 : t.state === "paused" ? 1 : 2);
  return [...new Set(state.timerEvents.map((e) => e.subjectId))]
    .map((id) => byId(id))
    .filter((item): item is SubjectListItem => !!item && !item.archived)
    .map((item) => foldTimer(state.timerEvents, item)!)
    .sort((a, b) => rank(a) - rank(b) || b.lastChangedAt.localeCompare(a.lastChangedAt));
}

/**
 * Drive a Task's timer the way `TimerService` does: the same allowed transitions and
 * refusals, one running timer at a time (starting or resuming pauses the other),
 * Complete = stop, then Status → Completed, and no Start or Resume on a closed Task.
 */
export function applyTimer(taskRef: string, action: TimerAction): TimerActionResult {
  const task = byRef(taskRef);
  if (!task) throw new Error(`No subject matches '${taskRef}'.`);
  if (task.type !== "Task") {
    throw new Error(`'${task.title}' is a ${task.type}, not a Task; the timer is kept on Tasks.`);
  }
  if (isSeries(state, task.id)) {
    throw new Error(`'${task.title}' is a repeating task; time one of its repetitions instead.`);
  }
  const current = timerFor(task.id)?.state ?? "stopped";
  const at = nowIso();
  const paused: CreatedSubject[] = [];

  if (action === "complete") {
    if (current !== "stopped") {
      state.timerEvents = [...state.timerEvents, { subjectId: task.id, action: "stop", occurredAt: at }];
    }
    const { awards, progress } = earnStatus(task.id, "Completed");
    return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: true, awards, progress };
  }

  const allowed =
    (action === "start" && current === "stopped") ||
    (action === "pause" && current === "running") ||
    (action === "resume" && current === "paused") ||
    (action === "stop" && current !== "stopped");
  if (!allowed) {
    const reason =
      action === "start" && current === "paused"
        ? "it is paused — resume it, or stop it first"
        : current === "running" && (action === "start" || action === "resume")
          ? "it is already running"
          : action === "resume" && current === "stopped"
            ? "it is stopped — start it instead"
            : current === "stopped"
              ? "it is not running"
              : `it is ${current}`;
    throw new Error(`Can't ${action} the timer on '${task.title}': ${reason}.`);
  }
  // A closed Task isn't timed until it's reopened; Pause and Stop still work.
  if ((action === "start" || action === "resume") && isTerminal(task.status)) {
    throw new Error(`Can't ${action} the timer on '${task.title}': it's ${task.status}. Reopen it to time it.`);
  }

  const events: TimerEvent[] = [];
  if (action === "start" || action === "resume") {
    // Archived or not, a running timer is spending time, so it is paused too.
    for (const otherId of new Set(state.timerEvents.map((e) => e.subjectId))) {
      if (otherId === task.id || timerFor(otherId)?.state !== "running") continue;
      events.push({ subjectId: otherId, action: "pause", occurredAt: at });
      const row = byId(otherId);
      if (row) paused.push({ id: row.id, urn: row.urn, type: row.type, title: row.title });
    }
  }
  events.push({ subjectId: task.id, action, occurredAt: at });
  state.timerEvents = [...state.timerEvents, ...events];
  emit();
  return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: false, awards: [], progress: [] };
}

function neverTimed(task: SubjectListItem): TaskTimer {
  return {
    subjectId: task.id,
    subjectUrn: task.urn,
    subjectTitle: task.title,
    state: "stopped",
    runningSince: null,
    totalSeconds: 0,
    sessionSeconds: 0,
    sessionStartedAt: null,
    sessions: 0,
    lastChangedAt: nowIso(),
  };
}

export function archiveSubject(subject: string, archived: boolean) {
  const item = byRef(subject);
  if (!item) return;
  state.subjects = state.subjects.map((s) =>
    s.id === item.id ? { ...s, archived } : s,
  );
  const detail = state.details[item.id];
  if (detail) state.details[item.id] = { ...detail, archived };
  if (item.type === "Habit") {
    state.habits = state.habits.map((h) =>
      h.id === item.id ? { ...h, archived } : h,
    );
  }
  // A repeating Task restored from the archive repeats again, from today (D16).
  if (!archived && isSeries(state, item.id)) {
    setStart(state, item.id, todayIso());
    ensureSeries(state, item.id, todayIso());
  }
  emit();
}

export function setTags(itemRef: string, add: string[] = [], remove: string[] = []) {
  const item = byRef(itemRef);
  const key = item?.id ?? itemRef;
  const current = new Set(state.tagsByItem[key] ?? []);
  // Normalized like the kernel (Tags.Normalize): trimmed and lowercased.
  for (const t of add) current.add(t.trim().toLowerCase());
  for (const t of remove) current.delete(t.trim().toLowerCase());
  state.tagsByItem[key] = [...current].filter(Boolean).sort();
  if (item) {
    state.subjects = state.subjects.map((s) =>
      s.id === item.id
        ? { ...s, tags: state.tagsByItem[key].join(", ") || null }
        : s,
    );
    // A repeating Task's tags follow to its open repetitions.
    if (isSeries(state, item.id)) {
      for (const id of openRepetitionIds(state, item.urn)) setTags(id, add, remove);
    }
  }
  emit();
}

/**
 * Rename a subject, as `bsk rename` does: the urn stays, the title changes. The live
 * reader joins titles on read, but the mock copies them into several rows, so each
 * copy is patched here. A series rename also renames its upcoming occurrences that
 * still carry the old title. Like the kernel, two Problems may not share a title.
 */
export function renameSubject(subjectRef: string, rawTitle: string): RenamedSubject {
  const item = byRef(subjectRef);
  if (!item) throw new Error(`No subject matches '${subjectRef}'.`);
  const title = rawTitle.trim().split(/\s+/).join(" ");
  if (!title) throw new Error("A new title is required.");
  const previousTitle = item.title;
  if (title === previousTitle) {
    return { id: item.id, title, previousTitle, changed: false, occurrencesRenamed: 0 };
  }
  if (
    item.type === "Problem" &&
    state.subjects.some((s) => s.type === "Problem" && s.id !== item.id && s.title === title)
  ) {
    throw new Error(`A Problem subject titled '${title}' already exists.`);
  }

  const today = todayIso();
  const occurrenceIds =
    item.type === "Appointment"
      ? state.subjects
          .filter(
            (s) =>
              s.type === "Appointment" &&
              s.title === previousTitle &&
              safeAttrs(state.details[s.id]?.attributes).series === item.urn &&
              (s.due ?? "") >= today,
          )
          .map((s) => s.id)
      : // A repeating Task's open repetitions still carrying the old title follow (D16).
        isSeries(state, item.id)
        ? openRepetitionIds(state, item.urn).filter((id) => byId(id)?.title === previousTitle)
        : [];
  for (const id of [item.id, ...occurrenceIds]) retitle(id, title);
  emit();
  return { id: item.id, title, previousTitle, changed: true, occurrencesRenamed: occurrenceIds.length };
}

function retitle(id: string, title: string) {
  const item = byId(id);
  if (!item) return;
  state.subjects = state.subjects.map((s) => {
    if (s.id === id) return { ...s, title };
    // Items filed under a renamed Area show its new name.
    if (item.type === "Area" && s.area === item.urn) return { ...s, areaName: title };
    return s;
  });
  for (const [key, detail] of Object.entries(state.details)) {
    if (key === id) state.details[key] = { ...detail, title };
    else if (item.type === "Area" && detail.area === item.urn) state.details[key] = { ...detail, areaName: title };
  }
  syncSpecial(id);
  state.occurrences = state.occurrences.map((o) => (o.habitId === id ? { ...o, habitName: title } : o));
  state.inbox = state.inbox.map((i) => (i.subjectUrn === item.urn ? { ...i, subjectTitle: title } : i));
  state.resolved = state.resolved.map((r) =>
    r.row.subjectUrn === item.urn ? { ...r, row: { ...r.row, subjectTitle: title } } : r,
  );
  state.associations = state.associations.map((a) => ({
    ...a,
    subjectTitle: a.subjectId === id ? title : a.subjectTitle,
    personName: a.personId === id ? title : a.personName,
  }));
  if (state.primaryFocus.subjectId === id) state.primaryFocus = { ...state.primaryFocus, title };
}

/**
 * Rename a tag on every item, as `bsk rename-tag` does. An item that already has the
 * new tag keeps one copy (a merge). Renaming a tag nothing carries is an error.
 */
export function renameTag(rawFrom: string, rawTo: string): RenamedTag {
  const from = rawFrom.trim().toLowerCase();
  const to = rawTo.trim().toLowerCase();
  if (!from || !to) throw new Error("Both the tag and its new name are required.");
  if (from === to) return { from, to, renamed: 0, merged: 0 };

  let renamed = 0;
  let merged = 0;
  for (const [key, tags] of Object.entries(state.tagsByItem)) {
    if (!tags.includes(from)) continue;
    if (tags.includes(to)) merged++;
    else renamed++;
    state.tagsByItem[key] = [...new Set(tags.map((t) => (t === from ? to : t)))].sort();
  }
  if (renamed + merged === 0) throw new Error(`No item is tagged '${from}'.`);

  state.subjects = state.subjects.map((s) =>
    state.tagsByItem[s.id] ? { ...s, tags: state.tagsByItem[s.id].join(", ") || null } : s,
  );
  emit();
  return { from, to, renamed, merged };
}

export function appendJournal(subject: string, text: string) {
  const item = byRef(subject);
  if (!item) return;
  const entry = { eventId: newId(), occurredAt: nowIso(), content: text };
  state.journals[item.id] = [entry, ...(state.journals[item.id] ?? [])];
  emit();
}

/** The row an item ref names: its row id, or the subject's urn / id. */
function rowFor<T extends InboxItem>(rows: T[], itemRef: string): T | undefined {
  const subject = byRef(itemRef);
  return rows.find(
    (i) =>
      i.itemId === itemRef ||
      (i.subjectUrn != null && (i.subjectUrn === itemRef || i.subjectUrn === subject?.urn)),
  );
}

/** Moves an Inbox row onto the resolved list (mirrors `bsk.v_inbox_resolved`). */
function resolveInbox(itemRef: string, resolution: InboxResolution) {
  const row = rowFor(state.inbox, itemRef);
  if (!row) return;
  state.inbox = state.inbox.filter((i) => i !== row);
  state.resolved = [
    { row, resolution, resolvedAt: nowIso() },
    ...state.resolved.filter((r) => r.row.itemId !== row.itemId),
  ];
}

export function flagItem(itemRef: string) {
  const subject = byRef(itemRef);
  if (rowFor(state.inbox, itemRef)) return;
  // Back to Inbox: a resolved row comes back whole, as a fresh flag.
  const resolved = state.resolved.find((r) => rowFor([r.row], itemRef));
  if (resolved) {
    state.resolved = state.resolved.filter((r) => r !== resolved);
    state.inbox = [{ ...resolved.row, triagedAt: nowIso() }, ...state.inbox];
    emit();
    return;
  }
  if (subject) {
    state.inbox = [
      {
        itemId: newId(),
        itemKind: "subject",
        triagedAt: nowIso(),
        subjectUrn: subject.urn,
        subjectType: subject.type,
        subjectTitle: subject.title,
      },
      ...state.inbox,
    ];
  } else {
    state.inbox = [
      {
        itemId: itemRef,
        itemKind: "event",
        triagedAt: nowIso(),
        eventKind: "note",
        eventContent: "Flagged item",
      },
      ...state.inbox,
    ];
  }
  emit();
}

export function removeInbox(itemRef: string, resolution: InboxResolution = "dismissed") {
  resolveInbox(itemRef, resolution);
  emit();
}

/** Drop: subjects move to a terminal status; events are dismissed. Not a delete. */
export function dropInboxItem(itemRef: string) {
  const row = rowFor(state.inbox, itemRef);
  if (!row) return;
  if (row.itemKind === "subject" && row.subjectUrn) {
    const subject = byRef(row.subjectUrn);
    if (subject && !isTerminal(subject.status)) {
      const terminal =
        subject.type === "Idea"
          ? "Rejected"
          : subject.type === "Problem"
            ? "Cancelled"
            : "Cancelled";
      setStatus(subject.id, terminal);
    }
    resolveInbox(row.itemId, "status");
  } else {
    resolveInbox(row.itemId, "dismissed");
  }
  emit();
}

/** A typed capture: a note, flagged, or an Idea or Problem made from it, as the kernel files it. */
export function captureNote(text: string, type: CaptureKind = "Note") {
  const eventId = newId();
  const occurredAt = nowIso();
  state.events = [
    { id: eventId, kind: "note", content: text, occurredAt },
    ...state.events,
  ];
  if (type === "Idea" || type === "Problem") {
    const created = createSubject({
      type,
      title: deriveTitle(text),
      attrs: { description: text, ...(type === "Problem" ? { date_identified: todayIso() } : {}) },
    });
    addEventRelation(eventId, created.id);
    state.inbox = state.inbox.map((item) =>
      item.subjectUrn === created.urn ? { ...item, eventKind: "note", eventContent: text } : item,
    );
    emit();
    return;
  }
  state.inbox = [
    {
      itemId: eventId,
      itemKind: "event",
      triagedAt: occurredAt,
      eventKind: "note",
      eventContent: text,
    },
    ...state.inbox,
  ];
  emit();
}

export function putArtifact(opts: {
  eventId: string;
  eventKind: string;
  blob: Blob;
  filename: string;
  contentType: string;
  durationSeconds?: number | null;
  sha256?: string | null;
}): ArtifactRecord {
  const id = newId();
  const record: ArtifactRecord = {
    id,
    eventId: opts.eventId,
    eventKind: opts.eventKind,
    filename: opts.filename,
    contentType: opts.contentType || opts.blob.type || "application/octet-stream",
    byteSize: opts.blob.size,
    sha256: opts.sha256 ?? null,
    hasBytes: true,
    bytesUrl: objectUrlFor(opts.blob),
    durationSeconds: opts.durationSeconds ?? null,
  };
  state.artifacts = { ...state.artifacts, [id]: record };
  return record;
}

function addEventRelation(eventId: string, subjectId: string, relation: EventRelation = "concerns") {
  const exists = state.eventRelations.some(
    (r) => r.eventId === eventId && r.subjectId === subjectId && r.relation === relation,
  );
  if (exists) return;
  state.eventRelations = [...state.eventRelations, { eventId, subjectId, relation }];
  const event = state.events.find((e) => e.id === eventId);
  if (!event) return;
  const row = {
    kind: event.kind,
    occurredAt: event.occurredAt,
    eventId: event.id,
    content: event.content,
  };
  state.concerning[subjectId] = [row, ...(state.concerning[subjectId] ?? [])];
}

export function relateEvent(eventId: string, subjectRef: string, as?: EventRelation) {
  const subject = byRef(subjectRef);
  if (subject) {
    const event = state.events.find((e) => e.id === eventId);
    const inbox = state.inbox.find((i) => i.itemId === eventId);
    if (!event && inbox) {
      state.events = [
        {
          id: eventId,
          kind: inbox.eventKind ?? "note",
          content: inbox.eventContent ?? null,
          occurredAt: inbox.triagedAt,
          artifactId: inbox.attachment?.id,
        },
        ...state.events,
      ];
      if (inbox.attachment) {
        state.artifacts = {
          ...state.artifacts,
          [inbox.attachment.id]: { ...inbox.attachment, eventId },
        };
      }
    }
    addEventRelation(eventId, subject.id, as ?? "concerns");
  }
  removeInbox(eventId);
}

export function getArtifact(id: string): ArtifactRecord | undefined {
  return state.artifacts[id] ?? Object.values(state.artifacts).find((a) => a.eventId === id);
}

export function filesForSubject(subjectRef: string): ArtifactRecord[] {
  const subject = byRef(subjectRef);
  if (!subject) return [];
  const eventIds = new Set(
    state.eventRelations.filter((r) => r.subjectId === subject.id).map((r) => r.eventId),
  );
  return Object.values(state.artifacts).filter(
    (a) => a.hasBytes && (eventIds.has(a.eventId) || eventIds.has(a.id)),
  );
}

export function captureDocument(req: {
  file: File;
  description: string;
  type: CaptureKind;
  sha256?: string | null;
}): BinaryCaptureResult {
  const description = req.description.trim();
  const occurredAt = nowIso();
  const eventId = newId();
  const filename = req.file.name || "attachment";
  const contentType = req.file.type || "application/octet-stream";
  const artifact = putArtifact({
    eventId,
    eventKind: "note",
    blob: req.file,
    filename,
    contentType,
    sha256: req.sha256,
  });
  state.events = [
    {
      id: eventId,
      kind: "note",
      content: description || filename,
      occurredAt,
      artifactId: artifact.id,
    },
    ...state.events,
  ];

  if (req.type === "Idea" || req.type === "Problem") {
    const title = description ? deriveTitle(description) : filename;
    const created = createSubject({
      type: req.type,
      title,
      attrs: {
        description: description || filename,
        ...(req.type === "Problem" ? { date_identified: todayIso() } : {}),
      },
    });
    addEventRelation(eventId, created.id);
    state.inbox = state.inbox.map((item) =>
      item.subjectUrn === created.urn
        ? {
            ...item,
            eventKind: "note",
            eventContent: description || filename,
            attachment: artifact,
          }
        : item,
    );
    emit();
    return {
      eventId,
      artifactId: artifact.id,
      kind: "note",
      filename,
      contentType,
      byteSize: artifact.byteSize,
      sha256: artifact.sha256,
      subject: created,
    };
  }

  state.inbox = [
    {
      itemId: eventId,
      itemKind: "event",
      triagedAt: occurredAt,
      eventKind: "note",
      eventContent: description || filename,
      attachment: artifact,
    },
    ...state.inbox,
  ];
  emit();
  return {
    eventId,
    artifactId: artifact.id,
    kind: "note",
    filename,
    contentType,
    byteSize: artifact.byteSize,
    sha256: artifact.sha256,
  };
}

export function captureVoice(req: {
  audioBlob: Blob;
  transcript: string;
  type: CaptureKind;
  keepAudio: boolean;
  durationSeconds?: number;
  sha256?: string | null;
  filename?: string;
  contentType?: string;
  /** What it's about, such as a review. The mock has no agent to file it, so it isn't kept. */
  concerning?: string;
}): BinaryCaptureResult {
  const transcript = req.transcript.trim();
  const needsTranscription = !transcript;
  const retainAudio = req.keepAudio || needsTranscription;
  const occurredAt = nowIso();
  const eventId = newId();
  const contentType = req.contentType || req.audioBlob.type || "audio/webm";
  const filename = req.filename || `voice-${occurredAt.slice(0, 19).replace(/[:T]/g, "-")}.${
    contentType.includes("mp4") ? "m4a" : contentType.includes("wav") ? "wav" : "webm"
  }`;

  let artifact: ArtifactRecord | undefined;
  if (retainAudio) {
    artifact = putArtifact({
      eventId,
      eventKind: "voice",
      blob: req.audioBlob,
      filename,
      contentType,
      durationSeconds: req.durationSeconds ?? null,
      sha256: req.sha256,
    });
  }

  state.events = [
    {
      id: eventId,
      kind: "voice",
      content: transcript || null,
      occurredAt,
      artifactId: artifact?.id,
    },
    ...state.events,
  ];

  if (req.type === "Idea" || req.type === "Problem") {
    const title = transcript ? deriveTitle(transcript) : "Needs transcription";
    const created = createSubject({
      type: req.type,
      title,
      attrs: {
        description: transcript || "Needs transcription.",
        ...(req.type === "Problem" ? { date_identified: todayIso() } : {}),
      },
    });
    addEventRelation(eventId, created.id);
    state.inbox = state.inbox.map((item) =>
      item.subjectUrn === created.urn
        ? {
            ...item,
            eventKind: "voice",
            eventContent: transcript || null,
            attachment: artifact ?? null,
            needsTranscription,
          }
        : item,
    );
    emit();
    return {
      eventId,
      artifactId: artifact?.id ?? null,
      kind: "voice",
      filename: artifact?.filename,
      contentType: artifact?.contentType,
      byteSize: artifact?.byteSize,
      sha256: artifact?.sha256,
      subject: created,
      needsTranscription,
    };
  }

  state.inbox = [
    {
      itemId: eventId,
      itemKind: "event",
      triagedAt: occurredAt,
      eventKind: "voice",
      eventContent: transcript || null,
      attachment: artifact ?? null,
      needsTranscription,
    },
    ...state.inbox,
  ];
  emit();
  return {
    eventId,
    artifactId: artifact?.id ?? null,
    kind: "voice",
    filename: artifact?.filename,
    contentType: artifact?.contentType,
    byteSize: artifact?.byteSize,
    sha256: artifact?.sha256,
    needsTranscription,
  };
}

/** Record a habit day, answering what it earned or gave back. */
export function adhereHabit(
  habitRef: string,
  adherence: "followed" | "partial" | "missed",
  on?: string,
  note?: string,
): Award[] {
  return earnDay(habitRef, adherence, on, note).awards;
}

/** Record a habit day, answering what it earned or gave back and each domain it moved (§3.8). */
export function earnDay(
  habitRef: string,
  adherence: "followed" | "partial" | "missed",
  on?: string,
  note?: string,
): Earned {
  const habit = state.habits.find((h) => h.id === habitRef || h.urn === habitRef);
  if (!habit) return { awards: [], progress: [] };
  if (isPlanned(byId(habit.id)?.status)) {
    throw new Error(
      `Habit '${habit.name}' is Planned, so it isn't tracked yet. Move it to Building to start tracking it, then record its days.`,
    );
  }
  const date = on ?? todayIso();
  const mapped = adherence === "missed" ? "not_followed" : adherence;
  state.occurrences = state.occurrences.map((o) =>
    o.habitId === habit.id && o.occurrenceDate === date
      ? { ...o, state: mapped, note: note ?? o.note }
      : o,
  );
  const has = state.occurrences.some(
    (o) => o.habitId === habit.id && o.occurrenceDate === date,
  );
  if (!has) {
    state.occurrences = [
      ...state.occurrences,
      {
        habitId: habit.id,
        habitUrn: habit.urn,
        habitName: habit.name,
        occurrenceDate: date,
        state: mapped,
        allowsPartial: habit.allowsPartial,
        note: note ?? null,
      },
    ];
  }
  const today = todayIso();
  state.habits = state.habits.map((h) =>
    h.id === habit.id ? applyStreakToHabit(h, state.occurrences, today) : h,
  );
  const earned = earning(() => reconcileDay(habit.id, date, nowIso()));
  emit();
  return earned;
}

/**
 * `bsk recur` / `POST /api/recur` (D8): the pattern is stored as an object; `null` clears it.
 * On a Task it makes a repeating Task (D16): `start` is the first date, and the first
 * repetition is made (or the open one re-dated). Answers the repetitions made.
 */
export function setRecurrence(subjectRef: string, spec: RecurrenceSpec | null, start?: string): CreatedSubject[] {
  const subject = byRef(subjectRef);
  const detail = subject ? state.details[subject.id] : undefined;
  if (!subject || !detail) throw new Error(`No subject matches "${subjectRef}".`);
  if (subject.type === "Task") {
    const problem = taskPatternProblem(spec);
    if (problem) throw new Error(problem);
    guardPattern(state, subject.id, !spec);
  } else if (spec && (spec.freq === "each" || spec.from === "completion")) {
    throw new Error("Once each week, and every N after the last completion, are for Tasks.");
  }
  const raw = spec ? serializeRecurrence(spec) : null;
  const bag = rawAttrs(detail.attributes);
  if (raw) bag.recurrence = JSON.parse(raw) as unknown;
  else delete bag.recurrence;
  state.details[subject.id] = { ...detail, attributes: JSON.stringify(bag) };
  state.habits = state.habits.map((h) => (h.id === subject.id ? { ...h, recurrence: raw } : h));
  const made = subject.type === "Task" ? afterPattern(state, subject.id, todayIso(), start) : [];
  emit();
  return made.map((m) => ({ id: m.id, urn: m.urn, type: m.type, title: m.title }));
}

/** An appointment's text attribute, or its series' when the occurrence has none (v_appointment). */
function appointmentAttr(own: Record<string, unknown>, series: Record<string, unknown>, key: string): string | null {
  const value = own[key] ?? series[key];
  return typeof value === "string" && value ? value : null;
}

/** `GET /api/appointments?on=`: the day's one-time appointments and occurrences (CAL-1). */
export function appointmentsOn(day: string): AppointmentRow[] {
  const rows: AppointmentRow[] = [];
  for (const s of state.subjects) {
    if (s.type !== "Appointment" || s.archived) continue;
    const own = rawAttrs(state.details[s.id]?.attributes);
    if (own.recurrence) continue;
    const date = typeof own.date === "string" ? own.date : s.due;
    if (date !== day) continue;
    const seriesUrn = typeof own.series === "string" ? own.series : null;
    const series = seriesUrn ? byUrn(seriesUrn) : undefined;
    const inherited = series ? rawAttrs(state.details[series.id]?.attributes) : {};
    rows.push({
      id: s.id,
      urn: s.urn,
      title: s.title,
      date,
      startTime: appointmentAttr(own, inherited, "start"),
      endTime: appointmentAttr(own, inherited, "end"),
      allDay: appointmentAttr(own, inherited, "all_day"),
      location: appointmentAttr(own, inherited, "location"),
      meetingLink: appointmentAttr(own, inherited, "meeting_link"),
      area: s.area ?? null,
      seriesUrn,
      recurrence: null,
      status: s.status ?? "Scheduled",
      archived: false,
      createdAt: s.createdAt ?? "",
    });
  }
  return sortAppointments(rows);
}

/**
 * `bsk materialize` / `POST /api/appointments/{id}/materialize` (CAL-1, D10): an occurrence for
 * each of the pattern's dates from the series' start through `through` that isn't made yet.
 */
export function materializeAppointments(
  seriesRef: string,
  through: string,
): { series: CreatedSubject; created: CreatedSubject[] } {
  const series = byRef(seriesRef);
  if (!series) throw new Error(`No subject matches "${seriesRef}".`);
  if (series.type !== "Appointment") {
    throw new Error(`'${series.urn}' is a ${series.type}, not an Appointment series.`);
  }
  const attrs = rawAttrs(state.details[series.id]?.attributes);
  const spec = attrs.recurrence ? parseRecurrence(JSON.stringify(attrs.recurrence)) : null;
  const start =
    (typeof attrs.start_date === "string" && attrs.start_date) || (series.createdAt ?? todayIso()).slice(0, 10);
  // A date is taken by the occurrence that stands for it, wherever it's moved (code review CR65).
  const made = new Set(
    state.subjects
      .filter((s) => s.type === "Appointment")
      .map((s) => rawAttrs(state.details[s.id]?.attributes))
      .filter((a) => a.series === series.urn)
      .map((a) => (typeof a.occurrence === "string" ? a.occurrence : a.date))
      .filter((d): d is string => typeof d === "string"),
  );
  const created: CreatedSubject[] = [];
  for (let day = start, guard = 0; spec && day <= through && guard < 1500; day = addDays(day, 1), guard += 1) {
    if (made.has(day) || !isOccurrenceDate(spec, day, start)) continue;
    created.push(
      createSubject({ type: "Appointment", title: series.title, attrs: { series: series.urn, occurrence: day, date: day } }),
    );
  }
  emit();
  return {
    series: { id: series.id, urn: series.urn, type: series.type, title: series.title },
    created,
  };
}

/** `POST /api/tasks/settle`: the repetitions every repeating Task is owed by today. */
export function mockSettleTasks(): TaskSettleResult {
  const { series, made } = settleAll(state, todayIso());
  if (made.length > 0) emit();
  return { series, made: made.map((m) => ({ id: m.id, urn: m.urn, type: m.type, title: m.title })) };
}

/** `GET /api/tasks/series`: every repeating Task, by title. */
export function mockTaskSeries(includeArchived = false): TaskSeriesRow[] {
  return state.subjects
    .filter((s) => isSeries(state, s.id) && (includeArchived || !s.archived))
    .map((s) => ({ ...seriesRow(state, s), bounty: withBounty(s).bounty ?? null }))
    .sort((a, b) => a.title.localeCompare(b.title));
}

/** `GET /api/tasks/series/{id}`: a repeating Task and its repetitions, latest first. */
export function mockTaskSeriesHistory(id: string): TaskSeriesHistory | null {
  const series = byId(id);
  if (!series || !isSeries(state, id)) return null;
  return {
    series: { ...seriesRow(state, series), bounty: withBounty(series).bounty ?? null },
    repetitions: repetitionRows(state, series.urn),
  };
}

/**
 * A list row's repeating-Task fields (TASKS-3), as the API reads them: whether it's a series,
 * a repetition's series and date, and the series' pattern.
 */
export function withRepeat(item: SubjectListItem): SubjectListItem {
  if (item.type !== "Task") return item;
  const series = isSeries(state, item.id) ? item : seriesOf(state, item.id);
  if (!series) return { ...item, isSeries: false, seriesId: null, occurrence: null, repeat: null };
  const own = series.id === item.id;
  return {
    ...item,
    isSeries: own,
    seriesId: own ? null : series.id,
    occurrence: own ? null : (repeatOf(state, item.id)?.occurrence ?? null),
    repeat: JSON.stringify(rawAttrs(state.details[series.id]?.attributes).recurrence ?? null),
  };
}

/** True when the subject is a repeating Task's series: the lists leave it out unless asked. */
export function isTaskSeries(id: string): boolean {
  return isSeries(state, id);
}

/** A review as it reads today: its status, missed and late worked out from its day (D8). */
export function reviewAsOf(review: MockReview, today = todayIso()): ReviewDoc {
  const { firstCompletedOn: _first, ...doc } = review;
  return { ...doc, ...reviewStateAsOf(review, today) };
}

/** The reviews whose day is in the range, of the kind when given, as they read today. */
export function listReviews(opts?: { kind?: ReviewDoc["kind"]; from?: string; to?: string }): ReviewDoc[] {
  const today = todayIso();
  return state.reviews
    .filter(
      (r) =>
        (!opts?.kind || r.kind === opts.kind) &&
        (!opts?.from || r.date >= opts.from) &&
        (!opts?.to || r.date <= opts.to),
    )
    .sort((a, b) => a.date.localeCompare(b.date) || (a.kind === "daily" ? -1 : 1))
    .map((r) => reviewAsOf(r, today));
}

/**
 * Open the review of a kind holding a day, or make it the first time (the mock's
 * `bsk review open`): one per kind and period, never ahead of its day.
 */
export function openReviewDoc(kind: ReviewDoc["kind"], day: string): ReviewDoc {
  const today = todayIso();
  const period = reviewPeriod(kind, day);
  const refusal = whyNotOpen(period, today);
  if (refusal) throw new Error(refusal);
  let found = state.reviews.find((r) => r.kind === kind && r.date === period.date);
  if (!found) {
    found = {
      id: `review-${kind}-${period.date}`,
      title: `${kind === "daily" ? "Daily" : "Weekly"} review`,
      kind,
      date: period.date,
      weekStart: startOfWeekMonday(period.date),
      weekEnd: endOfWeekSunday(period.date),
      planStart: period.planStart,
      planEnd: period.planEnd,
      dueOn: period.date,
      status: "Open",
      completedAt: null,
      firstCompletedOn: null,
      missed: false,
      completedLate: false,
      keepFocus: true,
      reflection: null,
    };
    state.reviews = [...state.reviews, found];
    emit();
  }
  return reviewAsOf(found, today);
}

export function saveReviewBody(id: string, body: ReviewBody) {
  state.reviews = state.reviews.map((r) => (r.id === id ? { ...r, ...body } : r));
  emit();
}

/** Completes the review; completing it again changes nothing, and the first completion stays. */
export function completeReviewDoc(id: string) {
  state.reviews = state.reviews.map((r) =>
    r.id === id && !r.completedAt
      ? { ...r, completedAt: nowIso(), firstCompletedOn: r.firstCompletedOn ?? todayIso() }
      : r,
  );
  emit();
}

export function involvePerson(
  subjectRef: string,
  personRef: string,
  role: string,
  remove?: boolean,
) {
  const subject = byRef(subjectRef);
  const person = state.people.find((p) => p.id === personRef || p.urn === personRef);
  if (!subject || !person) return;
  if (remove) {
    state.associations = state.associations.filter(
      (a) =>
        !(
          a.subjectId === subject.id &&
          a.personId === person.id &&
          a.role === role
        ),
    );
  } else {
    state.associations = [
      ...state.associations,
      {
        subjectId: subject.id,
        subjectUrn: subject.urn,
        subjectType: subject.type,
        subjectTitle: subject.title,
        role,
        personId: person.id,
        personUrn: person.urn,
        personName: person.title,
      },
    ];
  }
  emit();
}

// ---------------------------------------------------------------------------
// Rewards (REWARD-1 / REWARD-3): folds over `state.ledger`, as v_wallet / v_ledger /
// v_reward fold the ledger events, and the redeem and catalog rules of RewardService
// and RewardCatalogService — the same refusals, in the same words.
// ---------------------------------------------------------------------------

export function mockWallet(): Wallet {
  const balances = CURRENCIES.map((c) => {
    let earned = 0;
    let reversed = 0;
    let spent = 0;
    for (const e of state.ledger) {
      const n = e.amounts[c.key] ?? 0;
      if (e.entry === "earn") earned += n;
      else if (e.entry === "reversal") reversed -= n;
      else if (e.entry === "redeem") spent -= n;
    }
    return {
      currency: c.key,
      label: c.label,
      spendable: c.spendable,
      earned,
      reversed,
      spent,
      balance: earned - reversed - spent,
    };
  });
  return {
    openedAt: state.ledger.find((e) => e.entry === "open")?.occurredAt ?? null,
    coinValueUsd: state.coinValueUsd,
    balances,
  };
}

function ledgerRow(e: MockLedgerEntry): LedgerRow {
  const subject = e.subjectId ? byId(e.subjectId) : undefined;
  return {
    id: e.id,
    occurredAt: e.occurredAt,
    entry: e.entry,
    provenance: e.provenance,
    amounts: { ...e.amounts },
    trigger: e.trigger ?? null,
    awardKey: e.awardKey ?? null,
    subject: subject
      ? { id: subject.id, urn: subject.urn, type: subject.type, title: subject.title }
      : null,
    note: e.note ?? null,
    dollars: e.dollars ?? null,
    coinRate: e.coinRate ?? null,
  };
}

/** Newest first, in [from, to), as `GET /api/ledger` reads it. */
export function mockLedger(opts?: { from?: string; to?: string; limit?: number }): LedgerRow[] {
  if (opts?.from && opts?.to && opts.from >= opts.to) {
    throw new Error("The ledger range must end after it starts.");
  }
  const limit = Math.min(Math.max(opts?.limit ?? 100, 1), 500);
  // Newest first; entries written in the same instant keep their write order, newest first.
  return state.ledger
    .map((e, i) => ({ e, i }))
    .filter(({ e }) => (!opts?.from || e.occurredAt >= opts.from) && (!opts?.to || e.occurredAt < opts.to))
    .sort((a, b) => b.e.occurredAt.localeCompare(a.e.occurredAt) || b.i - a.i)
    .slice(0, limit)
    .map(({ e }) => ledgerRow(e));
}

const MAX_HISTORY_DAYS = 731;

/** "2026-10-14": the date an instant reads in `tz` (throws for a zone Intl doesn't know). */
function dateIn(tz: string): (iso: string) => string {
  const format = new Intl.DateTimeFormat("en-CA", {
    timeZone: tz,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
  return (iso) => {
    const parts = Object.fromEntries(format.formatToParts(new Date(iso)).map((p) => [p.type, p.value]));
    return `${parts.year}-${parts.month}-${parts.day}`;
  };
}

function daySpan(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);
}

/**
 * The history by local day, as `GET /api/ledger/daily` reads it (`LedgerDays`): each
 * entry filed on the date it reads in `tz`, the days [from, to), oldest first, with the
 * kernel's refusals in its words.
 */
export function mockLedgerDaily(opts: { from: string; to: string; tz: string }): LedgerDay[] {
  const { from, to, tz } = opts;
  if (to <= from) throw new Error("The history range must end after it starts.");
  const span = daySpan(from, to);
  if (span > MAX_HISTORY_DAYS) {
    throw new Error(`The history range can span at most ${MAX_HISTORY_DAYS} days; this one spans ${span}.`);
  }
  let local: (iso: string) => string;
  try {
    if (!tz.trim() || /\s/.test(tz)) throw new RangeError(tz);
    local = dateIn(tz);
  } catch {
    throw new Error(`Unknown time zone '${tz}'.`);
  }

  const days = new Map<string, { earned: Amounts; reversed: Amounts; spent: Amounts; net: Amounts }>();
  for (const e of state.ledger) {
    const date = local(e.occurredAt);
    if (date < from || date >= to) continue;
    let day = days.get(date);
    if (!day) {
      day = { earned: {}, reversed: {}, spent: {}, net: {} };
      days.set(date, day);
    }
    for (const [key, n] of Object.entries(e.amounts)) {
      if (!n) continue;
      const bucket = e.entry === "earn" ? day.earned : e.entry === "reversal" ? day.reversed : e.entry === "redeem" ? day.spent : null;
      if (bucket) bucket[key] = (bucket[key] ?? 0) + Math.abs(n);
      day.net[key] = (day.net[key] ?? 0) + n;
    }
  }
  const tidy = (a: Amounts) => Object.fromEntries(orderedAmounts(a));
  return [...days.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, d]) => ({
      date,
      earned: tidy(d.earned),
      reversed: tidy(d.reversed),
      spent: tidy(d.spent),
      net: tidy(d.net),
    }));
}

/** A price as the kernel reads it: spendable currencies, whole amounts above zero. */
function lenientPrice(raw: unknown): Amounts {
  const out: Amounts = {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return out;
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    const c = currency(key);
    const n = Number(value);
    if (c?.spendable && Number.isInteger(n) && n > 0) out[c.key] = n;
  }
  return Object.fromEntries(orderedAmounts(out));
}

function rewardRow(subject: SubjectListItem): RewardRow {
  const attrs = rawAttrs(state.details[subject.id]?.attributes);
  const kind = attrs.kind === "savings" ? "savings" : "instant";
  const target = Number(attrs.target_usd);
  const targetUsd = Number.isFinite(target) && target > 0 ? target : null;
  const redeems = state.ledger.filter((e) => e.entry === "redeem" && e.subjectId === subject.id);
  const dollarsRedeemed =
    Math.round(redeems.reduce((sum, e) => sum + (e.dollars ?? 0), 0) * 100) / 100;
  const hasTarget = kind === "savings" && targetUsd !== null;
  return {
    id: subject.id,
    urn: subject.urn,
    name: subject.title,
    kind,
    price: lenientPrice(attrs.price),
    targetUsd,
    redemptions: redeems.length,
    coinsRedeemed: redeems.reduce((sum, e) => sum - (e.amounts.coins ?? 0), 0),
    dollarsRedeemed,
    lastRedeemedAt: redeems.map((e) => e.occurredAt).sort().at(-1) ?? null,
    percentOfTarget: hasTarget ? Math.round((1000 * dollarsRedeemed) / targetUsd) / 10 : null,
    remainingUsd: hasTarget ? Math.max(targetUsd - dollarsRedeemed, 0) : null,
    description: typeof attrs.description === "string" ? attrs.description : null,
    archived: subject.archived,
  };
}

export function mockRewards(includeArchived = false): RewardRow[] {
  return state.subjects
    .filter((s) => s.type === "Reward" && (includeArchived || !s.archived))
    .sort((a, b) => a.title.localeCompare(b.title))
    .map(rewardRow);
}

/** RewardPrice.Normalize: spendable currencies, whole amounts above zero, display order. */
function normalizeSpend(amounts: Amounts): Amounts {
  const out: Amounts = {};
  for (const [key, n] of Object.entries(amounts)) {
    const c = currency(key);
    if (!c) {
      throw new Error(
        `Unknown currency '${key}'. Expected one of: ${CURRENCIES.map((x) => x.key).join(", ")}.`,
      );
    }
    if (!c.spendable) throw new Error(`${c.label} is tracked, not spent; it can't be redeemed.`);
    if (!Number.isInteger(n) || n <= 0) {
      throw new Error(`Use a whole number of ${c.label} above zero; got ${n}.`);
    }
    if (out[c.key] !== undefined) throw new Error(`${c.label} is listed twice.`);
    out[c.key] = n;
  }
  return Object.fromEntries(orderedAmounts(out));
}

function rewardByRef(ref: string): SubjectListItem | undefined {
  const title = ref.trim().toLowerCase();
  return byRef(ref) ?? state.subjects.find((s) => s.title.toLowerCase() === title);
}

export function mockRedeem(req: RedeemRequest): Redemption {
  const note = req.note?.trim() || undefined;
  const given = req.amounts && Object.keys(req.amounts).length > 0 ? req.amounts : undefined;
  let reward: SubjectListItem | undefined;
  let spend: Amounts;
  if (!req.reward?.trim()) {
    if (!given) throw new Error("Say what to redeem, e.g. freedom=45.");
    spend = normalizeSpend(given);
    if (!note) throw new Error("Say what this redemption is for: a note is required.");
  } else {
    reward = rewardByRef(req.reward);
    if (!reward) throw new Error(`No subject matches '${req.reward}'.`);
    if (reward.type !== "Reward") {
      throw new Error(
        `'${reward.title}' is a ${reward.type}, not a Reward; redeem against a Reward, or give a note instead.`,
      );
    }
    if (reward.archived) {
      throw new Error(`'${reward.title}' is archived; restore it to redeem against it.`);
    }
    const row = rewardRow(reward);
    if (row.kind === "savings") {
      if (!given) {
        throw new Error(`Say how many Coins to put toward '${reward.title}', e.g. coins=50.`);
      }
      spend = normalizeSpend(given);
      if (Object.keys(spend).some((k) => k !== "coins")) {
        throw new Error(`'${reward.title}' is a savings goal: only Coins go toward it.`);
      }
    } else if (given) {
      spend = normalizeSpend(given);
    } else if (Object.keys(row.price).length > 0) {
      spend = row.price;
    } else {
      throw new Error(
        `'${reward.title}' has no price yet: say what it costs this time, e.g. freedom=45, or give it a price.`,
      );
    }
  }
  const refused = shortfall(mockWallet(), spend);
  if (refused) throw new Error(refused);

  // Coins are real money: stamp what moved and the rate it moved at.
  const coins = spend.coins;
  const entry: MockLedgerEntry = {
    id: newId(),
    occurredAt: nowIso(),
    entry: "redeem",
    provenance: "declared",
    amounts: Object.fromEntries(Object.entries(spend).map(([k, n]) => [k, -n])),
    subjectId: reward?.id,
    note,
    dollars: coins ? coinsToUsd(coins, state.coinValueUsd) : undefined,
    coinRate: coins ? state.coinValueUsd : undefined,
  };
  state.ledger = [...state.ledger, entry];
  emit();
  return {
    id: entry.id,
    amounts: spend,
    note: note ?? null,
    dollars: entry.dollars ?? null,
    coinRate: entry.coinRate ?? null,
    reward: reward ? rewardRow(reward) : null,
    wallet: mockWallet(),
  };
}

function normalizeTarget(target: number): number {
  const cents = Math.round(target * 100) / 100;
  if (!(cents > 0)) throw new Error(`A savings target is a dollar amount above zero; got ${target}.`);
  return cents;
}

/** Write a Reward's catalog attributes; `undefined` removes a key. */
function writeRewardAttrs(id: string, patch: Record<string, unknown>) {
  const detail = state.details[id];
  if (!detail) return;
  const bag = rawAttrs(detail.attributes);
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined) delete bag[key];
    else bag[key] = value;
  }
  state.details[id] = { ...detail, attributes: JSON.stringify(bag) };
}

export function mockAddReward(req: NewRewardRequest): RewardRow {
  const title = req.title.trim();
  if (!title) throw new Error("title is required.");
  const kind = req.kind ?? "instant";
  if (kind !== "instant" && kind !== "savings") {
    throw new Error(`A Reward is instant or savings; got '${String(kind)}'.`);
  }
  const price = req.price && Object.keys(req.price).length > 0 ? req.price : undefined;
  if (price && kind === "savings") {
    throw new Error(
      "A savings goal has no price: Coins go into it as you deposit them. Give it a target instead.",
    );
  }
  if (req.targetUsd !== undefined && kind !== "savings") {
    throw new Error("Only a savings goal has a target; an instant reward has a price.");
  }
  const normalizedPrice = price ? normalizeSpend(price) : undefined;
  const target = req.targetUsd !== undefined ? normalizeTarget(req.targetUsd) : undefined;
  const description = req.description?.trim() || undefined;
  const created = createSubject({
    type: "Reward",
    title,
    attrs: description ? { description } : {},
  });
  writeRewardAttrs(created.id, { kind, price: normalizedPrice, target_usd: target });
  emit();
  return rewardRow(byId(created.id)!);
}

function catalogReward(ref: string): SubjectListItem {
  const reward = rewardByRef(ref);
  if (!reward) throw new Error(`No subject matches '${ref}'.`);
  if (reward.type !== "Reward") throw new Error(`'${reward.title}' is a ${reward.type}, not a Reward.`);
  return reward;
}

export function mockPriceReward(ref: string, price: Amounts): RewardRow {
  if (Object.keys(price).length === 0) throw new Error("Say what it costs, e.g. freedom=120.");
  const normalized = normalizeSpend(price);
  const reward = catalogReward(ref);
  if (rewardRow(reward).kind === "savings") {
    throw new Error(
      `'${reward.title}' is a savings goal: Coins go into it as you deposit them, so it has no price.`,
    );
  }
  writeRewardAttrs(reward.id, { price: normalized });
  emit();
  return rewardRow(reward);
}

export function mockTargetReward(ref: string, targetUsd: number | null): RewardRow {
  const normalized = targetUsd === null ? undefined : normalizeTarget(targetUsd);
  const reward = catalogReward(ref);
  if (rewardRow(reward).kind !== "savings") {
    throw new Error(`'${reward.title}' is an instant reward: it has a price, not a target.`);
  }
  writeRewardAttrs(reward.id, { target_usd: normalized });
  emit();
  return rewardRow(reward);
}

export type { CanonicalEdge, MockState };

// ---------------------------------------------------------------------------
// Earning (REWARD-2 / REWARD-4): the reconcile of RewardService, scoped to what a write
// touched. A status change settles the item's `completed` (or a habit's `established`)
// award, and a Task's `timed` one (its timer's time up to the completion); a habit day settles that day's followed / partial awards and the streak
// milestones of the runs around it. Due and unpaid → an earn priced now; paid and no
// longer due → a reversal of exactly what was paid. Seeded history is never swept, so a
// click pays only for what it changed.
// ---------------------------------------------------------------------------

/** The stored bounty: a repetition without its own pays its series' (D16). */
function bountyOf(subjectId: string): unknown {
  const own = rawAttrs(state.details[subjectId]?.attributes).bounty;
  if (own !== undefined) return own;
  const series = seriesOf(state, subjectId);
  return series ? rawAttrs(state.details[series.id]?.attributes).bounty : undefined;
}

function storedBounty(subjectId: string): Record<string, Amounts> {
  return parseBounty(bountyOf(subjectId));
}

function storedEveryMinutes(subjectId: string): number | null {
  return parseEveryMinutes(bountyOf(subjectId));
}

function netPaid(awardKey: string): Amounts {
  const net: Amounts = {};
  for (const e of state.ledger) {
    if (e.awardKey !== awardKey || (e.entry !== "earn" && e.entry !== "reversal")) continue;
    for (const [k, n] of Object.entries(e.amounts)) net[k] = (net[k] ?? 0) + n;
  }
  return Object.fromEntries(Object.entries(net).filter(([, n]) => n !== 0));
}

export function settle(
  item: SubjectListItem,
  awardKey: string,
  trigger: string,
  due: boolean,
  at: string,
  price: () => Amounts = () => priceOf(item.type, trigger, storedBounty(item.id)),
): Award | null {
  const net = netPaid(awardKey);
  const paid = Object.keys(net).length > 0;
  let entry: "earn" | "reversal";
  let amounts: Amounts;
  let creditedOn: string | undefined;
  let base: Amounts | undefined;
  let multipliers: Record<string, number> | undefined;
  if (due && !paid) {
    amounts = price();
    // A trigger priced to pay nothing writes nothing.
    if (Object.keys(amounts).length === 0) return null;
    entry = "earn";
    // Credited to the day it happened; domain XP paid at that day's multiplier, frozen here.
    creditedOn = creditedOnFor(awardKey, at);
    ({ amounts, base, multipliers } = priceDomains(amounts, creditedOn));
    // The first paying earn opens the wallet, dated at it.
    if (!state.ledger.some((e) => e.entry === "open")) {
      state.ledger = [
        ...state.ledger,
        { id: newId(), occurredAt: at, entry: "open", provenance: "declared", amounts: {} },
      ];
    }
  } else if (!due && paid) {
    amounts = Object.fromEntries(Object.entries(net).map(([k, n]) => [k, -n]));
    entry = "reversal";
  } else {
    return null;
  }
  const id = newId();
  state.ledger = [
    ...state.ledger,
    {
      id,
      occurredAt: at,
      entry,
      provenance: "derived",
      amounts,
      trigger,
      awardKey,
      subjectId: item.id,
      ...(creditedOn ? { creditedOn } : {}),
      ...(base ? { base, multipliers } : {}),
    },
  ];
  return {
    id,
    entry,
    trigger,
    awardKey,
    subject: { id: item.id, urn: item.urn, type: item.type, title: item.title },
    amounts: { ...amounts },
    ...(base ? { base: { ...base }, multipliers: { ...multipliers } } : {}),
  };
}

// ---------------------------------------------------------------------------
// Life domains (REWARD-6 / REWARD-7 / REWARD-8, xp-domains plan §3.4–3.8, §7.4): RewardService's
// domain half and the exception days. Each earning write prices domain XP from the record as it
// stood before the write, then pays any rank-up the best rank reached and says which domains moved.
// ---------------------------------------------------------------------------

/** The write in progress: the ledger before it, and its domains folded once, when needed. */
let pricing: { ledger: MockLedgerEntry[]; folds: DomainProgress[] | null } | null = null;

/** The marked days, as the fold reads them. */
function markedDays(): string[] {
  return state.exceptionDays.map((e) => e.day);
}

/** Each day's Home Score in ten-thousandths, as Environment's fold reads them. */
function homeScores(): Map<string, number> {
  return mockHomeScoreUnits(markedDays(), todayIso());
}

/** Every configured domain as the ledger, the marked days and the home's ratings read now. */
function foldNow(ledger: readonly MockLedgerEntry[] = state.ledger): DomainProgress[] {
  return foldDomains(ledger, todayIso(), markedDays(), settledThrough(), homeScores());
}

function foldsBefore(): DomainProgress[] {
  if (!pricing) return foldNow();
  pricing.folds ??= foldNow(pricing.ledger);
  return pricing.folds;
}

/** `DomainPricing.Price`: each configured domain's XP at the credited day's multiplier. */
export function priceDomains(
  price: Amounts,
  creditedOn: string,
): { amounts: Amounts; base?: Amounts; multipliers?: Record<string, number> } {
  if (!Object.entries(price).some(([k, n]) => n > 0 && rulesFor(k))) return { amounts: price };
  const amounts = { ...price };
  const base: Amounts = {};
  const multipliers: Record<string, number> = {};
  for (const [key, n] of Object.entries(price)) {
    const domain = foldsBefore().find((d) => d.rules.currency === key);
    if (!domain || n <= 0) continue;
    const multiplier = domain.multiplierFor(creditedOn);
    if (multiplier <= 1) continue;
    amounts[key] = applyMultiplier(n, multiplier);
    base[key] = n;
    multipliers[key] = multiplier;
  }
  return Object.keys(multipliers).length === 0 ? { amounts } : { amounts, base, multipliers };
}

/**
 * Runs an earning write's reconcile. When it wrote domain XP, it also pays any rank-up the
 * best rank reached and answers each domain whose level, rank or bests moved.
 */
export function earning(reconcile: () => Award[]): Earned {
  pricing = { ledger: state.ledger, folds: null };
  try {
    const awards = reconcile();
    if (!awards.some((a) => Object.keys(a.amounts).some((k) => rulesFor(k)))) return { awards, progress: [] };
    const before = foldsBefore();
    const { rankUps, folds } = payRankUps();
    return {
      awards: [...awards, ...rankUps.map((r) => r.award)],
      progress: domainChanges(before, folds, rankUps),
    };
  } finally {
    pricing = null;
  }
}

/**
 * `PayRankUpsAsync`: each rank a configured domain's best rank first reached on or after the
 * day the wallet opened, paid once under `rank:<domain>:<start level>` and never reversed.
 */
function payRankUps(): {
  rankUps: { award: Award; currency: string; rank: number }[];
  folds: DomainProgress[];
} {
  const folds = foldNow();
  const open = state.ledger.find((e) => e.entry === "open");
  if (!open) return { rankUps: [], folds };
  const openedOn = localDay(open.occurredAt);
  const paid = new Set(state.ledger.filter((e) => e.trigger === RANK_TRIGGER).map((e) => e.awardKey));
  const at = nowIso();
  const rankUps: { award: Award; currency: string; rank: number }[] = [];
  for (const progress of folds) {
    const rules = progress.rules;
    for (const reached of progress.ranksReached) {
      const key = rankAwardKey(rules, reached.rank);
      if (reached.qualifiedOn < openedOn || paid.has(key) || gemsFor(rules, reached.rank) <= 0) continue;
      const amounts = { gems: gemsFor(rules, reached.rank) };
      const note = `${currency(rules.currency)?.label.split(" ")[0] ?? rules.currency} ${reached.rule.title}`;
      const id = newId();
      state.ledger = [
        ...state.ledger,
        {
          id,
          occurredAt: at,
          entry: "earn",
          provenance: "derived",
          amounts,
          trigger: RANK_TRIGGER,
          awardKey: key,
          note,
          creditedOn: reached.qualifiedOn,
        },
      ];
      rankUps.push({
        award: { id, entry: "earn", trigger: RANK_TRIGGER, awardKey: key, subject: null, note, amounts: { ...amounts } },
        currency: rules.currency,
        rank: reached.rank,
      });
    }
  }
  return { rankUps, folds };
}

/** `GET /api/domains`: every life domain, configured or not. */
export function mockDomains(): DomainsRead {
  const today = todayIso();
  const credit = creditDays(state.ledger);
  const folds = foldNow();
  return {
    today,
    domains: CURRENCIES.filter((c) => c.group === "domain").map((c) => {
      const progress = folds.find((p) => p.rules.currency === c.key);
      if (progress) return toDomainRow(c, progress.totalXp, progress);
      const days = credit.get(c.key) ?? [];
      return toDomainRow(c, days.filter((d) => d.day <= today).reduce((sum, d) => sum + d.paid, 0), null);
    }),
  };
}

const MAX_DOMAIN_DAYS = 731;

/** `GET /api/domains/{key}/days`: one domain's last days, by key, label or first word. */
export function mockDomainDays(reference: string, days = 30): DomainDaysRead {
  if (days < 1) throw new Error(`Ask for at least 1 day; got ${days}.`);
  if (days > MAX_DOMAIN_DAYS) throw new Error(`Ask for at most ${MAX_DOMAIN_DAYS} days; got ${days}.`);
  const wanted = reference.trim().toLowerCase();
  const domains = CURRENCIES.filter((c) => c.group === "domain");
  const match = domains.find(
    (c) => c.key === wanted || c.label.toLowerCase() === wanted || c.label.split(" ")[0].toLowerCase() === wanted,
  );
  if (!match) {
    throw new Error(`'${reference}' isn't a life domain. Expected one of: ${domains.map((c) => c.key).join(", ")}.`);
  }
  return toDomainDays(
    match,
    creditDays(state.ledger).get(match.key) ?? [],
    todayIso(),
    days,
    state.exceptionDays,
    settledThrough(),
    homeScores(),
  );
}

/** `GET /api/domains/report?days=N`: every life domain over its last `days` days. */
export function mockDomainReport(days = 30): DomainReportRead {
  if (days < 1) throw new Error(`Ask for at least 1 day; got ${days}.`);
  if (days > MAX_DOMAIN_DAYS) throw new Error(`Ask for at most ${MAX_DOMAIN_DAYS} days; got ${days}.`);
  const today = todayIso();
  const credit = creditDays(state.ledger);
  const settled = settledThrough();
  const scores = homeScores();
  return {
    today,
    days,
    domains: CURRENCIES.filter((c) => c.group === "domain").map((c) =>
      toDomainReportRow(c, credit.get(c.key) ?? [], today, days, markedDays(), settled, scores),
    ),
  };
}

/**
 * A rating write (`SpaceRatingService`): the home's condition can move Environment's level and
 * rank, so it answers each domain that moved and pays any rank-up its best rank reached, as
 * the API's rating writes do (environment plan §4.6).
 */
export function mockHomeWrite<T extends Earned>(write: () => T): T {
  const before = foldNow();
  const result = write();
  const { rankUps, folds } = payRankUps();
  if (rankUps.length > 0) emit();
  return {
    ...result,
    awards: [...result.awards, ...rankUps.map((r) => r.award)],
    progress: [...result.progress, ...domainChanges(before, folds, rankUps)],
  };
}

/**
 * `POST /api/domains/settle`: settles the activity XP that's due (as the API does after the
 * food days), then pays any rank-up time alone has reached.
 */
export function mockSettleDomains(): Earned {
  const activity = settleActivityDays();
  const { rankUps, folds } = payRankUps();
  if (activity.awards.length > 0 || rankUps.length > 0) emit();
  return {
    awards: [...activity.awards, ...rankUps.map((r) => r.award)],
    progress: [...activity.progress, ...domainChanges(folds, folds, rankUps)],
  };
}

/** The configured domains as they read now (tests and the seed). */
export function mockDomainFolds(): DomainProgress[] {
  return foldNow();
}

const EXCEPTION_DAYS_DEFAULT = 90;
const EXCEPTION_COMMENT_MAX = 140;

/** `GET /api/domains/exceptions?days=N`: the marked days among the last `days`, newest first. */
export function mockExceptionDays(days = EXCEPTION_DAYS_DEFAULT): ExceptionDaysRead {
  if (!Number.isInteger(days) || days < 1 || days > MAX_DOMAIN_DAYS) {
    throw new Error(`Ask for 1 to ${MAX_DOMAIN_DAYS} days; got ${days}.`);
  }
  const today = todayIso();
  const from = addDays(today, 1 - days);
  return {
    today,
    from,
    exceptions: state.exceptionDays
      .filter((e) => e.day >= from && e.day <= today)
      .sort((a, b) => b.day.localeCompare(a.day))
      .map((e) => ({ ...e })),
  };
}

// Control characters and Unicode's own line breaks: an exception day's comment is one line.
// eslint-disable-next-line no-control-regex
const LINE_BREAK = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/;

/** `ExceptionDayService.Normalize`: the comment trimmed, or a refusal in words. */
function exceptionComment(comment: string): string {
  const text = (comment ?? "").trim();
  if (text.length === 0) {
    throw new Error("Say why the day is an exception (sick, injured, travelling…): the comment is required.");
  }
  if (LINE_BREAK.test(text)) {
    throw new Error("An exception day's comment is one line: take out the line break.");
  }
  if (text.length > EXCEPTION_COMMENT_MAX) {
    throw new Error(`An exception day's comment is up to ${EXCEPTION_COMMENT_MAX} characters; that one is ${text.length}.`);
  }
  return text;
}

/** `RankUpService.AroundAsync`: folds before and after a change, then pays what it reached. */
function changeDomainInputs(change: () => void): Earned {
  const before = foldNow();
  change();
  const { rankUps, folds } = payRankUps();
  emit();
  return { awards: rankUps.map((r) => r.award), progress: domainChanges(before, folds, rankUps) };
}

/**
 * `POST /api/domains/exceptions`: marks `day` (today when left out) as an exception. Marking it
 * again with the same comment writes nothing; a different comment replaces it.
 */
export function mockMarkException(comment: string, day?: string): ExceptionDayResult {
  const text = exceptionComment(comment);
  const today = todayIso();
  const on = day ?? today;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(on)) throw new Error(`'${on}' isn't a date (yyyy-MM-dd).`);
  if (on > today) {
    throw new Error(`An exception day is today or earlier; ${on} hasn't happened yet (today is ${today}).`);
  }
  const current = state.exceptionDays.find((e) => e.day === on);
  if (current && current.comment === text) return { exception: { ...current }, awards: [], progress: [] };
  const marked = { day: on, comment: text, markedAt: nowIso() };
  const earned = changeDomainInputs(() => {
    state.exceptionDays = [...state.exceptionDays.filter((e) => e.day !== on), marked];
  });
  return { exception: { ...marked }, ...earned };
}

/** `POST /api/domains/exceptions/{day}/unmark`: unmarks `day`; a day that isn't marked writes nothing. */
export function mockUnmarkException(day: string): ExceptionDayResult {
  if (!state.exceptionDays.some((e) => e.day === day)) return { exception: null, awards: [], progress: [] };
  const earned = changeDomainInputs(() => {
    state.exceptionDays = state.exceptionDays.filter((e) => e.day !== day);
  });
  return { exception: null, ...earned };
}

function reconcileStatus(subjectId: string, at: string): Award[] {
  const item = byId(subjectId);
  if (!item || !earns(item.type)) return [];
  const status = (item.status ?? "").trim().toLowerCase();
  if (item.type === "Habit") {
    const award = settle(item, `established:${item.id}`, "established", status === "established", at);
    return award ? [award] : [];
  }
  const awards = [settle(item, `completed:${item.id}`, "completed", status === "completed", at)];
  if (item.type === "Task") {
    const seconds = status === "completed" ? timedSecondsBefore(state.timerEvents, item.id, at) : 0;
    awards.push(
      settle(item, `${TIMED}:${item.id}`, TIMED, seconds > 0, at, () =>
        priceTimed(priceOf(item.type, TIMED, storedBounty(item.id)), seconds, storedEveryMinutes(item.id) ?? 1),
      ),
    );
  }
  return awards.filter((a): a is Award => !!a);
}

interface Run {
  start: string;
  dates: string[];
}

/** Runs of consecutive followed occurrences; any other concluded day breaks one. */
function followedRuns(habitId: string, today: string): Run[] {
  const days = state.occurrences
    .filter((o) => o.habitId === habitId && o.occurrenceDate <= today)
    .filter((o) => !(o.occurrenceDate === today && o.state === "unrecorded"))
    .sort((a, b) => a.occurrenceDate.localeCompare(b.occurrenceDate));
  const runs: Run[] = [];
  let current: Run | null = null;
  for (const o of days) {
    if (o.state === "followed") {
      current ??= { start: o.occurrenceDate, dates: [] };
      current.dates.push(o.occurrenceDate);
    } else {
      if (current) runs.push(current);
      current = null;
    }
  }
  if (current) runs.push(current);
  return runs;
}

function reconcileDay(habitId: string, date: string, at: string): Award[] {
  const item = byId(habitId);
  if (!item) return [];
  // An Established habit keeps earning for the days it's logged (maintenance, migration
  // 0031), but its streaks stopped at graduation. Days recorded after abandoning show in
  // the history and don't pay.
  const maintained = (item.status ?? "").trim().toLowerCase() === "established";
  if (isTerminal(item.status) && !maintained) return [];
  const recorded = state.occurrences.find((o) => o.habitId === habitId && o.occurrenceDate === date)?.state;
  const awards: (Award | null)[] = [
    settle(item, `day:${habitId}:${date}:followed`, "followed", recorded === "followed", at),
    settle(item, `day:${habitId}:${date}:partial`, "partial", recorded === "partial", at),
  ];
  if (maintained) return awards.filter((a): a is Award => !!a);

  const runs = followedRuns(habitId, todayIso());
  const reaches = (start: string, n: number) => runs.some((r) => r.start === start && r.dates.length >= n);
  // Milestones this habit has been paid for, on runs that may have held this day…
  const paid = new Set(
    state.ledger
      .filter((e) => e.subjectId === habitId && e.awardKey?.startsWith("streak_"))
      .map((e) => e.awardKey!)
      .filter((key) => Object.keys(netPaid(key)).length > 0 && key.slice(key.lastIndexOf(":") + 1) <= date),
  );
  for (const key of paid) {
    const n = Number(key.slice("streak_".length, key.indexOf(":")));
    const start = key.slice(key.lastIndexOf(":") + 1);
    awards.push(settle(item, key, `streak_${n}`, reaches(start, n), at));
  }
  // …and the milestones the run holding this day reaches now.
  const run = runs.find((r) => r.dates.includes(date));
  for (const n of STREAK_MILESTONES) {
    const key = `streak_${n}:${habitId}:${run?.start}`;
    if (run && run.dates.length >= n && !paid.has(key)) {
      awards.push(settle(item, key, `streak_${n}`, true, at));
    }
  }
  return awards.filter((a): a is Award => !!a);
}

/** A list row with what its headline trigger pays (`SubjectListItem.bounty`). */
export function withBounty(item: SubjectListItem): SubjectListItem {
  const trigger = BOUNTY_TRIGGERS[item.type]?.[0];
  return { ...item, bounty: trigger ? priceOf(item.type, trigger, storedBounty(item.id)) : null };
}

export function detailWithBounty(detail: SubjectDetail): SubjectDetail {
  const repeating = repeatOf(state, detail.id);
  return {
    ...detail,
    bounty: earns(detail.type)
      ? linesFor(detail.type, storedBounty(detail.id), storedEveryMinutes(detail.id))
      : null,
    bountyFromSeries:
      !!repeating && !repeating.isSeries && rawAttrs(state.details[detail.id]?.attributes).bounty === undefined,
    repeating,
  };
}

/**
 * The parts of a habit's `attributes.establish_target` the kernel honours, read as
 * `v_habit_establishment` reads them: numbers only, a `min_adherence` above 1 as a
 * percentage. A part left out (or unusable) is the default.
 */
function storedTarget(id: string): { minAdherence?: number; windowDays?: number } {
  const target = rawAttrs(state.details[id]?.attributes).establish_target as
    | { min_adherence?: unknown; window_days?: unknown }
    | undefined;
  const min = target?.min_adherence;
  const window = target?.window_days;
  return {
    minAdherence: typeof min === "number" && min > 0 && min <= 100 ? (min <= 1 ? min : min / 100) : undefined,
    windowDays: typeof window === "number" && window >= 1 ? Math.floor(window) : undefined,
  };
}

const DEFAULT_MIN_ADHERENCE = 0.8;
const DEFAULT_WINDOW_DAYS = 60;
const MAX_WINDOW_DAYS = 3650;

/** A habit that isn't tracked yet (migration 0049). */
function isPlanned(status: string | null | undefined): boolean {
  return (status ?? "").trim().toLowerCase() === "planned";
}

/** Today's open occurrence for a habit that starts being tracked, as the kernel's schedule would show it. */
function scheduleToday(habitId: string): void {
  const habit = state.habits.find((h) => h.id === habitId);
  const today = todayIso();
  if (!habit || state.occurrences.some((o) => o.habitId === habitId && o.occurrenceDate === today)) return;
  state.occurrences = [
    ...state.occurrences,
    {
      habitId,
      habitUrn: habit.urn,
      habitName: habit.name,
      occurrenceDate: today,
      state: "unrecorded",
      allowsPartial: habit.allowsPartial,
    },
  ];
}

/**
 * The days from `start` up to `today` a habit spent Planned, from its status history (newest
 * first), counting only spells it has left; a spell since it was made counts from its start.
 * They don't count toward its age for the establish window, as in `v_habit_establishment`.
 */
function plannedDays(habit: HabitRow, start: string, today: string): number {
  const history = state.history[habit.id] ?? [];
  let days = 0;
  history.forEach((h, i) => {
    const until = history[i - 1]?.occurredAt.slice(0, 10);
    if (!isPlanned(h.status) || !until) return;
    const from = h.occurredAt === habit.createdAt ? start : h.occurredAt.slice(0, 10);
    const overlap = (Date.parse(until < today ? until : today) - Date.parse(from > start ? from : start)) / 86_400_000;
    days += Math.max(0, Math.round(overlap));
  });
  return days;
}

function establishment(habit: HabitRow, status: string | null | undefined): HabitEstablishment | null {
  // A Planned habit has no progress; a graduated or abandoned one is past it.
  if (habit.archived || isTerminal(status) || isPlanned(status)) return null;
  const stored = storedTarget(habit.id);
  const minAdherence = stored.minAdherence ?? DEFAULT_MIN_ADHERENCE;
  const windowDays = stored.windowDays ?? DEFAULT_WINDOW_DAYS;
  const today = todayIso();
  const windowStart = addDays(today, -windowDays + 1);
  let followed = 0;
  let partial = 0;
  let missed = 0;
  for (const o of state.occurrences) {
    if (o.habitId !== habit.id || o.occurrenceDate < windowStart || o.occurrenceDate > today) continue;
    if (o.state === "followed") followed++;
    else if (o.state === "partial") partial++;
    // A past day left unrecorded concluded as missed; today stays open until recorded.
    else if (o.state === "not_followed" || o.occurrenceDate < today) missed++;
  }
  const concluded = followed + partial + missed;
  const adherence = concluded > 0 ? Math.round(((followed + 0.5 * partial) / concluded) * 10000) / 10000 : null;
  const start = habit.startDate ?? habit.createdAt.slice(0, 10);
  const ageDays =
    Math.round((Date.parse(today) - Date.parse(start)) / 86_400_000) - plannedDays(habit, start, today);
  return {
    minAdherence,
    windowDays,
    windowStart,
    followed,
    partial,
    missed,
    concluded,
    adherence,
    ageDays,
    targetMet: adherence !== null && adherence >= minAdherence && ageDays >= windowDays,
  };
}

/** `GET /api/habits`: each habit with its status, what a day pays and its progress. */
export function mockHabits(): HabitRow[] {
  return state.habits.map((h) => {
    const status = byId(h.id)?.status ?? null;
    return {
      ...h,
      status,
      bounty: priceOf("Habit", "followed", storedBounty(h.id)),
      establishment: establishment(h, status),
    };
  });
}

/** `bsk habit target` / `POST /api/habits/target`, with HabitTargetService's refusals in its words. */
export function mockSetHabitTarget(req: HabitTargetRequest): HabitTargetResult {
  if (!req.habit?.trim()) throw new Error("habit is required.");
  const setting = req.minAdherence !== undefined || req.windowDays !== undefined;
  if (setting === !!req.clear) {
    throw new Error("Send minAdherence and/or windowDays, or clear — one of them.");
  }
  let min: number | undefined;
  if (req.minAdherence !== undefined) {
    const n = req.minAdherence;
    if (!(n > 0 && n <= 100)) {
      throw new Error(
        `An establish target is the share of days followed: above 0 and up to 1 (0.8), or a percentage up to 100 (80); got ${n}.`,
      );
    }
    min = n <= 1 ? n : n / 100;
  }
  if (req.windowDays !== undefined) {
    const n = req.windowDays;
    if (!Number.isInteger(n) || n < 1 || n > MAX_WINDOW_DAYS) {
      throw new Error(`An establish window is a whole number of days from 1 to ${MAX_WINDOW_DAYS}; got ${n}.`);
    }
  }
  const habit = byRef(req.habit);
  if (!habit) throw new Error(`No subject matches '${req.habit}'.`);
  if (habit.type !== "Habit") {
    throw new Error(`'${habit.title}' is a ${habit.type}; only a Habit has an establish target.`);
  }

  const stored = req.clear ? {} : storedTarget(habit.id);
  const next = { minAdherence: min ?? stored.minAdherence, windowDays: req.windowDays ?? stored.windowDays };
  writeRewardAttrs(habit.id, {
    establish_target: req.clear
      ? undefined
      : {
          ...(next.minAdherence !== undefined ? { min_adherence: next.minAdherence } : {}),
          ...(next.windowDays !== undefined ? { window_days: next.windowDays } : {}),
        },
  });
  emit();
  return {
    habit: { id: habit.id, urn: habit.urn, type: habit.type, title: habit.title },
    minAdherence: next.minAdherence ?? DEFAULT_MIN_ADHERENCE,
    windowDays: next.windowDays ?? DEFAULT_WINDOW_DAYS,
    adherenceIsCustom: next.minAdherence !== undefined,
    windowIsCustom: next.windowDays !== undefined,
  };
}

/** `bsk bounty` / `POST /api/bounty`, with BountyService's refusals in its words. */
export function mockSetBounty(req: BountyRequest): BountyResult {
  const item = byRef(req.subject);
  if (!item) throw new Error(`No subject matches '${req.subject}'.`);
  const asks = (req.amounts && Object.keys(req.amounts).length > 0 ? 1 : 0) + (req.nothing ? 1 : 0) + (req.clear ? 1 : 0);
  const minutes = req.everyMinutes;
  if (asks > 1 || (asks === 0 && minutes == null) || (req.clear && minutes != null)) {
    throw new Error(
      "Send one of amounts, nothing or clear (everyMinutes may come alone, with amounts or with nothing).",
    );
  }
  const changes: Amounts = {};
  for (const [key, n] of Object.entries(req.amounts ?? {})) {
    const c = CURRENCIES.find((x) => x.key === key.trim().toLowerCase());
    if (!c) {
      throw new Error(`Unknown currency '${key}'. Expected one of: ${CURRENCIES.map((x) => x.key).join(", ")}.`);
    }
    if (!Number.isInteger(n) || n < 0) {
      throw new Error(`A bounty pays a whole number of ${c.label} from zero up; got ${n}.`);
    }
    if (changes[c.key] !== undefined) throw new Error(`${c.label} is listed twice.`);
    changes[c.key] = n;
  }
  const triggers = BOUNTY_TRIGGERS[item.type] ?? [];
  if (triggers.length === 0) {
    throw new Error(`'${item.title}' is a ${item.type}; only Task, Project, Goal, Habit earn rewards.`);
  }
  const asked = req.trigger?.trim().toLowerCase();
  if (asked === "partial" && triggers.includes("followed")) {
    throw new Error("A partial day always pays half of followed; price followed instead.");
  }
  // `BountyTriggers.Implied`: the only trigger, else completed.
  const implied = triggers.length === 1 ? triggers[0] : triggers.includes("completed") ? "completed" : undefined;
  const trigger = !asked
    ? req.clear
      ? undefined
      : (implied ??
        (() => {
          throw new Error(`Say which trigger to price: ${triggers.join(", ")}.`);
        })())
    : triggers.find((t) => t === asked);
  if (asked && !trigger) {
    throw new Error(`A ${item.type} doesn't pay on '${req.trigger?.trim()}'. Expected: ${triggers.join(", ")}.`);
  }
  if (minutes != null) {
    if (trigger !== TIMED) throw new Error(`Only ${TIMED} pays by the minutes on the timer; ${trigger} pays once.`);
    if (!Number.isInteger(minutes) || minutes < 1) {
      throw new Error(`Pay for every whole number of minutes on the timer from 1 up; got ${minutes}.`);
    }
  }

  const stored = storedBounty(item.id);
  let every = storedEveryMinutes(item.id);
  if (req.clear) {
    if (trigger) delete stored[trigger];
    else for (const key of Object.keys(stored)) delete stored[key];
  } else if (req.nothing) {
    stored[trigger!] = {};
    every = minutes ?? every;
  } else {
    const price = priceOf(item.type, trigger!, stored);
    for (const [k, n] of Object.entries(changes)) {
      if (n === 0) delete price[k];
      else price[k] = n;
    }
    stored[trigger!] = Object.fromEntries(orderedAmounts(price));
    every = minutes ?? every;
  }
  // The minutes live inside `timed`, so clearing it clears them.
  if (!stored[TIMED]) every = null;
  writeRewardAttrs(item.id, { bounty: Object.keys(stored).length > 0 ? bountyJson(stored, every) : undefined });
  emit();
  return {
    subject: { id: item.id, urn: item.urn, type: item.type, title: item.title },
    lines: linesFor(item.type, stored, every),
  };
}

// ---------------------------------------------------------------------------
// Covers (COVER-1, cover-images plan §3.3 and §3.9): CoverService against the bundled scenes.
// The cover lives in `attributes.cover` as the kernel stores it, and reads decorate it.
// ---------------------------------------------------------------------------

function storedCover(subjectId: string): CoverRead | null {
  return readStoredCover(rawAttrs(state.details[subjectId]?.attributes).cover);
}

export function withCover<T extends { id: string; type: SubjectListItem["type"] }>(item: T): T & { cover: CoverRead | null } {
  return { ...item, cover: canHaveCover(item.type) ? storedCover(item.id) : null };
}

function coverable(subject: string): SubjectListItem {
  const item = byRef(subject);
  if (!item) throw new Error(`No subject matches '${subject}'.`);
  if (!canHaveCover(item.type)) {
    throw new Error(`'${item.title}' is a ${item.type}; only identity statements (Value), goals, projects and habits have covers.`);
  }
  return item;
}

function checkPosition(positionY: number): number {
  if (!Number.isInteger(positionY) || positionY < 0 || positionY > 100) {
    throw new Error(`A cover's position runs from 0 (top) to 100 (bottom); got ${positionY}.`);
  }
  return positionY;
}

function coverSubject(item: SubjectListItem) {
  return { id: item.id, urn: item.urn, type: item.type, title: item.title };
}

/** `bsk cover set` / `POST /api/cover`: a scene by id, centred at `positionY`. */
export function mockSetCover(req: CoverRequest): CoverResult {
  const scene = sceneById(req.photoId?.trim() ?? "");
  const position = checkPosition(req.positionY ?? DEFAULT_POSITION_Y);
  const item = coverable(req.subject);
  if (!scene) throw new Error("That photo is no longer on Unsplash.");
  writeRewardAttrs(item.id, { cover: storedCoverOf(scene, position, nowIso()) });
  emit();
  return { subject: coverSubject(item), cover: storedCover(item.id)!, downloadCounted: true, downloadProblem: null };
}

/** `bsk cover move` / `POST /api/cover/position`. Refused when there's no cover. */
export function mockMoveCover(subject: string, positionY: number): CoverResult {
  const position = checkPosition(positionY);
  const item = coverable(subject);
  const stored = rawAttrs(state.details[item.id]?.attributes).cover;
  if (!storedCover(item.id) || !stored || typeof stored !== "object") {
    throw new Error(`'${item.title}' has no cover to move.`);
  }
  writeRewardAttrs(item.id, { cover: { ...(stored as Record<string, unknown>), position_y: position } });
  emit();
  return { subject: coverSubject(item), cover: storedCover(item.id)!, downloadCounted: false, downloadProblem: null };
}

/** `bsk cover clear` / `POST /api/cover/clear`: works on any subject; says whether there was a cover. */
export function mockClearCover(subject: string): CoverCleared {
  const item = byRef(subject);
  if (!item) throw new Error(`No subject matches '${subject}'.`);
  const removed = rawAttrs(state.details[item.id]?.attributes).cover !== undefined;
  if (removed) {
    writeRewardAttrs(item.id, { cover: undefined });
    emit();
  }
  return { subject: coverSubject(item), removed };
}

// ---------------------------------------------------------------------------
// Activity XP (activity plan §3.8, A8–A9): `ActivityRewards` over the mock's Oura days
// ---------------------------------------------------------------------------

const ACTIVITY_KEY = /^(steps|exercise|sleep):(\d{4}-\d{2}-\d{2})$/;
const ACTIVITY_NOTES: Record<string, string> = { steps: "Steps", exercise: "Exercise", sleep: "Sleep" };

function activityNote(trigger: string, day: string): string {
  return `${ACTIVITY_NOTES[trigger] ?? "Sleep"} ${noteDay(day)}`;
}

/** An instant on `day` at local `hh:00`. */
function localAt(day: string, hour: number): string {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d, hour).toISOString();
}

/**
 * `ActivityRewards.CandidatesAsync`: from the day activity XP starts, the steps and counted
 * exercise of each day Oura has finished (at 4 a.m. the next morning), and the sleep of each
 * day with a score once its night is over. The mock's Oura days never change, so each part
 * cites one fixed source.
 */
function activityCandidates(): DueFood[] {
  const now = new Date();
  const today = todayIso();
  const from = ACTIVITY_RULES.xpFrom;
  if (today < from) return [];
  const ouraToday = ouraDayAt(now);
  const { days, workouts } = mockActivityRecords();
  const due: DueFood[] = [];
  for (const d of days) {
    if (d.day < from || d.day > today) continue;
    if (d.day < ouraToday && d.steps != null) {
      due.push({
        key: `steps:${d.day}`,
        trigger: "steps",
        day: d.day,
        base: stepsXp(d.steps),
        triggeredAt: localAt(addDays(d.day, 1), 4),
        sources: [`mock-activity-${d.day}`],
        note: activityNote("steps", d.day),
      });
    }
    const woke = d.wokeAt ?? localAt(d.day, 8);
    if (d.sleepScore != null && Date.parse(woke) <= now.getTime()) {
      due.push({
        key: `sleep:${d.day}`,
        trigger: "sleep",
        day: d.day,
        base: sleepXp(d.sleepScore),
        triggeredAt: woke,
        sources: [`mock-sleep-${d.day}`],
        note: activityNote("sleep", d.day),
      });
    }
  }
  const byDay = new Map<string, typeof workouts>();
  for (const w of workouts) {
    if (!w.counted || w.day < from || w.day >= ouraToday) continue;
    byDay.set(w.day, [...(byDay.get(w.day) ?? []), w]);
  }
  for (const [day, counted] of byDay) {
    due.push({
      key: `exercise:${day}`,
      trigger: "exercise",
      day,
      base: exerciseXp(exerciseSeconds(counted)),
      triggeredAt: counted.map((w) => w.endedAt).sort().at(-1)!,
      sources: counted.map((w) => w.id),
      note: activityNote("exercise", day),
    });
  }
  return due;
}

/**
 * `ActivityRewards.SettleAsync`: activity never opens the wallet, and pays the Oura days from
 * the one it opened on.
 */
function settleActivityDays(): Earned {
  const open = state.ledger.find((e) => e.entry === "open");
  if (!open) return { awards: [], progress: [] };
  const openedAt = Date.parse(open.occurredAt);
  return earning(() =>
    planKeyed(
      activityCandidates(),
      heldKeyed(ACTIVITY_KEY),
      openedAt,
      (key) => {
        const [, trigger, day] = ACTIVITY_KEY.exec(key)!;
        return { trigger, note: activityNote(trigger, day) };
      },
      ouraDayAt(new Date(openedAt)),
    ),
  );
}

/** Settles activity XP, as the page's sync and `POST /api/domains/settle` do. */
export function mockSettleActivity(): Earned {
  const earned = settleActivityDays();
  if (earned.awards.length > 0) emit();
  return earned;
}

/** What the ledger pays each Oura day from `from` to `to` (`ActivityPaidReads`). */
export function mockActivityPaid(from: string, to: string): Map<string, ActivityPaid> {
  const byDay = new Map<string, ActivityPaid>();
  for (const held of heldKeyed(ACTIVITY_KEY).values()) {
    const [, trigger, day] = ACTIVITY_KEY.exec(held.key)!;
    if (!held.paid || day < from || day > to) continue;
    const award = {
      base: latestBase(held),
      paid: held.net[ACTIVITY_RULES.currency] ?? 0,
      multiplier: held.latestEarn?.multipliers?.[ACTIVITY_RULES.currency] ?? null,
    };
    const current = byDay.get(day) ?? { steps: null, exercise: null, sleep: null, total: 0, baseTotal: 0 };
    const next: ActivityPaid = { ...current, [trigger]: award };
    next.total = (next.steps?.paid ?? 0) + (next.exercise?.paid ?? 0) + (next.sleep?.paid ?? 0);
    next.baseTotal = (next.steps?.base ?? 0) + (next.exercise?.base ?? 0) + (next.sleep?.base ?? 0);
    byDay.set(day, next);
  }
  return byDay;
}
