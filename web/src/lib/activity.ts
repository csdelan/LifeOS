/**
 * The Activity page's shapes, numbers and words (ACTIVITY-1..4, activity plan §3.6–§3.7): the
 * API's numbers as numbers (it types them `number | string`), the step bands in words, the
 * 7-day average, the year view's weekly bars, and the week summary the mock answers the way
 * the kernel does.
 */
import { addDays, startOfWeekMonday } from "@/lib/dates";
import type {
  ActivityDayDetailRead,
  ActivityDayRow,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivitySyncResult,
  ActivityWeekRow,
  ActivityWorkoutsRead,
  OuraState,
  OuraStatus,
  SleepPeriodRow,
  StepBand,
  WorkoutRow,
} from "@/lib/production-ui-types";

/** Chris's step thresholds (ACTIVITY-2): the API answers them; the mock uses these. */
export const ACTIVITY_TARGETS = { minimumSteps: 5_000, goalSteps: 7_500 } as const;

export interface StepTargets {
  minimumSteps: number;
  goalSteps: number;
}

/** A day with at least this much time without the ring says so beside its steps. */
export const HIGH_NON_WEAR_S = 2 * 3_600;

/** The longest workout note, in characters. */
export const NOTE_MAX = 140;

// ---------------------------------------------------------------------------
// The API's numbers
// ---------------------------------------------------------------------------

function num(value: unknown): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

function numOrNull(value: unknown): number | null {
  return value === null || value === undefined || value === "" ? null : num(value);
}

const STATES: readonly OuraState[] = ["not_configured", "not_connected", "reconnect", "connected"];

export function normalizeOuraStatus(raw: OuraStatus): OuraStatus {
  return {
    state: STATES.includes(raw.state) ? raw.state : "not_connected",
    configured: Boolean(raw.configured),
    connected: Boolean(raw.connected),
    needsReconnect: Boolean(raw.needsReconnect),
    reconnectReason: raw.reconnectReason ?? null,
    connectedAt: raw.connectedAt ?? null,
    expiresAt: raw.expiresAt ?? null,
    scopes: raw.scopes ?? [],
    missingScopes: raw.missingScopes ?? [],
    lastSyncedAt: raw.lastSyncedAt ?? null,
    lastSyncError: raw.lastSyncError ?? null,
    historyFrom: raw.historyFrom ?? null,
    historyDone: Boolean(raw.historyDone),
    startDate: raw.startDate ?? null,
  };
}

/** What a day before the start date says: LifeOS never read Oura for it (`Oura:StartDate`). */
export const BEFORE_START = "before LifeOS read Oura";

/** The day comes before the first day LifeOS reads Oura for. */
export function beforeStart(day: string, start: string | null | undefined): boolean {
  return start != null && day < start;
}

export function normalizeActivityDay(raw: ActivityDayRow): ActivityDayRow {
  return {
    day: raw.day,
    steps: numOrNull(raw.steps),
    band: raw.band ?? null,
    activeCalories: numOrNull(raw.activeCalories),
    totalCalories: numOrNull(raw.totalCalories),
    walkingM: numOrNull(raw.walkingM),
    highS: numOrNull(raw.highS),
    mediumS: numOrNull(raw.mediumS),
    lowS: numOrNull(raw.lowS),
    nonWearS: numOrNull(raw.nonWearS),
    activityScore: numOrNull(raw.activityScore),
    sleepScore: numOrNull(raw.sleepScore),
    readinessScore: numOrNull(raw.readinessScore),
    asleepS: numOrNull(raw.asleepS),
    inBedS: numOrNull(raw.inBedS),
    bedAt: raw.bedAt ?? null,
    wokeAt: raw.wokeAt ?? null,
    napCount: num(raw.napCount),
    napAsleepS: num(raw.napAsleepS),
  };
}

export function normalizeActivityDays(raw: ActivityDaysRead): ActivityDaysRead {
  return {
    ...raw,
    minimumSteps: num(raw.minimumSteps) || ACTIVITY_TARGETS.minimumSteps,
    goalSteps: num(raw.goalSteps) || ACTIVITY_TARGETS.goalSteps,
    days: (raw.days ?? []).map(normalizeActivityDay),
  };
}

export function normalizeWorkout(raw: WorkoutRow): WorkoutRow {
  return {
    ...raw,
    label: raw.label ?? null,
    seconds: num(raw.seconds),
    calories: numOrNull(raw.calories),
    distanceM: numOrNull(raw.distanceM),
    intensity: raw.intensity ?? null,
    counted: Boolean(raw.counted),
    reason: raw.reason ?? null,
    note: raw.note ?? null,
    noteAt: raw.noteAt ?? null,
  };
}

export function normalizeWorkouts(raw: ActivityWorkoutsRead): ActivityWorkoutsRead {
  return { ...raw, workouts: (raw.workouts ?? []).map(normalizeWorkout) };
}

export function normalizeWeek(raw: ActivityWeekRow): ActivityWeekRow {
  return {
    from: raw.from,
    to: raw.to,
    averageSteps: numOrNull(raw.averageSteps),
    stepDays: num(raw.stepDays),
    goalDays: num(raw.goalDays),
    underDays: num(raw.underDays),
    sessions: num(raw.sessions),
    exerciseSeconds: num(raw.exerciseSeconds),
    exerciseCalories: num(raw.exerciseCalories),
    averageSleep: numOrNull(raw.averageSleep),
    averageReadiness: numOrNull(raw.averageReadiness),
  };
}

export function normalizeSummary(raw: ActivitySummaryRead): ActivitySummaryRead {
  return {
    today: raw.today,
    minimumSteps: num(raw.minimumSteps) || ACTIVITY_TARGETS.minimumSteps,
    goalSteps: num(raw.goalSteps) || ACTIVITY_TARGETS.goalSteps,
    todaySoFar: normalizeActivityDay(raw.todaySoFar),
    todayWorkouts: (raw.todayWorkouts ?? []).map(normalizeWorkout),
    thisWeek: normalizeWeek(raw.thisWeek),
    lastWeek: normalizeWeek(raw.lastWeek),
    oura: normalizeOuraStatus(raw.oura),
  };
}

export function normalizeSleepPeriod(raw: SleepPeriodRow): SleepPeriodRow {
  return {
    id: raw.id,
    type: raw.type ?? "",
    startedAt: raw.startedAt,
    endedAt: raw.endedAt,
    inBedS: numOrNull(raw.inBedS),
    asleepS: numOrNull(raw.asleepS),
    deepS: numOrNull(raw.deepS),
    lightS: numOrNull(raw.lightS),
    remS: numOrNull(raw.remS),
    awakeS: numOrNull(raw.awakeS),
    latencyS: numOrNull(raw.latencyS),
    efficiency: numOrNull(raw.efficiency),
    averageHrv: numOrNull(raw.averageHrv),
    averageHr: numOrNull(raw.averageHr),
    lowestHr: numOrNull(raw.lowestHr),
  };
}

function contributors(raw: Record<string, unknown> | null | undefined): Record<string, number | null> {
  return Object.fromEntries(Object.entries(raw ?? {}).map(([k, v]) => [k, numOrNull(v)]));
}

export function normalizeDayDetail(raw: ActivityDayDetailRead): ActivityDayDetailRead {
  return {
    day: raw.day,
    today: raw.today,
    minimumSteps: num(raw.minimumSteps) || ACTIVITY_TARGETS.minimumSteps,
    goalSteps: num(raw.goalSteps) || ACTIVITY_TARGETS.goalSteps,
    summary: normalizeActivityDay(raw.summary),
    sleepContributors: contributors(raw.sleepContributors),
    readinessContributors: contributors(raw.readinessContributors),
    temperatureDeviation: numOrNull(raw.temperatureDeviation),
    temperatureTrendDeviation: numOrNull(raw.temperatureTrendDeviation),
    night: raw.night ? normalizeSleepPeriod(raw.night) : null,
    naps: (raw.naps ?? []).map(normalizeSleepPeriod),
    workouts: (raw.workouts ?? []).map(normalizeWorkout),
  };
}

export function normalizeSync(raw: ActivitySyncResult): ActivitySyncResult {
  return {
    ran: Boolean(raw.ran),
    changed: Boolean(raw.changed),
    from: raw.from ?? null,
    to: raw.to ?? null,
    records: num(raw.records),
    written: num(raw.written),
    voided: num(raw.voided),
    historyFrom: raw.historyFrom ?? null,
    historyDone: Boolean(raw.historyDone),
    lastSyncedAt: raw.lastSyncedAt ?? null,
  };
}

// ---------------------------------------------------------------------------
// The charts' ranges (the Steps card and the Sleep and readiness card)
// ---------------------------------------------------------------------------

export const METRIC_RANGES = [
  { id: "30", label: "30 days" },
  { id: "90", label: "90 days" },
  { id: "365", label: "1 year" },
] as const;

export type MetricRange = (typeof METRIC_RANGES)[number]["id"];

/** The 7-day average needs the six days before the first one shown. */
export const LEAD_DAYS = 6;

/** The first day a range asks for: the lead-in for the average, or the Monday a year of weeks starts on. */
export function rangeFrom(range: MetricRange, today: string): string {
  return range === "365"
    ? addDays(startOfWeekMonday(today), -7 * 51)
    : addDays(today, -(Number(range) - 1) - LEAD_DAYS);
}

/** The first day a range shows. */
export function rangeStart(range: MetricRange, today: string): string {
  return range === "365" ? rangeFrom(range, today) : addDays(today, -(Number(range) - 1));
}

// ---------------------------------------------------------------------------
// Steps and their bands
// ---------------------------------------------------------------------------

/** Which band a step count falls in, as the kernel's `ActivityTargets.Band` says. */
export function stepBand(steps: number, targets: StepTargets = ACTIVITY_TARGETS): StepBand {
  return steps >= targets.goalSteps ? "goal" : steps >= targets.minimumSteps ? "minimum" : "under";
}

/** Each band's tone (index.css `--steps-*`, an ordinal ramp validated in both themes). */
export const BAND_TONE: Record<StepBand, string> = {
  under: "bg-steps-under",
  minimum: "bg-steps-minimum",
  goal: "bg-steps-goal",
};

/** The band in words, so colour is never the only cue. */
export function bandWords(band: StepBand): string {
  return band === "goal" ? "at the goal" : band === "minimum" ? "over the minimum" : "under the minimum";
}

/** "Tue, Oct 21": a day as the readouts say it. */
export function formatWeekday(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" });
}

/** "over the minimum, 1,260 short of the goal": where a count stands, without the count. */
export function stepsVerdict(steps: number, targets: StepTargets): string {
  const band = stepBand(steps, targets);
  if (band === "goal") return "at the goal";
  if (band === "minimum") return `over the minimum, ${(targets.goalSteps - steps).toLocaleString()} short of the goal`;
  return `${(targets.minimumSteps - steps).toLocaleString()} under the minimum`;
}

/** "6,240 steps, over the minimum, 1,260 short of the goal": a count against the thresholds. */
export function describeSteps(steps: number, targets: StepTargets, soFar = false): string {
  return `${steps.toLocaleString()} steps${soFar ? " so far" : ""}, ${stepsVerdict(steps, targets)}`;
}

/** "Today", or "Tue, Oct 21". */
export function dayName(day: string, today: string): string {
  return day === today ? "Today" : formatWeekday(day);
}

/**
 * A day's steps against the thresholds, and a long time without the ring, without its name. A
 * day before `start` (the Oura start date) says LifeOS never read it.
 */
export function stepDayText(day: ActivityDayRow, targets: StepTargets, today: string, start?: string | null): string {
  if (day.steps == null) return beforeStart(day.day, start) ? BEFORE_START : "no steps recorded";
  const nonWear =
    day.nonWearS != null && day.nonWearS >= HIGH_NON_WEAR_S ? `, ${formatDuration(day.nonWearS)} without the ring` : "";
  return `${describeSteps(day.steps, targets, day.day === today)}${nonWear}`;
}

/** A day's readout: "Tue, Oct 21: 6,240 steps, over the minimum, 1,260 short of the goal". */
export function describeStepDay(day: ActivityDayRow, targets: StepTargets, today: string): string {
  return `${dayName(day.day, today)}: ${stepDayText(day, targets, today)}`;
}

/**
 * The 7-day average for each day up to yesterday: the mean of the days with steps among it and
 * the six before. Today is still going, so it isn't averaged (null), as in the summary.
 */
export function rollingAverage(days: ActivityDayRow[], today: string, window = 7): (number | null)[] {
  return days.map((d, i) => {
    if (d.day >= today) return null;
    const known = days
      .slice(Math.max(0, i - window + 1), i + 1)
      .map((x) => x.steps)
      .filter((s): s is number => s != null);
    return known.length === 0 ? null : Math.round(known.reduce((a, b) => a + b, 0) / known.length);
  });
}

/** A week of steps for the year view: its average day, and whether it's still going. */
export interface StepWeek {
  /** The Monday it starts on. */
  from: string;
  to: string;
  /** The average over its days with steps, before today; this week's so far when it has none yet. */
  average: number | null;
  days: number;
  /** It holds today, so its average isn't final. */
  partial: boolean;
}

/** The days grouped into Monday-to-Sunday weeks, oldest first, for a bar per week. */
export function stepWeeks(days: ActivityDayRow[], today: string): StepWeek[] {
  const weeks = new Map<string, ActivityDayRow[]>();
  for (const d of days) {
    const monday = startOfWeekMonday(d.day);
    weeks.set(monday, [...(weeks.get(monday) ?? []), d]);
  }

  return [...weeks.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([from, rows]) => {
      const complete = rows.filter((d) => d.day < today && d.steps != null).map((d) => d.steps!);
      const partial = rows.some((d) => d.day === today);
      const soFar = rows.find((d) => d.day === today)?.steps ?? null;
      const average =
        complete.length > 0 ? Math.round(complete.reduce((a, b) => a + b, 0) / complete.length) : partial ? soFar : null;
      return { from, to: addDays(from, 6), average, days: complete.length, partial };
    });
}

// ---------------------------------------------------------------------------
// The week summary (the kernel's ActivityReads.GetSummaryAsync, for the mock)
// ---------------------------------------------------------------------------

function average(values: (number | null)[]): number | null {
  const known = values.filter((v): v is number => v != null);
  return known.length === 0 ? null : Math.round(known.reduce((a, b) => a + b, 0) / known.length);
}

/**
 * A week as the summary counts it. Today counts toward the goal once it gets there, but isn't
 * averaged or counted under the minimum until it's over; a day with no record isn't under.
 */
export function summarizeWeek(
  from: string,
  to: string,
  days: ActivityDayRow[],
  workouts: WorkoutRow[],
  today: string,
  targets: StepTargets = ACTIVITY_TARGETS,
): ActivityWeekRow {
  const inWeek = days.filter((d) => d.day >= from && d.day <= to);
  const complete = inWeek.filter((d) => d.day < today && d.steps != null).map((d) => d.steps!);
  const counted = workouts.filter((w) => w.day >= from && w.day <= to && w.counted);
  return {
    from,
    to,
    averageSteps: average(complete),
    stepDays: complete.length,
    goalDays: inWeek.filter((d) => d.steps != null && d.steps >= targets.goalSteps).length,
    underDays: complete.filter((s) => s < targets.minimumSteps).length,
    sessions: counted.length,
    exerciseSeconds: counted.reduce((s, w) => s + w.seconds, 0),
    exerciseCalories: counted.reduce((s, w) => s + (w.calories ?? 0), 0),
    averageSleep: average(inWeek.map((d) => d.sleepScore)),
    averageReadiness: average(inWeek.map((d) => d.readinessScore)),
  };
}

// ---------------------------------------------------------------------------
// Workouts
// ---------------------------------------------------------------------------

/** "tableTennis" → "Table tennis", "strength_training" → "Strength training". */
export function readableActivity(activity: string): string {
  const words = activity
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim()
    .toLowerCase();
  return words ? words[0].toUpperCase() + words.slice(1) : "Workout";
}

/** Oura's label when there is one, otherwise the activity made readable. */
export function workoutName(workout: Pick<WorkoutRow, "activity" | "label">): string {
  return workout.label?.trim() || readableActivity(workout.activity);
}

/** Where a workout came from, in words. */
export function sourceWords(source: string): string {
  switch (source.trim().toLowerCase()) {
    case "manual":
      return "Entered in the Oura app";
    case "workout_heart_rate":
      return "Recorded live with heart rate";
    case "live_oura_heart_rate":
      return "Recorded live with the ring";
    case "live_third_party_heart_rate":
      return "Recorded live with another device";
    case "autodetected":
      return "Detected by Oura";
    case "confirmed":
      return "Detected by Oura and confirmed";
    default:
      return source;
  }
}

/** "Easy", "Moderate", "Hard", or Oura's own word. */
export function intensityWords(intensity: string | null): string | null {
  return intensity ? readableActivity(intensity) : null;
}

/** "40 min", "1 h 5 min", "2 h". */
export function formatDuration(seconds: number): string {
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} h` : `${h} h ${m} min`;
}

/** "5.1 km", or "850 m" under a kilometre. */
export function formatDistance(metres: number): string {
  return metres < 1_000 ? `${Math.round(metres)} m` : `${(metres / 1_000).toFixed(1)} km`;
}

/** "7:02 AM", in the browser's zone. */
export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

/** Workouts grouped into Monday-to-Sunday weeks, newest week first, each newest first. */
export function workoutWeeks(workouts: WorkoutRow[]): { from: string; workouts: WorkoutRow[] }[] {
  const weeks = new Map<string, WorkoutRow[]>();
  for (const w of [...workouts].sort((a, b) => b.startedAt.localeCompare(a.startedAt))) {
    const monday = startOfWeekMonday(w.day);
    weeks.set(monday, [...(weeks.get(monday) ?? []), w]);
  }
  return [...weeks.entries()].sort(([a], [b]) => b.localeCompare(a)).map(([from, list]) => ({ from, workouts: list }));
}

/** The Exercise card's window: the last 12 whole weeks, Monday to Sunday, through this one. */
export const EXERCISE_WEEKS = 12;

export function exerciseFrom(today: string): string {
  return addDays(startOfWeekMonday(today), -7 * (EXERCISE_WEEKS - 1));
}

/** Counted exercise per week for the last `weeks` weeks through today's, oldest first. */
export function weeklyExercise(
  workouts: WorkoutRow[],
  today: string,
  weeks = 12,
): { from: string; seconds: number; sessions: number }[] {
  const thisMonday = startOfWeekMonday(today);
  return Array.from({ length: weeks }, (_, i) => {
    const from = addDays(thisMonday, -7 * (weeks - 1 - i));
    const to = addDays(from, 6);
    const counted = workouts.filter((w) => w.counted && w.day >= from && w.day <= to);
    return { from, seconds: counted.reduce((s, w) => s + w.seconds, 0), sessions: counted.length };
  });
}

/** "This week" / "Last week" / "Week of Oct 6". */
export function weekTitle(from: string, today: string): string {
  const thisMonday = startOfWeekMonday(today);
  if (from === thisMonday) return "This week";
  if (from === addDays(thisMonday, -7)) return "Last week";
  const [y, m, d] = from.split("-").map(Number);
  return `Week of ${new Date(y, m - 1, d).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}

// ---------------------------------------------------------------------------
// Sleep and readiness
// ---------------------------------------------------------------------------

/** Oura's bands for a sleep or readiness score (and each contributor): 85 optimal, 70 good. */
export const OURA_SCORE_LINES = { good: 70, optimal: 85 } as const;

export type ScoreBand = "attention" | "good" | "optimal";

export function scoreBand(score: number): ScoreBand {
  return score >= OURA_SCORE_LINES.optimal ? "optimal" : score >= OURA_SCORE_LINES.good ? "good" : "attention";
}

/** Each band's tone (index.css `--sleep-*`, an ordinal ramp validated in both themes). */
export const SCORE_TONE: Record<ScoreBand, string> = {
  attention: "bg-sleep-attention",
  good: "bg-sleep-good",
  optimal: "bg-sleep-optimal",
};

/** Oura's band for a sleep or readiness score, in its words: "Optimal", "Good", "Pay attention". */
export function ouraScoreWords(score: number): string {
  const band = scoreBand(score);
  return band === "optimal" ? "Optimal" : band === "good" ? "Good" : "Pay attention";
}

export type SleepMetric = "sleep" | "readiness" | "asleep";

export const SLEEP_METRICS: { id: SleepMetric; label: string }[] = [
  { id: "sleep", label: "Sleep score" },
  { id: "readiness", label: "Readiness" },
  { id: "asleep", label: "Time asleep" },
];

/** A day's value on a sleep metric, in the chart's unit: a score, or hours asleep. */
export function sleepValue(day: ActivityDayRow, metric: SleepMetric): number | null {
  if (metric === "sleep") return day.sleepScore;
  if (metric === "readiness") return day.readinessScore;
  return day.asleepS != null ? day.asleepS / 3_600 : null;
}

/** "7 h 12 min" from hours. */
export function formatHours(hours: number): string {
  return formatDuration(hours * 3_600);
}

/**
 * The 7-day mean ending on each value: of the known values among it and the six before, or
 * null when there are none. Unlike steps, a night is over by morning, so today counts.
 */
export function rollingMean(values: (number | null)[], window = 7): (number | null)[] {
  return values.map((_, i) => {
    const known = values.slice(Math.max(0, i - window + 1), i + 1).filter((v): v is number => v != null);
    return known.length === 0 ? null : known.reduce((a, b) => a + b, 0) / known.length;
  });
}

/** A metric's value said in words: "73", or "7 h 12 min". */
export function sleepValueWords(value: number, metric: SleepMetric): string {
  return metric === "asleep" ? formatHours(value) : String(Math.round(value));
}

/**
 * A day on a sleep metric, without its name: "sleep score 73, good"; "6 h 41 min asleep of 7 h
 * 21 min in bed". A day before `start` (the Oura start date) says LifeOS never read it.
 */
export function sleepDayText(day: ActivityDayRow, metric: SleepMetric, today: string, start?: string | null): string {
  if (metric === "asleep") {
    if (day.asleepS == null) {
      if (beforeStart(day.day, start)) return BEFORE_START;
      return day.day === today ? "last night hasn't synced yet" : "no sleep recorded";
    }
    const inBed = day.inBedS != null ? ` of ${formatDuration(day.inBedS)} in bed` : "";
    const naps =
      day.napCount > 0
        ? `, and ${day.napCount === 1 ? "a nap" : `${day.napCount} naps`} (${formatDuration(day.napAsleepS)})`
        : "";
    return `${formatDuration(day.asleepS)} asleep${inBed}${naps}`;
  }
  const score = metric === "sleep" ? day.sleepScore : day.readinessScore;
  const name = metric === "sleep" ? "sleep score" : "readiness";
  if (score == null) {
    if (beforeStart(day.day, start)) return BEFORE_START;
    return day.day === today ? "last night hasn't synced yet" : `no ${name}`;
  }
  return `${name} ${score}, ${ouraScoreWords(score).toLowerCase()}`;
}

/** A week on a sleep metric for the year view: the average over its nights so far. */
export interface MetricWeek {
  /** The Monday it starts on. */
  from: string;
  to: string;
  average: number | null;
  nights: number;
  /** It holds today, so its average isn't final. */
  partial: boolean;
}

/** The days grouped into Monday-to-Sunday weeks, oldest first, each averaged over its known values. */
export function metricWeeks(
  days: ActivityDayRow[],
  today: string,
  value: (day: ActivityDayRow) => number | null,
): MetricWeek[] {
  const weeks = new Map<string, ActivityDayRow[]>();
  for (const d of days) {
    if (d.day > today) continue;
    const monday = startOfWeekMonday(d.day);
    weeks.set(monday, [...(weeks.get(monday) ?? []), d]);
  }
  return [...weeks.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([from, rows]) => {
      const known = rows.map(value).filter((v): v is number => v != null);
      return {
        from,
        to: addDays(from, 6),
        average: known.length === 0 ? null : known.reduce((a, b) => a + b, 0) / known.length,
        nights: known.length,
        partial: rows.some((d) => d.day === today),
      };
    });
}

// Oura's contributors, in the order the Oura app lists them; any new one follows.
export const SLEEP_CONTRIBUTORS = [
  "total_sleep",
  "efficiency",
  "restfulness",
  "rem_sleep",
  "deep_sleep",
  "latency",
  "timing",
] as const;

export const READINESS_CONTRIBUTORS = [
  "resting_heart_rate",
  "hrv_balance",
  "body_temperature",
  "recovery_index",
  "previous_night",
  "sleep_balance",
  "sleep_regularity",
  "previous_day_activity",
  "activity_balance",
] as const;

const CONTRIBUTOR_NAMES: Record<string, string> = {
  rem_sleep: "REM sleep",
  hrv_balance: "HRV balance",
};

/** "hrv_balance" → "HRV balance", "previous_day_activity" → "Previous day activity". */
export function contributorName(key: string): string {
  return CONTRIBUTOR_NAMES[key] ?? readableActivity(key);
}

/** A score's contributors in Oura's order, then any Oura has added since, by name. */
export function orderedContributors(
  values: Record<string, number | null>,
  order: readonly string[],
): { key: string; name: string; value: number | null }[] {
  const known = order.filter((k) => k in values);
  const rest = Object.keys(values)
    .filter((k) => !order.includes(k))
    .sort((a, b) => contributorName(a).localeCompare(contributorName(b)));
  return [...known, ...rest].map((key) => ({ key, name: contributorName(key), value: values[key] }));
}

export type SleepStage = "deep" | "light" | "rem" | "awake";

/** Each stage's tone (index.css `--sleep-*`): an ordinal ramp by depth, validated in both themes. */
export const STAGE_TONE: Record<SleepStage, string> = {
  deep: "bg-sleep-deep",
  light: "bg-sleep-light",
  rem: "bg-sleep-rem",
  awake: "bg-sleep-awake",
};

export const STAGE_NAMES: Record<SleepStage, string> = { deep: "Deep", light: "Light", rem: "REM", awake: "Awake" };

/** A period's time in bed by stage, deepest first, with each one's share; empty when Oura has none. */
export function sleepStages(period: SleepPeriodRow): { stage: SleepStage; seconds: number; share: number }[] {
  const parts: [SleepStage, number | null][] = [
    ["deep", period.deepS],
    ["light", period.lightS],
    ["rem", period.remS],
    ["awake", period.awakeS],
  ];
  const total = parts.reduce((s, [, v]) => s + (v ?? 0), 0);
  if (total <= 0) return [];
  return parts.map(([stage, v]) => ({ stage, seconds: v ?? 0, share: (v ?? 0) / total }));
}

/** "11:05 PM to 7:10 AM": when a period started and ended. */
export function bedToWake(period: Pick<SleepPeriodRow, "startedAt" | "endedAt">): string {
  return `${formatTime(period.startedAt)} to ${formatTime(period.endedAt)}`;
}

/** "+0.3 °C", "−0.2 °C", "±0.0 °C": the temperature against Chris's usual. */
export function formatTemperature(deviation: number): string {
  const rounded = Math.round(deviation * 10) / 10;
  const sign = rounded > 0 ? "+" : rounded < 0 ? "−" : "±";
  return `${sign}${Math.abs(rounded).toFixed(1)} °C`;
}

// ---------------------------------------------------------------------------
// Syncing
// ---------------------------------------------------------------------------

/** "just now", "3 min ago", "2 h ago", "yesterday", or the date. */
export function syncedAgo(iso: string, now: Date = new Date()): string {
  const seconds = Math.max(0, (now.getTime() - Date.parse(iso)) / 1_000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  if (hours < 48) return "yesterday";
  return `on ${new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}
