import {
  objectUrlFor,
  revokeIfBlobUrl,
} from "@/lib/artifacts";
import { deriveTitle } from "@/lib/capture";
import { nowIso, slugify, todayIso, startOfWeekSunday, endOfWeekSaturday } from "@/lib/dates";
import type {
  ArtifactRecord,
  BinaryCaptureResult,
  CaptureKind,
  CreatedSubject,
  EventRelation,
  InboxItem,
  InboxResolution,
  NewSubjectRequest,
  RecurrenceSpec,
  Relation,
  RenamedSubject,
  RenamedTag,
  ReviewBody,
  ReviewDoc,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
  TaskTimer,
  TimerAction,
  TimerActionResult,
} from "@/lib/production-ui-types";
import { defaultStatus, isTerminal } from "@/lib/production-ui-types";
import { applyStreakToHabit } from "@/lib/habit-streak";
import { foldTimer, type TimerEvent } from "@/lib/timer";
import { createSeed, type CanonicalEdge, type MockState } from "@/lib/mock/seed";

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
  const bag = safeAttrs(detail.attributes);
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

export function setStatus(subject: string, status: string) {
  const item = byRef(subject);
  if (!item) return;
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
  emit();
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
    setStatus(task.id, "Completed");
    return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: true };
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
  return { timer: timerFor(task.id) ?? neverTimed(task), paused, completed: false };
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
) {
  const habit = state.habits.find((h) => h.id === habitRef || h.urn === habitRef);
  if (!habit) return;
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
  emit();
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

export type { CanonicalEdge, MockState };
