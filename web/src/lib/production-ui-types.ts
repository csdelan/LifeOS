/**
 * LifeOS — Production Web UI seed types.
 *
 * INTERIM, hand-authored mirror of the kernel's read models and write-verb surface,
 * for scaffolding the first-pass UI against mock data. Sources:
 *   - Read models:  src/LifeOs.Pilot/Reader/ReadModels.cs
 *   - Verb surface: docs/pilot/ui-refactor-plan.md §3
 *   - Vocabulary:   src/LifeOs.Pilot/Shell/PilotVocab.cs (mirror of the Domain)
 *
 * In Production this file is REPLACED by the OpenAPI-generated client (types come from
 * LifeOs.Api, which is built from the Domain — the single source of truth). Until then,
 * a Cursor agent copies this into web/src/ and generates mock data from it.
 *
 * Date conventions (matching the kernel readers):
 *   - `*Date` / `due` / `scheduled` / `targetDate` / `occurrenceDate` : "YYYY-MM-DD"
 *   - `*At` (createdAt, occurredAt, triagedAt, nextReviewAt)          : ISO-8601 timestamp
 */

// ---------------------------------------------------------------------------
// Vocabulary (interim mirror of PilotVocab / the Domain). Presentation-facing.
// ---------------------------------------------------------------------------

/** The subject types (11 core + Area, Habit, Appointment). */
export type SubjectType =
  | "Value"        // shown as "Identity Statement"
  | "Goal"
  | "Problem"
  | "Project"
  | "Task"
  | "Commitment"
  | "Decision"
  | "Idea"
  | "Person"       // shown as "Person / Agent"
  | "Constraint"
  | "Season"
  | "Area"
  | "Habit"
  | "Appointment";

/** Alignment-graph edge relations (subject → subject). */
export type Relation = "serves" | "results_in" | "supersedes";

/** Event → subject edge relations. */
export type EventRelation = "concerns" | "evidences" | "violates";

/** Types offered by global New (GEN-6). */
export const CREATABLE_TYPES: SubjectType[] = [
  "Value", "Goal", "Project", "Task", "Problem", "Decision",
  "Idea", "Person", "Area", "Habit", "Appointment", "Commitment",
];

/**
 * Types offered as `bsk link` targets in the relationship editor.
 * Project/Task first — the usual link targets — then the rest of CREATABLE_TYPES.
 */
export const LINK_TARGET_TYPES: SubjectType[] = [
  "Project", "Task", "Goal", "Value",
  "Problem", "Decision", "Idea", "Person",
  "Area", "Habit", "Appointment", "Commitment",
];

/** User-facing label for a subject type. */
export function typeLabel(type: SubjectType): string {
  switch (type) {
    case "Value": return "Identity Statement";
    case "Person": return "Person / Agent";
    default: return type;
  }
}

/** Per-type status vocabularies (D7). Status moves only by state_change events. */
export const STATUS_BY_TYPE: Partial<Record<SubjectType, string[]>> = {
  Goal:        ["New", "Active", "Completed", "Abandoned"],
  Project:     ["New", "Active", "Completed", "Abandoned"],
  Task:        ["Not started", "In progress", "Waiting", "Completed", "Cancelled"],
  Commitment:  ["Open", "Fulfilled", "Missed", "Cancelled"],
  Decision:    ["Open", "Implementing", "Cancelled", "Closed"],
  Problem:     ["Open", "Working", "Resolved", "Cancelled"],
  Appointment: ["Scheduled", "Completed", "Cancelled", "Missed"],
  Idea:        ["New", "Promoted", "Rejected"],
  // Value (Identity Statement), Area, Habit, Person: no status workflow.
};

/** Statuses that drop an item from default views (terminal). */
export const TERMINAL_STATUSES = new Set([
  "done", "completed", "resolved", "closed", "cancelled",
  "abandoned", "dropped", "archived", "superseded",
  "fulfilled", "missed", "promoted", "rejected",
]);

export function isTerminal(status?: string | null): boolean {
  return !!status && TERMINAL_STATUSES.has(status.trim().toLowerCase());
}

/** The default status for a type (first in its vocabulary), or "" if status-less. */
export function defaultStatus(type: SubjectType): string {
  return STATUS_BY_TYPE[type]?.[0] ?? "";
}

/**
 * Canonical parent→child relation map (GEN-7). Key is [childType, parentType].
 * The child is the edge's `from`, the parent the `to`. Used for inline create-child
 * in the Map/tree; unambiguous pairs infer the relation silently.
 */
export const PARENT_MAP: Array<{ child: SubjectType; parent: SubjectType; relation: Relation }> = [
  { child: "Goal",    parent: "Value",   relation: "serves" },
  { child: "Project", parent: "Goal",    relation: "results_in" },
  { child: "Task",    parent: "Project", relation: "serves" },
  { child: "Task",    parent: "Goal",    relation: "serves" },
];

export function inferChildRelation(child: SubjectType, parent: SubjectType): Relation | undefined {
  return PARENT_MAP.find((m) => m.child === child && m.parent === parent)?.relation;
}

/**
 * Attribute keys the UI may send through `setAttributes` for each type.
 * Cross-cutting: `area`, `expected_cadence`, `next_review_at` (not on Area).
 * `due` (Task) and `target_date` (Goal/Project) are independent (GEN-10).
 * Title is a first-class column and status moves only by event — never attributes.
 */
export const ATTR_KEYS_BY_TYPE: Record<SubjectType, readonly string[]> = {
  Value: ["statement", "why_it_matters", "notes"],
  Goal: ["desired_end_state", "target_date", "description", "motivation"],
  Project: ["description", "start_date", "target_date", "notes"],
  Task: ["description", "due", "scheduled", "estimated_duration", "priority"],
  Problem: ["description", "date_identified", "impact"],
  Decision: ["description", "decision_date"],
  Person: ["role", "description", "contact", "notes", "person_kind"],
  Area: ["description", "notes"],
  Habit: ["cue", "routine", "reward", "start", "end", "allows_partial", "recurrence"],
  Appointment: ["date", "start", "end", "location", "meeting_link", "notes", "all_day", "recurrence", "attendees"],
  Idea: ["description", "notes"],
  Commitment: ["description", "notes", "due", "recurrence"],
  Constraint: ["scope", "limit", "notes"],
  Season: ["focus", "ends", "notes"],
};

const CROSS_CUTTING = ["area", "expected_cadence", "next_review_at"] as const;

export function pickApplicableAttrs(
  type: SubjectType,
  attrs: Record<string, string>,
): Record<string, string> {
  const allowed = new Set<string>(ATTR_KEYS_BY_TYPE[type]);
  if (type !== "Area") {
    for (const key of CROSS_CUTTING) allowed.add(key);
  }
  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(attrs)) {
    if (allowed.has(key)) out[key] = value;
  }
  return out;
}

export function childTypesFor(parent: SubjectType): SubjectType[] {
  return [...new Set(PARENT_MAP.filter((m) => m.parent === parent).map((m) => m.child))];
}

/** Roles a Person can hold on a subject (non-alignment association). */
export const PERSON_ROLES = ["attendee", "owner", "assignee", "waiting_for", "involves"] as const;
export type PersonRole = (typeof PERSON_ROLES)[number];

/** Habit occurrence / adherence states (GEN-3). */
export type AdherenceState = "unrecorded" | "followed" | "partial" | "not_followed";

/** Recurrence for Habits, Commitments, Appointments (app-layer; kernel TBD). */
export type RecurrenceKind = "daily" | "weekly" | "interval";
export interface RecurrenceSpec {
  kind: RecurrenceKind;
  /** weekly: 0 = Sunday … 6 = Saturday. Empty / omitted = every day. */
  weekdays?: number[];
  /** interval: every N days (from start). */
  intervalDays?: number;
}

/** Review working document (REVIEW-1/2). Mock-only until the kernel ships D3. */
export type ReviewKind = "daily" | "weekly";
export interface ReviewBody {
  workedWell: string;
  differently: string;
  planning: string;
  keepFocus: boolean;
}
export interface ReviewDoc extends ReviewBody {
  id: string;
  kind: ReviewKind;
  /** Daily: the calendar day. Weekly: the Sunday the week starts. */
  date: string;
  weekStart: string;
  weekEnd: string;
  completedAt?: string | null;
  missed: boolean;
}

/** Inbox item kinds — an event (raw capture) or a subject (Idea/Problem flagged). */
export type ItemKind = "event" | "subject";

// ---------------------------------------------------------------------------
// Read models (mirror of ReadModels.cs — the API's read responses).
// ---------------------------------------------------------------------------

/** A subject type and its count — the Browse/type tree. */
export interface TypeCount {
  type: SubjectType;
  n: number;
}

/** One row in a subject list (Map, Browse, Goals, Projects, Tasks, …). */
export interface SubjectListItem {
  id: string;              // uuid
  urn: string;             // urn:bsk:<type>:<slug>-<shortid>
  type: SubjectType;
  title: string;
  status?: string | null;  // folded current status (null → use defaultStatus)
  due?: string | null;         // "YYYY-MM-DD"
  scheduled?: string | null;   // "YYYY-MM-DD" (do-date, independent of due)
  targetDate?: string | null;  // "YYYY-MM-DD"
  area?: string | null;        // Area urn
  areaName?: string | null;
  tags?: string | null;        // comma-joined; prefer the tag endpoints for editing
  archived: boolean;
  createdAt: string;           // ISO timestamp
  expectedCadence?: string | null;
  nextReviewAt?: string | null;
  personKind?: string | null;  // "human" | "ai"
  visionOrder?: number | null;
}

/** Detail-pane header for one subject, plus the raw attributes JSON. */
export interface SubjectDetail {
  id: string;
  urn: string;
  type: SubjectType;
  title: string;
  status?: string | null;
  due?: string | null;
  targetDate?: string | null;  // independent of due (GEN-10)
  expectedCadence?: string | null;
  nextReviewAt?: string | null;
  scope?: string | null;
  statement?: string | null;   // Value's first-person identity statement
  createdAt: string;
  archived: boolean;
  area?: string | null;
  areaName?: string | null;
  attributes?: string | null;  // raw attributes jsonb as text
}

/** One alignment-graph edge (the tree/graph is built from these). */
export interface RelationEdge {
  relation: Relation;
  urn: string;             // the OTHER end's urn
  type: SubjectType;       // the OTHER end's type
  subjectId: string;       // the OTHER end's id
  title?: string | null;
}

/** An event that concerns a subject. */
export interface ConcerningEvent {
  kind: string;            // journal | note | voice | observation | activity | …
  occurredAt: string;
  eventId: string;
  content?: string | null;
}

/** One recorded state_change in a subject's status history. */
export interface StatusHistoryEntry {
  status: string;
  occurredAt: string;
  id: string;
}

/** An append-only journal entry (journal-kind event concerning the subject). */
export interface JournalEntry {
  eventId: string;
  occurredAt: string;
  content: string;
}

/**
 * One flagged item awaiting triage (v_inbox, INBOX-1). Membership is asserted, not
 * inferred. An item is an event (raw capture) or a subject (Idea/Problem flagged on
 * creation). Resolve via Promote / Relate / Dismiss / Drop (see write client).
 */
export interface InboxItem {
  itemId: string;
  itemKind: ItemKind;
  triagedAt: string;
  subjectUrn?: string | null;
  subjectType?: SubjectType | null;
  subjectTitle?: string | null;
  eventKind?: string | null;
  eventContent?: string | null;
}

export interface AreaRow {
  id: string;
  urn: string;
  name: string;
  description?: string | null;
  notes?: string | null;
  createdAt: string;
}

export interface TagUniverseItem {
  tag: string;
  itemCount: number;
}

export interface HabitRow {
  id: string;
  urn: string;
  name: string;
  cue?: string | null;
  routine?: string | null;
  reward?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  allowsPartial: boolean;
  recurrence?: string | null; // structured recurrence as text
  archived: boolean;
  createdAt: string;
  currentStreak: number;
  lastState?: string | null;
}

export interface HabitOccurrenceRow {
  habitId: string;
  habitUrn: string;
  habitName: string;
  occurrenceDate: string;   // "YYYY-MM-DD"
  state: AdherenceState;
  allowsPartial: boolean;
  note?: string | null;
}

export interface AppointmentRow {
  id: string;
  urn: string;
  title: string;
  date?: string | null;         // "YYYY-MM-DD" (null on a series header)
  startTime?: string | null;
  endTime?: string | null;
  allDay?: string | null;
  location?: string | null;
  meetingLink?: string | null;
  area?: string | null;
  seriesUrn?: string | null;
  recurrence?: string | null;
  status: string;               // Scheduled | Completed | Cancelled | Missed
  archived: boolean;
  createdAt: string;
}

export interface PersonRow {
  id: string;
  urn: string;
  title: string;                // the person's name
  personKind?: string | null;   // "human" | "ai"
  role?: string | null;
  archived: boolean;
}

export interface PersonAssociationRow {
  subjectId: string;
  subjectUrn: string;
  subjectType: SubjectType;
  subjectTitle: string;
  role: PersonRole | string;
  personId: string;
  personUrn: string;
  personName: string;
}

// ---------------------------------------------------------------------------
// Write surface (the API's write endpoints; mirror of the bsk verb table).
// In the first pass these are mocked (optimistic UI); later they hit LifeOs.Api.
// ---------------------------------------------------------------------------

/** Result of creating a subject (bsk new --json). */
export interface CreatedSubject {
  id: string;
  urn: string;
  type: SubjectType;
  title: string;
}

export interface NewSubjectRequest {
  type: SubjectType;
  title: string;
  area?: string;                    // Area urn
  parent?: string;                  // parent ref → atomic create-and-link (GEN-7)
  relation?: Relation;              // override inferred relation when ambiguous
  attrs?: Record<string, string>;   // initial attributes → bsk set
}

/** Deferred defer semantics for Inbox triage (INBOX-3). */
export type DeferKind = "date" | "days" | "hours" | "someday" | "postponed";
export interface DeferRequest { kind: DeferKind; value?: string | number }

/**
 * The write client the UI calls. Every method maps to a bsk verb / API endpoint.
 * Mock all of these for the first pass; return shapes match the real API.
 */
export interface LifeOsWriteClient {
  newSubject(req: NewSubjectRequest): Promise<CreatedSubject>;
  /** source = event id (capture→subject) or subject ref (Idea→work via results_in). */
  promote(source: string, type: SubjectType, title: string): Promise<CreatedSubject>;
  setAttributes(subject: string, attrs: Record<string, string>): Promise<void>; // "" removes a key
  setStatus(subject: string, status: string): Promise<void>;                    // state_change
  archive(subject: string): Promise<void>;
  restore(subject: string): Promise<void>;
  tag(item: string, opts: { add?: string[]; remove?: string[] }): Promise<void>;
  link(from: string, relation: Relation, to: string): Promise<void>;            // subject→subject
  relate(eventId: string, subject: string, as?: EventRelation): Promise<void>;  // event→subject
  flag(item: string): Promise<void>;                                            // (re-)enter inbox
  dismiss(item: string): Promise<void>;                                         // attention-only clear
  drop(item: string): Promise<void>;                                            // subject → terminal status
  adhere(habit: string, state: "followed" | "partial" | "missed",
         opts?: { on?: string; note?: string }): Promise<void>;
  /** Set structured recurrence on a Habit (GEN-3). */
  recur(habit: string, spec: RecurrenceSpec): Promise<void>;
  involve(subject: string, person: string, role: PersonRole, remove?: boolean): Promise<void>;
  appendJournal(subject: string, text: string): Promise<void>;
  capture(text: string): Promise<void>;                                         // note event → inbox
  /** Mock-only until Review subjects (D3) exist. */
  saveReview(id: string, body: ReviewBody): Promise<void>;
  completeReview(id: string): Promise<void>;
}
