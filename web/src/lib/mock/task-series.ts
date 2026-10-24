import { addDays, localDateFromTimestamp, nowIso, slugify } from "@/lib/dates";
import { isTerminal, SERIES_STATUSES } from "@/lib/production-ui-types";
import type {
  RecurrenceSpec,
  SubjectListItem,
  TaskRepeat,
  TaskRepetitionRow,
  TaskSeriesCounts,
  TaskSeriesRow,
} from "@/lib/production-ui-types";
import { parseRecurrence } from "@/lib/recurrence";
import { afterCompletion, datesFor, nextOccurrence, repeatKind, weekOf, type RepeatKind } from "@/lib/task-series";
import type { MockState } from "@/lib/mock/seed";

/**
 * The mock's `TaskSeriesService` (TASKS-3, D16, recurring-tasks plan §2): a series is a Task
 * whose attributes hold a pattern and no `series`; each repetition is an ordinary Task whose
 * attributes hold `series` (the series' urn), `occurrence`, `due` and, once each week,
 * `scheduled`. These work on the state they're handed, so the seed and the store share them.
 */

/** What a repetition copies from its series when it's made, and what a series edit follows to. */
export const COPIED_KEYS = ["description", "area", "priority", "estimated_duration"] as const;

// Most repetitions one catch-up makes for one series: a daily chore after a year away.
const MOST_AT_ONCE = 400;

function bagOf(state: MockState, id: string): Record<string, unknown> {
  const raw = state.details[id]?.attributes;
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? { ...(parsed as Record<string, unknown>) } : {};
  } catch {
    return {};
  }
}

function text(bag: Record<string, unknown>, key: string): string | undefined {
  const v = bag[key];
  return typeof v === "string" && v ? v : undefined;
}

function writeBag(state: MockState, id: string, patch: Record<string, unknown>, remove: string[] = []) {
  const detail = state.details[id];
  if (!detail) return;
  const bag = { ...bagOf(state, id), ...patch };
  for (const key of remove) delete bag[key];
  state.details[id] = { ...detail, attributes: JSON.stringify(bag) };
}

function byId(state: MockState, id: string): SubjectListItem | undefined {
  return state.subjects.find((s) => s.id === id);
}

/** The Task's pattern, when it has one. */
export function patternOf(state: MockState, id: string): RecurrenceSpec | null {
  const recurrence = bagOf(state, id).recurrence;
  return recurrence && typeof recurrence === "object" ? parseRecurrence(JSON.stringify(recurrence)) : null;
}

/** True for a repeating Task's series (`bsk.is_task_series`). */
export function isSeries(state: MockState, id: string): boolean {
  const item = byId(state, id);
  if (item?.type !== "Task") return false;
  const bag = bagOf(state, id);
  return !!bag.recurrence && typeof bag.recurrence === "object" && !text(bag, "series");
}

/** The series a repetition belongs to. */
export function seriesOf(state: MockState, id: string): SubjectListItem | undefined {
  const urn = text(bagOf(state, id), "series");
  return urn ? state.subjects.find((s) => s.urn === urn && s.type === "Task") : undefined;
}

/** Cancelled or archived: a series that makes no more. */
export function isStopped(series: SubjectListItem): boolean {
  return series.archived || isTerminal(series.status);
}

/** The day a Task closed: the day of its latest status change, when that left it terminal. */
function closedOn(state: MockState, item: SubjectListItem): string | null {
  if (!isTerminal(item.status)) return null;
  const latest = state.history[item.id]?.[0];
  return latest ? localDateFromTimestamp(latest.occurredAt) : null;
}

/** Every repetition of a series, latest date first (`bsk.v_task_repetition`). */
export function repetitionRows(state: MockState, seriesUrn: string): TaskRepetitionRow[] {
  return state.subjects
    .filter((s) => s.type === "Task" && text(bagOf(state, s.id), "series") === seriesUrn)
    .map((s) => {
      const bag = bagOf(state, s.id);
      return {
        id: s.id,
        urn: s.urn,
        title: s.title,
        occurrence: text(bag, "occurrence") ?? null,
        due: s.due ?? null,
        scheduled: s.scheduled ?? null,
        status: s.status ?? null,
        closed: isTerminal(s.status),
        closedOn: closedOn(state, s),
        archived: s.archived,
      };
    })
    .sort((a, b) => (b.occurrence ?? "").localeCompare(a.occurrence ?? ""));
}

/** A series' repetitions counted as `bsk.v_task_series` counts them. */
export function countsOf(state: MockState, seriesUrn: string): TaskSeriesCounts {
  const rows = repetitionRows(state, seriesUrn);
  const status = (r: TaskRepetitionRow) => (r.status ?? "").toLowerCase();
  const open = rows.filter((r) => !r.closed && !r.archived);
  const completed = rows.filter((r) => status(r) === "completed");
  const max = (xs: (string | null | undefined)[]) =>
    xs.filter((x): x is string => !!x).sort().at(-1) ?? null;
  const min = (xs: (string | null | undefined)[]) =>
    xs.filter((x): x is string => !!x).sort()[0] ?? null;
  return {
    openCount: open.length,
    completedCount: completed.length,
    missedCount: rows.filter((r) => status(r) === "missed").length,
    cancelledCount: rows.filter((r) => status(r) === "cancelled").length,
    lastCompletedOn: max(completed.map((r) => r.closedOn)),
    nextDue: min(open.map((r) => r.due)),
    latestOccurrence: max(rows.map((r) => r.occurrence)),
  };
}

/** One series as `GET /api/tasks/series` reads it; `bounty` is left for the caller to price. */
export function seriesRow(state: MockState, series: SubjectListItem): TaskSeriesRow {
  const bag = bagOf(state, series.id);
  return {
    id: series.id,
    urn: series.urn,
    title: series.title,
    recurrence: JSON.stringify(bag.recurrence ?? null),
    start: text(bag, "start") ?? null,
    status: series.status ?? null,
    archived: series.archived,
    stopped: isStopped(series),
    area: series.area ?? null,
    areaName: series.areaName ?? null,
    ...countsOf(state, series.urn),
  };
}

/** The repeating Task a Task is or belongs to (`SubjectDetail.repeating`); null for any other. */
export function repeatOf(state: MockState, id: string): TaskRepeat | null {
  const item = byId(state, id);
  if (item?.type !== "Task") return null;
  const series = isSeries(state, id) ? item : seriesOf(state, id);
  if (!series) return null;
  const row = seriesRow(state, series);
  return {
    isSeries: series.id === id,
    seriesId: series.id,
    seriesUrn: series.urn,
    seriesTitle: series.title,
    recurrence: row.recurrence,
    start: row.start,
    seriesStatus: series.status ?? null,
    stopped: row.stopped,
    occurrence: series.id === id ? null : (text(bagOf(state, id), "occurrence") ?? null),
    openCount: row.openCount,
    completedCount: row.completedCount,
    missedCount: row.missedCount,
    cancelledCount: row.cancelledCount,
    lastCompletedOn: row.lastCompletedOn,
    nextDue: row.nextDue,
    latestOccurrence: row.latestOccurrence,
  };
}

/** Refuses a status a series can't take: it repeats while Not started, Cancelled stops it. */
export function checkSeriesStatus(state: MockState, id: string, status: string) {
  if (!isSeries(state, id)) return;
  const wanted = status.trim().toLowerCase();
  if (!SERIES_STATUSES.some((s) => s.toLowerCase() === wanted)) {
    throw new Error(
      `'${byId(state, id)?.title}' is a repeating task: it's Not started while it repeats, or Cancelled to stop it. Its repetitions are the ones you complete or mark Missed.`,
    );
  }
}

/** Sets a series' start: the first date it repeats from. */
export function setStart(state: MockState, seriesId: string, start: string) {
  writeBag(state, seriesId, { start });
}

/** Refuses a pattern change the kernel refuses (`GuardPatternAsync`). */
export function guardPattern(state: MockState, id: string, clearing: boolean) {
  const item = byId(state, id);
  if (item?.type !== "Task") return;
  if (seriesOf(state, id)) {
    throw new Error(
      `'${item.title}' is one repetition of a repeating task. Change the repeat on the repeating task itself.`,
    );
  }
  if (clearing) {
    if (isSeries(state, id) && countsOf(state, item.urn).latestOccurrence) {
      throw new Error(`'${item.title}' has repetitions. To stop it repeating, set its status to Cancelled.`);
    }
    return;
  }
  if (isTerminal(item.status) && (item.status ?? "").toLowerCase() !== "cancelled") {
    throw new Error(`'${item.title}' is ${item.status}. A finished Task doesn't start repeating: make a new one.`);
  }
}

/**
 * After a Task's pattern (or start) is set (`AfterPatternAsync`): a new series gets its start
 * (the date chosen, else its due date, else today) and its first repetition; an existing one
 * re-dates its open repetition whose date hasn't passed. Returns the repetitions made.
 */
export function afterPattern(state: MockState, id: string, today: string, start?: string): SubjectListItem[] {
  if (!isSeries(state, id)) return [];
  const item = byId(state, id)!;
  const bag = bagOf(state, id);
  if (start && start !== text(bag, "start")) setStart(state, id, start);
  else if (!text(bag, "start")) setStart(state, id, item.due ?? today);
  const latest = repetitionRows(state, item.urn)[0];
  if (latest && !latest.closed && !latest.archived && (latest.due ?? latest.occurrence ?? "") >= today) {
    redate(state, item, latest, today);
  }
  return ensureSeries(state, id, today);
}

/** Moves the open repetition to where the series' pattern now puts it (`RedateAsync`). */
function redate(state: MockState, series: SubjectListItem, open: TaskRepetitionRow, today: string) {
  const spec = patternOf(state, series.id);
  const kind = repeatKind(spec);
  if (!spec || !kind) return;
  const start = text(bagOf(state, series.id), "start") ?? today;
  const from = start > today ? start : today;
  let when: string | null;
  if (kind === "calendar") when = nextOccurrence(spec, start, from);
  else if (kind === "each-week") when = weekOf(from);
  else {
    const lastClosed = repetitionRows(state, series.urn).find((r) => r.id !== open.id && r.closed && r.closedOn);
    const counted = lastClosed?.closedOn ? afterCompletion(spec, lastClosed.closedOn) : start;
    when = counted > start ? counted : start;
  }
  if (!when) return;
  const { due, scheduled } = datesFor(kind, when);
  if (when === open.occurrence && due === open.due && scheduled === (open.scheduled ?? null)) return;
  // Another repetition already standing for that date keeps it; this one stays as it was.
  if (repetitionRows(state, series.urn).some((r) => r.id !== open.id && r.occurrence === when)) return;
  writeBag(state, open.id, { occurrence: when, due, ...(scheduled ? { scheduled } : {}) }, scheduled ? [] : ["scheduled"]);
  state.subjects = state.subjects.map((s) => (s.id === open.id ? { ...s, due, scheduled } : s));
  const detail = state.details[open.id];
  if (detail) state.details[open.id] = { ...detail, due };
}

/**
 * Makes the repetitions a series is owed by `today` (`EnsureAsync` / `PlanAsync`): on calendar
 * dates and once each week, the next when the latest closes or its date passes, so every date
 * gets its own; after the last completion, the next when the latest closes. Nothing here
 * changes a status.
 */
export function ensureSeries(state: MockState, seriesId: string, today: string): SubjectListItem[] {
  const series = byId(state, seriesId);
  if (!series || !isSeries(state, seriesId) || isStopped(series)) return [];
  const spec = patternOf(state, seriesId);
  const kind = repeatKind(spec);
  if (!spec || !kind) return [];
  let start = text(bagOf(state, seriesId), "start");
  if (!start) {
    start = series.due ?? today;
    setStart(state, seriesId, start);
  }

  const plan: string[] = [];
  const latest = repetitionRows(state, series.urn)[0];
  type Cursor = { occurrence: string; due: string; closed: boolean; closedOn: string | null };
  const open = (occurrence: string): Cursor => ({
    occurrence,
    due: datesFor(kind, occurrence).due,
    closed: false,
    closedOn: null,
  });
  // An archived latest counts as closed, so hiding one doesn't stall the series.
  let cursor: Cursor | null = latest?.occurrence
    ? {
        occurrence: latest.occurrence,
        due: latest.due ?? datesFor(kind, latest.occurrence).due,
        closed: latest.closed || latest.archived,
        closedOn: latest.closedOn ?? (latest.archived ? today : null),
      }
    : null;

  if (!cursor) {
    const first = kind === "each-week" ? weekOf(start) : kind === "after-completion" ? start : nextOccurrence(spec, start, start);
    if (!first) return [];
    plan.push(first);
    cursor = open(first);
  }

  while (plan.length < MOST_AT_ONCE) {
    let next: string | null;
    const dayAfter = addDays(cursor.occurrence, 1);
    const notBefore = start > dayAfter ? start : dayAfter;
    if (kind === "after-completion") {
      if (!cursor.closed) break;
      const counted = afterCompletion(spec, cursor.closedOn ?? today);
      next = counted > notBefore ? counted : notBefore;
    } else {
      if (!cursor.closed && cursor.due >= today) break;
      if (kind === "each-week") {
        const week = addDays(cursor.occurrence, 7);
        const first = weekOf(start);
        next = week > first ? week : first;
      } else {
        next = nextOccurrence(spec, start, notBefore);
      }
    }
    if (!next) break;
    plan.push(next);
    cursor = open(next);
  }
  return plan.map((occurrence) => makeRepetition(state, series, kind, occurrence));
}

/** Settles every repeating Task (`SettleAsync`): how many it looked at, and what it made. */
export function settleAll(state: MockState, today: string): { series: number; made: SubjectListItem[] } {
  const repeating = state.subjects.filter((s) => isSeries(state, s.id) && !isStopped(s));
  return { series: repeating.length, made: repeating.flatMap((s) => ensureSeries(state, s.id, today)) };
}

/** The open repetitions of a series: the ones a series edit follows to. */
export function openRepetitionIds(state: MockState, seriesUrn: string): string[] {
  return repetitionRows(state, seriesUrn)
    .filter((r) => !r.closed && !r.archived)
    .map((r) => r.id);
}

/** A series' value for each copied key, before an edit (`BeforeEditAsync`). */
export function copiedValues(state: MockState, seriesId: string): Record<string, string | undefined> {
  const bag = bagOf(state, seriesId);
  return Object.fromEntries(COPIED_KEYS.map((k) => [k, text(bag, k)]));
}

/** A repetition's own value for a key. */
export function valueOf(state: MockState, id: string, key: string): string | undefined {
  return text(bagOf(state, id), key);
}

function makeRepetition(state: MockState, series: SubjectListItem, kind: RepeatKind, occurrence: string): SubjectListItem {
  const id = crypto.randomUUID();
  const urn = `urn:bsk:task:${slugify(series.title)}-${Math.random().toString(36).slice(2, 8)}`;
  const createdAt = nowIso();
  const { due, scheduled } = datesFor(kind, occurrence);
  const seriesBag = bagOf(state, series.id);
  const attrs: Record<string, string> = { series: series.urn, occurrence, due };
  if (scheduled) attrs.scheduled = scheduled;
  for (const key of COPIED_KEYS) {
    const value = text(seriesBag, key);
    if (value) attrs[key] = value;
  }
  const status = "Not started";
  const item: SubjectListItem = {
    id,
    urn,
    type: "Task",
    title: series.title,
    status,
    archived: false,
    createdAt,
    due,
    scheduled,
    area: series.area,
    areaName: series.areaName,
    tags: series.tags,
  };
  state.subjects = [item, ...state.subjects];
  state.details[id] = {
    id,
    urn,
    type: "Task",
    title: series.title,
    status,
    createdAt,
    archived: false,
    area: series.area,
    areaName: series.areaName,
    due,
    scope: attrs.description,
    attributes: JSON.stringify(attrs),
  };
  state.history[id] = [{ id: crypto.randomUUID(), status, occurredAt: createdAt }];
  state.tagsByItem[id] = [...(state.tagsByItem[series.id] ?? [])];
  state.journals[id] = [];
  // It serves what the series serves, so it sits under the same Goal or Project.
  const serves = state.edges.filter((e) => e.fromId === series.id && e.relation === "serves");
  state.edges = [...state.edges, ...serves.map((e) => ({ ...e, fromId: id }))];
  return item;
}
