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
  NewSubjectRequest,
  RecurrenceSpec,
  Relation,
  ReviewBody,
  ReviewDoc,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
} from "@/lib/production-ui-types";
import { defaultStatus, isTerminal } from "@/lib/production-ui-types";
import { applyStreakToHabit } from "@/lib/habit-streak";
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
  for (const t of add) current.add(t.trim());
  for (const t of remove) current.delete(t.trim());
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

export function appendJournal(subject: string, text: string) {
  const item = byRef(subject);
  if (!item) return;
  const entry = { eventId: newId(), occurredAt: nowIso(), content: text };
  state.journals[item.id] = [entry, ...(state.journals[item.id] ?? [])];
  emit();
}

export function flagItem(itemRef: string) {
  const subject = byRef(itemRef);
  const already = state.inbox.some(
    (i) => i.itemId === itemRef || i.subjectUrn === subject?.urn,
  );
  if (already) return;
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

export function removeInbox(itemId: string) {
  state.inbox = state.inbox.filter((i) => i.itemId !== itemId);
  emit();
}

/** Drop: subjects move to a terminal status; events are dismissed. Not a delete. */
export function dropInboxItem(itemRef: string) {
  const row = state.inbox.find(
    (i) => i.itemId === itemRef || i.subjectUrn === itemRef,
  );
  if (row?.itemKind === "subject" && row.subjectUrn) {
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
  }
  const id = row?.itemId ?? itemRef;
  state.inbox = state.inbox.filter(
    (i) => i.itemId !== id && i.subjectUrn !== itemRef,
  );
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
