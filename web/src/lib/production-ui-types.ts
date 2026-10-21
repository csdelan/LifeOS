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

/** The subject types (11 core + Area, Habit, Appointment, Reward). */
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
  | "Appointment"
  | "Reward";      // a reward catalog entry (REWARD-3); not offered by global New

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

/**
 * The short id a person sees and types (`bsk status a1b2c3 …`): the trailing token
 * of the URN. The URN's slug is the item's name at birth and never follows a rename,
 * so the UI shows only this permanent part. The kernel mints hex; the mock mints
 * base-36, so any 4–12 character token that is hex or carries a digit counts. A URN
 * without one falls back to its whole tail.
 */
export function shortIdOf(urn: string): string {
  const tail = urn.slice(urn.lastIndexOf(":") + 1);
  const token = tail.slice(tail.lastIndexOf("-") + 1);
  const isShortId = /^[0-9a-f]{4,12}$/i.test(token) || /^(?=.*\d)[0-9a-z]{4,12}$/i.test(token);
  return isShortId ? token : tail;
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
  // Established graduates a Habit: it leaves daily tracking (REWARD-2).
  Habit:       ["Building", "Established", "Abandoned"],
  // Value (Identity Statement), Area, Person: no status workflow.
};

/** Statuses that drop an item from default views (terminal). */
export const TERMINAL_STATUSES = new Set([
  "done", "completed", "resolved", "closed", "cancelled",
  "abandoned", "dropped", "archived", "superseded",
  "fulfilled", "missed", "promoted", "rejected", "established",
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
  // The price ({currency: n}) is an object the rewards API writes, not a string attribute.
  Reward: ["kind", "target_usd", "description", "notes"],
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
  /** What completing it pays (a Habit: a followed day); null for a type that doesn't earn. */
  bounty?: Amounts | null;
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
  /** What each trigger pays (REWARD-2); null for a type that doesn't earn. */
  bounty?: BountyLine[] | null;
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
  /** Present when the capture carries a managed binary artifact (CAP-2 / CAP-4). */
  attachment?: ArtifactRecord | null;
  /** Empty/failed transcription — audio was retained for later processing (CAP-2). */
  needsTranscription?: boolean;
  /** The item's own tags: on the event for a raw capture, on the subject otherwise. */
  tags?: string[];
}

/**
 * How an item left the Inbox: its newest triage state, or `status` when a terminal
 * Status cleared a still-flagged subject (Drop). `filed` / `dropped` are retired
 * states that older items may still carry.
 */
export type InboxResolution = "dismissed" | "promoted" | "status" | "filed" | "dropped";

/** An item that was triaged and has left the Inbox (`bsk.v_inbox_resolved`). */
export interface ResolvedInboxItem extends InboxItem {
  resolution: InboxResolution;
  resolvedAt: string;
  subjectStatus?: string | null;
  /** The subject must be re-opened before Back to Inbox can surface it. */
  subjectStatusTerminal?: boolean;
}

/**
 * Metadata for a managed binary artifact (`bsk.v_artifact` / `artifact_blob`).
 * Bytes are not inlined — fetch them via `LifeOsReads.artifactBytesUrl`.
 */
export interface ArtifactRecord {
  id: string;
  eventId: string;
  /** The referencing event's kind (`note` for documents, `voice` for audio). */
  eventKind: string;
  filename?: string | null;
  contentType: string;
  byteSize: number;
  sha256?: string | null;
  hasBytes: boolean;
  /**
   * Mock: an object URL for the retained blob.
   * Live: leave unset and resolve through `artifactBytesUrl`.
   */
  bytesUrl?: string | null;
  /** App-side duration for voice captures; the kernel does not store this. */
  durationSeconds?: number | null;
}

/** Note / Idea / Problem selector shared by quick capture and voice (CAP-1 / D4). */
export const CAPTURE_KINDS = ["Note", "Idea", "Problem"] as const;
export type CaptureKind = (typeof CAPTURE_KINDS)[number];

export interface CaptureDocumentRequest {
  file: File;
  description: string;
  type: CaptureKind;
}

export interface CaptureVoiceRequest {
  audioBlob: Blob;
  transcript: string;
  type: CaptureKind;
  keepAudio: boolean;
  /** Elapsed recording time, used for the Inbox duration chip. */
  durationSeconds?: number;
}

/** Outcome of a document or voice capture (mirrors kernel `AttachmentResult` + D4). */
export interface BinaryCaptureResult {
  eventId: string;
  artifactId?: string | null;
  kind: string;
  filename?: string | null;
  contentType?: string | null;
  byteSize?: number;
  sha256?: string | null;
  /** Present when type is Idea or Problem — those become subjects immediately (D4). */
  subject?: CreatedSubject | null;
  needsTranscription?: boolean;
}

/** A Task timer's folded state (TASKS-2). A task never timed has no timer row. */
export type TimerState = "running" | "paused" | "stopped";

/**
 * What the timer widget can do. Complete is Stop followed by moving the Task to
 * Completed — the only place the timer touches Status.
 */
export type TimerAction = "start" | "pause" | "resume" | "stop" | "complete";

/**
 * A Task's optional timer (`bsk.v_task_timer`, TASKS-2). The seconds count closed
 * segments only: while `state` is running, add now − `runningSince` (see
 * `lib/timer.ts`). A session runs from start to stop and may span pauses.
 */
export interface TaskTimer {
  subjectId: string;
  subjectUrn: string;
  subjectTitle: string;
  state: TimerState;
  runningSince?: string | null;   // ISO timestamp, while running
  totalSeconds: number;           // every session
  sessionSeconds: number;         // the newest session
  sessionStartedAt?: string | null;
  sessions: number;
  lastChangedAt: string;
}

/** Outcome of a timer action. One timer runs at a time, so a start may pause another. */
export interface TimerActionResult {
  timer: TaskTimer;
  paused: CreatedSubject[];
  completed: boolean;
  /** What Complete earned (REWARD-4); empty for the other actions. */
  awards?: Award[];
  /** Each life domain Complete moved a level or rank (xp-domains plan §3.8). */
  progress?: DomainChange[];
}

// ---------------------------------------------------------------------------
// Rewards (REWARD-1 / REWARD-3 / D11): the wallet, the ledger and the catalog.
// Amounts are keyed by currency (`coins`, `freedom`, …; see `lib/rewards.ts`).
// ---------------------------------------------------------------------------

/** Currency key → whole amount. Ledger rows are signed; prices and spends are positive. */
export type Amounts = Record<string, number>;

/** One currency's line in the wallet (`bsk.v_wallet`); earned / reversed / spent are positive totals. */
export interface WalletLine {
  currency: string;
  label: string;
  spendable: boolean;
  earned: number;
  reversed: number;
  spent: number;
  balance: number;
}

/** The wallet: every currency in display order, and what a Coin is worth in dollars. */
export interface Wallet {
  /** When the ledger opened; null before anything has paid. */
  openedAt?: string | null;
  coinValueUsd: number;
  balances: WalletLine[];
}

export type LedgerEntryKind = "open" | "earn" | "reversal" | "redeem";

/**
 * One local day of the rewards history (REWARD-5, `GET /api/ledger/daily`): per currency,
 * what that day's entries earned, gave back and spent (positive totals), and `net`, the
 * signed sum of all of them. Currencies at zero are left out; a day without entries has no
 * row. Weeks and months are roll-ups of these days (`lib/rewards-history.ts`).
 */
export interface LedgerDay {
  /** `YYYY-MM-DD` in the viewer's time zone. */
  date: string;
  earned: Amounts;
  reversed: Amounts;
  spent: Amounts;
  net: Amounts;
}

/** One ledger entry (`bsk.v_ledger`), newest first: what it was and what it concerns. */
export interface LedgerRow {
  id: string;
  occurredAt: string;
  entry: LedgerEntryKind;
  provenance: string;
  /** Signed: earns are positive, reversals and redemptions negative. */
  amounts: Amounts;
  trigger?: string | null;
  awardKey?: string | null;
  /** The item that earned it, or the Reward it bought. */
  subject?: CreatedSubject | null;
  note?: string | null;
  /** Set whenever Coins moved: the dollars, at `coinRate`. */
  dollars?: number | null;
  coinRate?: number | null;
}

/** `instant` rewards have a price; `savings` goals take Coins toward a dollar target. */
export type RewardKind = "instant" | "savings";

/** One catalog Reward (`bsk.v_reward`), with what has been redeemed against it. */
export interface RewardRow {
  id: string;
  urn: string;
  name: string;
  kind: RewardKind;
  /** An instant reward's price; empty for a savings goal or an unpriced reward. */
  price: Amounts;
  targetUsd?: number | null;
  redemptions: number;
  coinsRedeemed: number;
  dollarsRedeemed: number;
  lastRedeemedAt?: string | null;
  /** A savings goal with a target: progress, and what is left. */
  percentOfTarget?: number | null;
  remainingUsd?: number | null;
  description?: string | null;
  archived: boolean;
}

/**
 * An earn or reversal a write produced (REWARD-4's feedback). A rank-up (`trigger` "rank",
 * REWARD-7) belongs to no item: `subject` is null and `note` names it ("Health Apprentice").
 * An earn whose life-domain XP was paid at a multiplier above 1.0 (xp-domains plan §3.6) adds
 * `base` (those currencies before it) and `multipliers`, e.g. `{ health_xp: 1.3 }`.
 */
export interface Award {
  id: string;
  entry: "earn" | "reversal";
  trigger: string;
  awardKey: string;
  subject: CreatedSubject | null;
  note?: string | null;
  amounts: Amounts;
  base?: Amounts | null;
  multipliers?: Record<string, number> | null;
}

// ---------------------------------------------------------------------------
// Life domains (REWARD-6 / REWARD-7, xp-domains plan §3.11): `GET /api/domains` and
// `GET /api/domains/{key}/days`. Dates are home-zone days (`yyyy-MM-dd`); ranks are indexes
// into the domain's ladder (0 is Novice); shares and bars run 0–1.
// ---------------------------------------------------------------------------

export interface DomainsRead {
  today: string;
  domains: DomainRow[];
}

/**
 * One life domain. Only a configured one (Health in the pilot) has `lifetime`, `current`
 * and `ranks`; `nextRank` is null at the last rank.
 */
export interface DomainRow {
  key: string;
  label: string;
  /** "Health". */
  name: string;
  totalXp: number;
  configured: boolean;
  lifetime: DomainLifetime | null;
  nextRank: DomainNextRank | null;
  current: DomainCurrent | null;
  ranks: DomainRankRow[] | null;
}

/** Lifetime advancement, which a bad month never lowers. */
export interface DomainLifetime {
  start: string | null;
  level: number;
  /** The level the XP alone reaches, before the rank's cap. */
  rawLevel: number;
  /** The highest level the rank allows (9 for Novice). */
  topLevel: number;
  capped: boolean;
  rank: number;
  rankTitle: string;
  qualifiedOn: string | null;
  /** What the current level costs in all. */
  levelXp: number;
  /** The XP into the level; the whole of it while capped. */
  xpIntoLevel: number;
  xpToNext: number;
  bankedXp: number;
}

/** The next rank's gate as it reads today; today counts once it has succeeded. */
export interface DomainNextRank {
  rank: number;
  title: string;
  level: number;
  bar: number;
  windowDays: number;
  consistency: number;
  successfulDays: number;
  requiredDays: number;
  daysNeeded: number;
  daysOld: number;
  levelReached: boolean;
  oldEnough: boolean;
  barMet: boolean;
}

/** Current performance, which does drop: today against the rule, and the last 30 days. */
export interface DomainCurrent {
  todayBase: number;
  todaySucceeded: boolean;
  /** "at least 70 base XP". */
  rule: string;
  target: number | null;
  consistency: number;
  successfulDays: number;
  windowDays: number;
  multiplier: number;
  nextTier: DomainTier | null;
}

export interface DomainTier {
  minShare: number;
  multiplier: number;
  daysNeeded: number;
}

export interface DomainRankRow {
  rank: number;
  title: string;
  level: number;
  topLevel: number;
  bar: number;
  windowDays: number;
  gems: number;
  qualifiedOn: string | null;
}

/** One domain's last days, oldest first, ending today. */
export interface DomainDaysRead {
  key: string;
  label: string;
  name: string;
  configured: boolean;
  today: string;
  /** The success rule's number (70 for Health); null without rules. */
  target: number | null;
  days: DomainDayRow[];
}

/** One day: base XP (before any multiplier), paid XP, and whether it succeeded (null without rules). */
export interface DomainDayRow {
  day: string;
  base: number;
  paid: number;
  succeeded: boolean | null;
}

/**
 * `GET /api/domains/report?days=N` (xp-domains plan §3.11, §3.13): every life domain over its
 * last `days` days, as of `today` (home zone) — the Character page's Reports.
 */
export interface DomainReportRead {
  today: string;
  days: number;
  domains: DomainReportRow[];
}

/**
 * One life domain over a report's period: the days it covers (`from`–`to`; a configured
 * domain's end yesterday until today succeeds) and the XP credited to them. A configured
 * domain adds the days counted (on or after its start), the successful ones, consistency (0–1)
 * and base XP a day over the days counted, and the level and rank (indexes, 0 is Novice) the
 * period started from against now. Those are null for a domain without rules.
 */
export interface DomainReportRow {
  key: string;
  label: string;
  name: string;
  configured: boolean;
  from: string;
  to: string;
  base: number;
  paid: number;
  daysCounted: number | null;
  successfulDays: number | null;
  consistency: number | null;
  averageBase: number | null;
  levelFrom: number | null;
  levelTo: number | null;
  rankFrom: number | null;
  rankTo: number | null;
  /** The rank held now, e.g. "Apprentice". */
  rankTitle: string | null;
  ranksReached: DomainRankReached[] | null;
}

/** A rank a domain entered in a report's period, and the day it qualified. */
export interface DomainRankReached {
  rank: number;
  title: string;
  qualifiedOn: string;
}

/**
 * A configured life domain whose level or rank a write changed (xp-domains plan §3.8):
 * `domain` is its currency (`health_xp`), ranks are indexes into its ladder (0 is Novice),
 * and `rankTitle` names the rank after.
 */
export interface DomainChange {
  domain: string;
  levelFrom: number;
  levelTo: number;
  rankFrom: number;
  rankTo: number;
  rankTitle: string;
}

/** What an earning write answers: the entries it wrote, and each domain it moved. */
export interface Earned {
  awards: Award[];
  progress: DomainChange[];
}

/**
 * What one trigger of an item pays (REWARD-2): its own price when `isCustom`, otherwise its
 * type default, which `default` always carries.
 */
export interface BountyLine {
  trigger: string;
  amounts: Amounts;
  isCustom: boolean;
  default: Amounts;
  /** `timed` only: the minutes on the timer each payment of `amounts` takes (default 1). */
  everyMinutes?: number | null;
}

/** Each trigger an earning type pays on, at its default — what a create form pre-fills. */
export interface BountyTypeDefaults {
  type: SubjectType;
  lines: BountyLine[];
}

/**
 * Price an item (`bsk bounty`). Send one of `amounts` (set these currencies on what the
 * trigger pays now; name all six to set the whole price), `nothing` or `clear` (back on the
 * default; with no trigger, every trigger). `trigger` may be left out when the type has one,
 * or for `completed`. `everyMinutes` (`timed` only, from 1) sets the minutes on the timer
 * each payment takes; it may come alone, with amounts or with nothing, never with clear.
 */
export interface BountyRequest {
  subject: string;
  trigger?: string;
  amounts?: Amounts;
  nothing?: boolean;
  clear?: boolean;
  everyMinutes?: number;
}

export interface BountyResult {
  subject: CreatedSubject;
  lines: BountyLine[];
}

/**
 * A Building habit's progress toward its establish target (`v_habit_establishment`):
 * adherence over the window, partial counting half. `targetMet` is the "Ready to
 * establish" suggestion; confirming is a status change to Established.
 */
export interface HabitEstablishment {
  minAdherence: number;
  windowDays: number;
  windowStart: string;
  followed: number;
  partial: number;
  missed: number;
  concluded: number;
  /** 0..1, or null before any occurrence in the window concluded. */
  adherence?: number | null;
  ageDays: number;
  targetMet: boolean;
}

/**
 * Set a Habit's establish target (`bsk habit target`): send `minAdherence` (a fraction above 0
 * and up to 1; a number above 1 and up to 100 is read as a percentage), `windowDays` (whole
 * days, 1–3650) or both — a part left out keeps what the habit has — or `clear` (back on the
 * default, 80% over 60 days).
 */
export interface HabitTargetRequest {
  habit: string;
  minAdherence?: number;
  windowDays?: number;
  clear?: boolean;
}

/** The habit's establish target after the write, and whether each part is its own. */
export interface HabitTargetResult {
  habit: CreatedSubject;
  minAdherence: number;
  windowDays: number;
  adherenceIsCustom: boolean;
  windowIsCustom: boolean;
}

/**
 * A redemption (`bsk redeem`). Omit `amounts` to pay an instant reward's price; a savings
 * goal needs `{ coins: n }`; with no `reward`, amounts and a `note` are both required.
 */
export interface RedeemRequest {
  reward?: string;
  amounts?: Amounts;
  note?: string;
}

/** What a redemption spent (positive amounts), the Reward's progress after it, and the wallet. */
export interface Redemption {
  id: string;
  amounts: Amounts;
  note?: string | null;
  dollars?: number | null;
  coinRate?: number | null;
  reward?: RewardRow | null;
  wallet: Wallet;
}

/** A new catalog Reward (`bsk rewards add`). */
export interface NewRewardRequest {
  title: string;
  kind?: RewardKind;
  /** Instant rewards only. */
  price?: Amounts;
  /** Savings goals only. */
  targetUsd?: number;
  description?: string;
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
  /** Building (null reads as Building), Established (graduated) or Abandoned. */
  status?: string | null;
  /** What a followed day pays. */
  bounty?: Amounts | null;
  /** While Building and unarchived: progress toward the establish target. */
  establishment?: HabitEstablishment | null;
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

/** Result of `rename`. The URN never changes, so only titles come back. */
export interface RenamedSubject {
  id: string;
  title: string;
  previousTitle: string;
  changed: boolean;
  /** Appointment series: upcoming occurrences that still had the old title and took the new one. */
  occurrencesRenamed: number;
}

/** Toast text for a rename; mentions series occurrences that followed it. */
export function renamedMessage(renamed: RenamedSubject): string {
  const n = renamed.occurrencesRenamed;
  return n > 0
    ? `Renamed to ${renamed.title} (and ${n} upcoming occurrence${n === 1 ? "" : "s"})`
    : `Renamed to ${renamed.title}`;
}

/** Result of `renameTag`. `merged` counts items that already carried the new tag. */
export interface RenamedTag {
  from: string;
  to: string;
  renamed: number;
  merged: number;
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
  /** Change the title (urn unchanged). A series rename carries to its upcoming occurrences. */
  rename(subject: string, title: string): Promise<RenamedSubject>;
  /** A state_change; answers what it earned or gave back (REWARD-4) and any level or rank it moved. */
  setStatus(subject: string, status: string): Promise<Earned>;
  archive(subject: string): Promise<void>;
  restore(subject: string): Promise<void>;
  tag(item: string, opts: { add?: string[]; remove?: string[] }): Promise<void>;
  /** Rename a tag on every item; renaming onto an existing tag merges the two. */
  renameTag(from: string, to: string): Promise<RenamedTag>;
  link(from: string, relation: Relation, to: string): Promise<void>;            // subject→subject
  relate(eventId: string, subject: string, as?: EventRelation): Promise<void>;  // event→subject
  flag(item: string): Promise<void>;                                            // (re-)enter inbox
  dismiss(item: string): Promise<void>;                                         // attention-only clear
  drop(item: string): Promise<void>;                                            // subject → terminal status
  /** Record a habit day; answers what it earned or gave back (REWARD-4) and any level or rank it moved. */
  adhere(habit: string, state: "followed" | "partial" | "missed",
         opts?: { on?: string; note?: string }): Promise<Earned>;
  /**
   * Pay any rank-up time alone has reached (`POST /api/domains/settle`, xp-domains plan
   * §3.7), which the Character page does when it opens.
   */
  settleDomains(): Promise<Earned>;
  /** Set structured recurrence on a Habit (GEN-3). */
  recur(habit: string, spec: RecurrenceSpec): Promise<void>;
  involve(subject: string, person: string, role: PersonRole, remove?: boolean): Promise<void>;
  /** Drive a Task's timer (TASKS-2). Starting or resuming pauses any other running timer. */
  timer(task: string, action: TimerAction): Promise<TimerActionResult>;
  appendJournal(subject: string, text: string): Promise<void>;
  capture(text: string): Promise<void>;                                         // note event → inbox
  /**
   * Document / attachment capture (CAP-4). Note → `note` event + binary artifact;
   * Idea / Problem → durable subject (D4) with the file related onto it.
   * // TODO(api): multipart POST to the kernel attach write path.
   */
  captureDocument(req: CaptureDocumentRequest): Promise<BinaryCaptureResult>;
  /**
   * Voice capture (CAP-2). Note → `voice` event + transcript (+ audio if kept);
   * Idea / Problem → durable subject (D4). Empty transcript forces audio retention.
   * // TODO(api): multipart POST to the kernel voice write path.
   */
  captureVoice(req: CaptureVoiceRequest): Promise<BinaryCaptureResult>;
  /** Mock-only until Review subjects (D3) exist. */
  saveReview(id: string, body: ReviewBody): Promise<void>;
  completeReview(id: string): Promise<void>;
  /**
   * Spend currencies (REWARD-3). Refused — nothing written — on an overdraft, a tracked
   * currency, or a custom redemption without a note.
   */
  redeem(req: RedeemRequest): Promise<Redemption>;
  /**
   * Catalog writes. Rename, describe (`description`) and archive a Reward through the
   * ordinary `rename` / `setAttributes` / `archive`.
   */
  addReward(req: NewRewardRequest): Promise<RewardRow>;
  /** Replace an instant reward's price. */
  priceReward(reward: string, price: Amounts): Promise<RewardRow>;
  /** Set a savings goal's target in dollars; null removes it. */
  targetReward(reward: string, targetUsd: number | null): Promise<RewardRow>;
  /** Price a Task / Project / Goal / Habit (REWARD-2). Refused for a type that doesn't earn. */
  setBounty(req: BountyRequest): Promise<BountyResult>;
  /** Set or clear a Habit's establish target (REWARD-2). Refused for any other type. */
  setHabitTarget(req: HabitTargetRequest): Promise<HabitTargetResult>;
}
