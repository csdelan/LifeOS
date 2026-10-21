import {
  objectUrlFor,
  revokeIfBlobUrl,
} from "@/lib/artifacts";
import { deriveTitle } from "@/lib/capture";
import { addDays, nowIso, slugify, todayIso, startOfWeekSunday, endOfWeekSaturday } from "@/lib/dates";
import type {
  Amounts,
  ArtifactRecord,
  Award,
  BountyRequest,
  BountyResult,
  HabitEstablishment,
  HabitTargetRequest,
  HabitTargetResult,
  HabitRow,
  BinaryCaptureResult,
  CaptureKind,
  CreatedSubject,
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
  TaskTimer,
  TimerAction,
  TimerActionResult,
  Wallet,
} from "@/lib/production-ui-types";
import { defaultStatus, isTerminal } from "@/lib/production-ui-types";
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
import { applyStreakToHabit } from "@/lib/habit-streak";
import { CURRENCIES, coinsToUsd, currency, orderedAmounts, shortfall } from "@/lib/rewards";
import { foldTimer, timedSecondsBefore, type TimerEvent } from "@/lib/timer";
import {
  createSeed,
  type CanonicalEdge,
  type MockLedgerEntry,
  type MockState,
} from "@/lib/mock/seed";

let state: MockState = createSeed();
const listeners = new Set<() => void>();

export function getState(): MockState {
  return state;
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function emit() {
  for (const listener of listeners) listener();
}

export function resetStore() {
  revokeArtifacts(state);
  state = createSeed();
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

function newId(): string {
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
  const status = defaultStatus(req.type) || null;
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
        recurrence: attrs.recurrence ?? JSON.stringify({ kind: "daily" }),
        archived: false,
        createdAt,
        currentStreak: 0,
        lastState: "unrecorded",
      },
      ...state.habits,
    ];
    state.occurrences = [
      ...state.occurrences,
      {
        habitId: id,
        habitUrn: urn,
        habitName: req.title,
        occurrenceDate: todayIso(),
        state: "unrecorded",
        allowsPartial,
      },
    ];
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

export function applyAttrs(subjectId: string, attrs: Record<string, string>) {
  const list = byId(subjectId);
  const detail = state.details[subjectId];
  if (!list || !detail) return;
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

export function setStatus(subject: string, status: string): Award[] {
  const item = byRef(subject);
  if (!item) return [];
  const occurredAt = nowIso();
  state.subjects = state.subjects.map((s) =>
    s.id === item.id ? { ...s, status } : s,
  );
  const detail = state.details[item.id];
  if (detail) state.details[item.id] = { ...detail, status };
  state.history[item.id] = [
    { id: newId(), status, occurredAt },
    ...(state.history[item.id] ?? []),
  ];
  const awards = reconcileStatus(item.id, occurredAt);
  emit();
  return awards;
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
 * refusals, one running timer at a time (starting or resuming pauses the other), and
 * Complete = stop, then Status → Completed.
 */
export function applyTimer(taskRef: string, action: TimerAction): TimerActionResult {
  const task = byRef(taskRef);
  if (!task) throw new Error(`No subject matches '${taskRef}'.`);
  if (task.type !== "Task") {
    throw new Error(`'${task.title}' is a ${task.type}, not a Task; the timer is kept on Tasks.`);
  }
  const current = timerFor(task.id)?.state ?? "stopped";
  const at = nowIso();
  const paused: CreatedSubject[] = [];

  if (action === "complete") {
    if (current !== "stopped") {
      state.timerEvents = [...state.timerEvents, { subjectId: task.id, action: "stop", occurredAt: at }];
    }
    const awards = setStatus(task.id, "Completed");
    return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: true, awards };
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
  return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: false, awards: [] };
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

export function captureNote(text: string) {
  const eventId = newId();
  const occurredAt = nowIso();
  state.events = [
    { id: eventId, kind: "note", content: text, occurredAt },
    ...state.events,
  ];
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

function putArtifact(opts: {
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

export function adhereHabit(
  habitRef: string,
  adherence: "followed" | "partial" | "missed",
  on?: string,
  note?: string,
): Award[] {
  const habit = state.habits.find((h) => h.id === habitRef || h.urn === habitRef);
  if (!habit) return [];
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
  const awards = reconcileDay(habit.id, date, nowIso());
  emit();
  return awards;
}

export function setHabitRecurrence(habitRef: string, spec: RecurrenceSpec) {
  const habit = state.habits.find((h) => h.id === habitRef || h.urn === habitRef);
  if (!habit) return;
  const raw = JSON.stringify(spec);
  state.habits = state.habits.map((h) =>
    h.id === habit.id ? { ...h, recurrence: raw } : h,
  );
  applyAttrs(habit.id, { recurrence: raw });
  emit();
}

export function ensureReviewDocs(today = todayIso()): ReviewDoc[] {
  const weekStart = startOfWeekSunday(today);
  const weekEnd = endOfWeekSaturday(today);
  if (!state.reviews.some((r) => r.kind === "daily" && r.date === today)) {
    state.reviews = [makeReview("daily", today, weekStart, weekEnd), ...state.reviews];
  }
  if (!state.reviews.some((r) => r.kind === "weekly" && r.weekStart === weekStart)) {
    state.reviews = [makeReview("weekly", weekStart, weekStart, weekEnd), ...state.reviews];
  }
  return state.reviews;
}

export function ensureReview(kind: ReviewDoc["kind"], date: string): ReviewDoc {
  ensureReviewDocs();
  const weekStart = startOfWeekSunday(date);
  const weekEnd = endOfWeekSaturday(weekStart);
  if (kind === "daily") {
    let found = state.reviews.find((r) => r.kind === "daily" && r.date === date);
    if (!found) {
      found = makeReview("daily", date, weekStart, weekEnd);
      state.reviews = [found, ...state.reviews];
    }
    if (!state.reviews.some((r) => r.kind === "weekly" && r.weekStart === weekStart)) {
      state.reviews = [makeReview("weekly", weekStart, weekStart, weekEnd), ...state.reviews];
    }
    return found;
  }
  let found = state.reviews.find((r) => r.kind === "weekly" && r.weekStart === weekStart);
  if (!found) {
    found = makeReview("weekly", weekStart, weekStart, weekEnd);
    state.reviews = [found, ...state.reviews];
  }
  return found;
}

function makeReview(
  kind: ReviewDoc["kind"],
  date: string,
  weekStart: string,
  weekEnd: string,
): ReviewDoc {
  return {
    id: `review-${kind}-${date}`,
    kind,
    date,
    weekStart,
    weekEnd,
    completedAt: null,
    missed: false,
    workedWell: "",
    differently: "",
    planning: "",
    keepFocus: true,
  };
}

export function saveReviewBody(id: string, body: ReviewBody) {
  ensureReviewDocs();
  state.reviews = state.reviews.map((r) => (r.id === id ? { ...r, ...body } : r));
  emit();
}

export function completeReviewDoc(id: string) {
  ensureReviewDocs();
  state.reviews = state.reviews.map((r) =>
    r.id === id ? { ...r, completedAt: nowIso(), missed: false } : r,
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

function storedBounty(subjectId: string): Record<string, Amounts> {
  return parseBounty(rawAttrs(state.details[subjectId]?.attributes).bounty);
}

function storedEveryMinutes(subjectId: string): number | null {
  return parseEveryMinutes(rawAttrs(state.details[subjectId]?.attributes).bounty);
}

function netPaid(awardKey: string): Amounts {
  const net: Amounts = {};
  for (const e of state.ledger) {
    if (e.awardKey !== awardKey || (e.entry !== "earn" && e.entry !== "reversal")) continue;
    for (const [k, n] of Object.entries(e.amounts)) net[k] = (net[k] ?? 0) + n;
  }
  return Object.fromEntries(Object.entries(net).filter(([, n]) => n !== 0));
}

function settle(
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
  if (due && !paid) {
    amounts = price();
    // A trigger priced to pay nothing writes nothing.
    if (Object.keys(amounts).length === 0) return null;
    entry = "earn";
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
    { id, occurredAt: at, entry, provenance: "derived", amounts, trigger, awardKey, subjectId: item.id },
  ];
  return {
    id,
    entry,
    trigger,
    awardKey,
    subject: { id: item.id, urn: item.urn, type: item.type, title: item.title },
    amounts: { ...amounts },
  };
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
  return {
    ...detail,
    bounty: earns(detail.type)
      ? linesFor(detail.type, storedBounty(detail.id), storedEveryMinutes(detail.id))
      : null,
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

function establishment(habit: HabitRow, status: string | null | undefined): HabitEstablishment | null {
  if (habit.archived || isTerminal(status)) return null;
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
  const ageDays = Math.round((Date.parse(today) - Date.parse(start)) / 86_400_000);
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
