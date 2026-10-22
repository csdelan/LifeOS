/**
 * The mock store's activity half (ACTIVITY-1..4 and 6, activity plan §3.7 and §3.9): about 120
 * days of steps, sleep and readiness (each night with its stages, heart and both scores'
 * contributors), 40 workouts of every source, walks and detections included, and tags from the
 * Oura app (a coffee most mornings, drinks, the sauna, a custom tag, a note, a few days sick), all
 * relative to today and the same on every load. It answers as `/api/oura` and `/api/activity`
 * do — the thresholds, `counted` and its reasons, this week against last, the note's
 * refusals, the page's sync skipped under 5 minutes — so the page behaves the same on either.
 * Its sync changes nothing here; the store pays activity XP after it (`mockSettleActivity`) and
 * fills in what each day paid.
 */
import {
  ACTIVITY_TARGETS,
  NOTE_MAX,
  NOTHING_PAID,
  READINESS_CONTRIBUTORS,
  SLEEP_CONTRIBUTORS,
  stepBand,
  summarizeWeek,
} from "@/lib/activity";
import { addDays, nowIso, startOfWeekMonday, todayIso } from "@/lib/dates";
import { exerciseVerdict } from "@/lib/exercise-rules";
import type {
  ActivityDayDetailRead,
  ActivityDayRow,
  ActivityDaysRead,
  ActivitySummaryRead,
  ActivitySyncResult,
  ActivityTagRow,
  ActivityTagsRead,
  ActivityWorkoutsRead,
  OuraStatus,
  SleepPeriodRow,
  WorkoutRow,
} from "@/lib/production-ui-types";

/** How many days the seed holds, through today. */
export const SEEDED_DAYS = 120;

/** The widest range a read answers, in days (the API's `ActivityReads.MaxRangeDays`). */
export const MAX_RANGE_DAYS = 731;

/** A sync on opening the page is skipped when the last was this recent. */
export const PAGE_SYNC_MS = 5 * 60_000;

interface ActivityState {
  oura: OuraStatus;
  /** Keyed by day. */
  days: Map<string, ActivityDayRow>;
  /** Newest first. */
  workouts: WorkoutRow[];
  /** Newest first. */
  tags: ActivityTagRow[];
  pendingState: string | null;
}

let seeded: ActivityState | null = null;

/** Seeded on first use, so "today" is the day it's first read. */
function store(): ActivityState {
  return (seeded ??= seedActivity());
}

export function resetActivityStore() {
  seeded = null;
}

/**
 * Test seam: no Oura days, workouts or tags, so nothing pays activity XP. Activity XP pays from
 * the real date, so tests about anything else clear it to stay the same on every day.
 */
export function clearMockActivity() {
  const state = store();
  state.days = new Map();
  state.workouts = [];
  state.tags = [];
}

/** Test seam: put the connection in any state (not connected, reconnect, a sync error…). */
export function setMockOura(patch: Partial<OuraStatus>) {
  const state = store();
  state.oura = { ...state.oura, ...patch };
}

// ---------------------------------------------------------------------------
// The seed
// ---------------------------------------------------------------------------

/** A small deterministic generator, so a day's numbers are the same on every load. */
function random(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4_294_967_296;
  };
}

/** An instant on `day` at local `hh:mm`. */
function at(day: string, hour: number, minute = 0): string {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d, hour, minute).toISOString();
}

function emptyDay(day: string): ActivityDayRow {
  return {
    day,
    steps: null,
    band: null,
    activeCalories: null,
    totalCalories: null,
    walkingM: null,
    highS: null,
    mediumS: null,
    lowS: null,
    nonWearS: null,
    activityScore: null,
    sleepScore: null,
    readinessScore: null,
    asleepS: null,
    inBedS: null,
    bedAt: null,
    wokeAt: null,
    napCount: 0,
    napAsleepS: 0,
    paid: { ...NOTHING_PAID },
  };
}

function seedDay(day: string, ago: number, today: string): ActivityDayRow {
  const r = random(ago * 7_919 + 17);
  const row = emptyDay(day);
  // Two days without the ring at all: nothing recorded.
  if (ago === 23 || ago === 61) return row;

  const weekday = new Date(`${day}T12:00:00`).getDay();
  const weekend = weekday === 0 || weekday === 6;
  let steps = Math.round(3_200 + r() * 7_800 + (weekend ? 1_500 : 0));
  // Every so often a day the ring sat on the charger for hours.
  const offRing = ago % 17 === 5;
  if (offRing) steps = Math.round(steps * 0.55);
  // Today is still going.
  if (day === today) steps = Math.round(steps * 0.6);

  row.steps = steps;
  row.band = stepBand(steps);
  row.activeCalories = Math.round(steps * 0.045 + r() * 60);
  row.totalCalories = 2_050 + row.activeCalories;
  row.walkingM = Math.round(steps * 0.76);
  row.highS = Math.round(r() * 1_200);
  row.mediumS = Math.round(600 + r() * 2_400);
  row.lowS = Math.round(5_400 + r() * 7_200);
  row.nonWearS = offRing ? Math.round(3 * 3_600 + r() * 3_600) : Math.round(r() * 1_800);
  row.activityScore = Math.round(62 + r() * 34);

  // Last night counts on the morning it ended.
  row.sleepScore = Math.round(58 + r() * 36);
  row.readinessScore = Math.round(55 + r() * 40);
  const inBedMinutes = Math.round(390 + r() * 150);
  const asleepMinutes = Math.round(inBedMinutes * (0.82 + r() * 0.12));
  const bed = at(addDays(day, -1), 22, Math.round(r() * 90));
  row.bedAt = bed;
  row.wokeAt = new Date(Date.parse(bed) + inBedMinutes * 60_000).toISOString();
  row.inBedS = inBedMinutes * 60;
  row.asleepS = asleepMinutes * 60;
  if (ago % 9 === 4) {
    row.napCount = 1;
    row.napAsleepS = Math.round(20 + r() * 25) * 60;
  }
  return row;
}

interface SeedWorkout {
  ago: number;
  hour: number;
  activity: string;
  source: string;
  minutes: number;
  label?: string;
  distanceKm?: number;
  intensity?: "easy" | "moderate" | "hard";
  note?: string;
}

/** The last fortnight by hand, so the page always has a run, a walk and a detection to show. */
const RECENT: SeedWorkout[] = [
  { ago: 0, hour: 7, activity: "running", source: "manual", minutes: 32, distanceKm: 5.2, intensity: "moderate" },
  { ago: 1, hour: 12, activity: "walking", source: "manual", minutes: 35, distanceKm: 2.6, intensity: "easy", label: "Lunch loop" },
  { ago: 2, hour: 18, activity: "strengthTraining", source: "workout_heart_rate", minutes: 45, intensity: "hard" },
  { ago: 3, hour: 7, activity: "running", source: "manual", minutes: 41, distanceKm: 6.4, intensity: "hard", note: "Hill repeats, left knee fine" },
  { ago: 3, hour: 17, activity: "walking", source: "autodetected", minutes: 28, distanceKm: 2.1, intensity: "easy" },
  { ago: 4, hour: 9, activity: "cycling", source: "autodetected", minutes: 25, distanceKm: 8.3, intensity: "moderate" },
  { ago: 5, hour: 6, activity: "yoga", source: "live_oura_heart_rate", minutes: 30, intensity: "easy" },
  { ago: 7, hour: 8, activity: "cycling", source: "manual", minutes: 62, distanceKm: 24.5, intensity: "moderate" },
  { ago: 8, hour: 19, activity: "tableTennis", source: "confirmed", minutes: 50, intensity: "moderate" },
  { ago: 9, hour: 7, activity: "running", source: "live_third_party_heart_rate", minutes: 38, distanceKm: 6, intensity: "moderate" },
  { ago: 10, hour: 13, activity: "padel_v2", source: "garmin_import", minutes: 60, intensity: "hard" },
  { ago: 11, hour: 18, activity: "swimming", source: "manual", minutes: 40, distanceKm: 1.6, intensity: "moderate" },
  { ago: 12, hour: 10, activity: "hiking", source: "manual", minutes: 95, distanceKm: 7.8, intensity: "moderate", label: "Ridge trail" },
];

const OLDER = ["running", "cycling", "strengthTraining", "walking", "yoga", "rowing", "running", "swimming"] as const;
const OLDER_SOURCES = ["manual", "manual", "workout_heart_rate", "manual", "autodetected", "manual", "confirmed", "live_oura_heart_rate"] as const;

function seedWorkouts(today: string): WorkoutRow[] {
  const seeds = [...RECENT];
  // 27 more, every four days back from a fortnight ago.
  for (let i = 0; i < 27; i++) {
    const r = random(i * 104_729 + 3);
    const activity = OLDER[i % OLDER.length];
    seeds.push({
      ago: 14 + i * 4,
      hour: 6 + Math.round(r() * 12),
      activity,
      source: OLDER_SOURCES[i % OLDER_SOURCES.length],
      minutes: Math.round(25 + r() * 50),
      distanceKm: activity === "running" || activity === "cycling" || activity === "walking" ? Math.round((2 + r() * 10) * 10) / 10 : undefined,
      intensity: (["easy", "moderate", "hard"] as const)[Math.floor(r() * 3)],
    });
  }

  return seeds
    .map((s, i): WorkoutRow => {
      const day = addDays(today, -s.ago);
      const startedAt = at(day, s.hour, (i * 7) % 60);
      const verdict = exerciseVerdict(s.source, s.activity);
      return {
        id: `mock-workout-${String(i + 1).padStart(2, "0")}-${s.activity.toLowerCase()}`,
        day,
        activity: s.activity,
        label: s.label ?? null,
        startedAt,
        endedAt: new Date(Date.parse(startedAt) + s.minutes * 60_000).toISOString(),
        seconds: s.minutes * 60,
        calories: s.activity === "yoga" ? null : Math.round(s.minutes * (s.intensity === "hard" ? 11 : 8)),
        distanceM: s.distanceKm != null ? Math.round(s.distanceKm * 1_000) : null,
        intensity: s.intensity ?? null,
        source: s.source,
        counted: verdict.counted,
        reason: verdict.reason,
        note: s.note ?? null,
        noteAt: s.note ? at(day, 21) : null,
        syncedAt: at(day, 22),
      };
    })
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt));
}

// ---------------------------------------------------------------------------
// Tags from the Oura app (activity plan §3.9)
// ---------------------------------------------------------------------------

const TAG_PREFIXES = ["tag_", "generic_"];

/**
 * How a tag reads, as the kernel's `OuraTags.Name` words it (the API answers the name; only the
 * mock needs this): a custom tag by its own name, a text-only tag as "Note", and Oura's code
 * made readable: its `tag_` and `generic_` prefixes off, split on `_` and capitals, in
 * sentence case.
 */
export function ouraTagName(typeCode: string | null, customName: string | null): string {
  const code = typeCode?.trim();
  if (!code) return "Note";
  if (code.toLowerCase() === "custom") return customName?.trim() || "Custom tag";

  let rest = code;
  for (let stripped = true; stripped; ) {
    stripped = false;
    for (const prefix of TAG_PREFIXES) {
      if (rest.length > prefix.length && rest.toLowerCase().startsWith(prefix)) {
        rest = rest.slice(prefix.length);
        stripped = true;
      }
    }
  }

  const words: string[] = [];
  let word = "";
  const flush = () => {
    if (word) words.push(word);
    word = "";
  };
  const isUpper = (c: string) => c !== c.toLowerCase() && c === c.toUpperCase();
  for (const c of rest) {
    if (c === "_" || c === "-" || c === " ") {
      flush();
      continue;
    }
    if (isUpper(c) && word.length > 0 && !isUpper(word[word.length - 1])) flush();
    word += c;
  }
  flush();
  if (words.length === 0) return code;
  const text = words.join(" ").toLowerCase();
  return text[0].toUpperCase() + text.slice(1);
}

interface SeedTag {
  ago: number;
  hour: number;
  minute?: number;
  typeCode: string | null;
  customName?: string;
  /** A length in minutes, or for a stretch over days, `days`. */
  minutes?: number;
  days?: number;
  comment?: string;
}

function seedTags(today: string): ActivityTagRow[] {
  const seeds: SeedTag[] = [];
  for (let ago = 0; ago < SEEDED_DAYS; ago++) {
    const r = random(ago * 48_271 + 29);
    // A coffee most mornings.
    if (ago % 7 !== 6) seeds.push({ ago, hour: 7, minute: Math.round(r() * 50), typeCode: "tag_generic_caffeine" });
    // Drinks some evenings.
    if (ago % 5 === 2) {
      seeds.push({
        ago,
        hour: 20,
        minute: Math.round(r() * 40),
        typeCode: "tag_generic_alcohol",
        comment: ago === 2 ? "Two IPAs with dinner" : undefined,
      });
    }
    // A late meal now and then.
    if (ago % 11 === 3) seeds.push({ ago, hour: 22, minute: 15, typeCode: "tag_generic_late_meal" });
    // The sauna, with its length.
    if (ago % 9 === 1) {
      seeds.push({
        ago,
        hour: 18,
        minute: 30,
        typeCode: "tag_generic_sauna",
        minutes: 20,
        comment: ago === 1 ? "Felt great after" : undefined,
      });
    }
  }
  // A custom tag, a note with only words, a few days of being sick and a good day.
  seeds.push({ ago: 4, hour: 21, typeCode: "custom", customName: "Cold plunge", minutes: 5 });
  seeds.push({ ago: 6, hour: 13, minute: 10, typeCode: null, comment: "New mattress arrived" });
  seeds.push({ ago: 33, hour: 9, typeCode: "generic_sick", days: 3, comment: "Head cold" });
  seeds.push({ ago: 45, hour: 19, typeCode: "generic_happy", comment: "Good day at work" });

  return seeds
    .map((s, i): ActivityTagRow => {
      const day = addDays(today, -s.ago);
      const startedAt = at(day, s.hour, s.minute ?? 0);
      const endedAt =
        s.days != null
          ? at(addDays(day, s.days), s.hour)
          : s.minutes != null
            ? new Date(Date.parse(startedAt) + s.minutes * 60_000).toISOString()
            : null;
      return {
        id: `mock-tag-${String(i + 1).padStart(3, "0")}`,
        day,
        endDay: s.days != null ? addDays(day, s.days) : endedAt ? day : null,
        typeCode: s.typeCode,
        customName: s.customName ?? null,
        name: ouraTagName(s.typeCode, s.customName ?? null),
        startedAt,
        endedAt,
        seconds: endedAt ? Math.round((Date.parse(endedAt) - Date.parse(startedAt)) / 1_000) : null,
        comment: s.comment ?? null,
        syncedAt: at(day, 23),
      };
    })
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt) || a.id.localeCompare(b.id));
}

function seedActivity(): ActivityState {
  const today = todayIso();
  const days = new Map<string, ActivityDayRow>();
  for (let ago = SEEDED_DAYS - 1; ago >= 0; ago--) {
    const day = addDays(today, -ago);
    days.set(day, seedDay(day, ago, today));
  }

  return {
    oura: {
      state: "connected",
      configured: true,
      connected: true,
      needsReconnect: false,
      reconnectReason: null,
      connectedAt: at(addDays(today, -SEEDED_DAYS), 20),
      expiresAt: new Date(Date.now() + 20 * 3_600_000).toISOString(),
      scopes: ["daily", "workout", "tag"],
      missingScopes: [],
      // Two hours ago, so opening the page syncs.
      lastSyncedAt: new Date(Date.now() - 2 * 3_600_000).toISOString(),
      lastSyncError: null,
      historyFrom: addDays(today, -SEEDED_DAYS + 1),
      historyDone: true,
    },
    days,
    workouts: seedWorkouts(today),
    tags: seedTags(today),
    pendingState: null,
  };
}

// ---------------------------------------------------------------------------
// A day in full: made up from the day's row, the same way on every load
// ---------------------------------------------------------------------------

/** A stable number for a day, so its night is seeded the same whatever "today" is. */
function dayNumber(day: string): number {
  return Math.round(Date.parse(`${day}T00:00:00Z`) / 86_400_000);
}

function around(score: number, r: () => number, spread = 30): number {
  return Math.min(100, Math.max(15, Math.round(score + (r() - 0.5) * spread)));
}

// The eight readiness contributors the sandbox answers; `sleep_regularity` is newer.
const MOCK_READINESS = READINESS_CONTRIBUTORS.filter((k) => k !== "sleep_regularity");

function seedDetail(
  row: ActivityDayRow,
): Omit<ActivityDayDetailRead, "day" | "today" | "minimumSteps" | "goalSteps" | "summary" | "workouts" | "tags"> {
  const r = random(dayNumber(row.day) * 2_654_435 + 11);
  const sleepContributors: Record<string, number | null> = {};
  const readinessContributors: Record<string, number | null> = {};
  if (row.sleepScore != null) for (const k of SLEEP_CONTRIBUTORS) sleepContributors[k] = around(row.sleepScore, r);
  if (row.readinessScore != null) for (const k of MOCK_READINESS) readinessContributors[k] = around(row.readinessScore, r);

  let night: SleepPeriodRow | null = null;
  if (row.bedAt && row.wokeAt && row.asleepS != null && row.inBedS != null) {
    const deep = Math.round(row.asleepS * (0.14 + r() * 0.08));
    const rem = Math.round(row.asleepS * (0.18 + r() * 0.07));
    night = {
      id: `mock-night-${row.day}`,
      type: "long_sleep",
      startedAt: row.bedAt,
      endedAt: row.wokeAt,
      inBedS: row.inBedS,
      asleepS: row.asleepS,
      deepS: deep,
      lightS: row.asleepS - deep - rem,
      remS: rem,
      awakeS: row.inBedS - row.asleepS,
      latencyS: Math.round(4 + r() * 20) * 60,
      efficiency: Math.round((row.asleepS / row.inBedS) * 100),
      averageHrv: Math.round(32 + r() * 38),
      averageHr: Math.round((51 + r() * 10) * 10) / 10,
      lowestHr: Math.round(44 + r() * 9),
    };
  }

  const naps: SleepPeriodRow[] = [];
  if (row.napCount > 0) {
    const startedAt = at(row.day, 14, Math.round(r() * 30));
    const inBed = row.napAsleepS + 6 * 60;
    naps.push({
      id: `mock-nap-${row.day}`,
      type: "sleep",
      startedAt,
      endedAt: new Date(Date.parse(startedAt) + inBed * 1_000).toISOString(),
      inBedS: inBed,
      asleepS: row.napAsleepS,
      deepS: 0,
      lightS: Math.round(row.napAsleepS * 0.8),
      remS: row.napAsleepS - Math.round(row.napAsleepS * 0.8),
      awakeS: inBed - row.napAsleepS,
      latencyS: 4 * 60,
      efficiency: Math.round((row.napAsleepS / inBed) * 100),
      averageHrv: Math.round(30 + r() * 30),
      averageHr: Math.round((58 + r() * 8) * 10) / 10,
      lowestHr: Math.round(50 + r() * 8),
    });
  }

  const hasReadiness = row.readinessScore != null;
  return {
    sleepContributors,
    readinessContributors,
    temperatureDeviation: hasReadiness ? Math.round((r() - 0.5) * 100) / 100 : null,
    temperatureTrendDeviation: hasReadiness ? Math.round((r() - 0.5) * 60) / 100 : null,
    night,
    naps,
  };
}

// ---------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------

function checkRange(from: string, to: string) {
  if (to < from) throw new Error(`The range ends (${to}) before it starts (${from}).`);
  const days = Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000) + 1;
  if (days > MAX_RANGE_DAYS) throw new Error(`Ask for up to ${MAX_RANGE_DAYS} days at a time.`);
}

function daysBetween(from: string, to: string): ActivityDayRow[] {
  const rows: ActivityDayRow[] = [];
  for (let day = from; day <= to; day = addDays(day, 1)) {
    rows.push({ ...(store().days.get(day) ?? emptyDay(day)) });
  }
  return rows;
}

/** The mock's Oura days and live workouts, for the store's activity XP. */
export function mockActivityRecords(): { days: ActivityDayRow[]; workouts: WorkoutRow[] } {
  return { days: [...store().days.values()], workouts: [...store().workouts] };
}

export function mockOuraStatus(): OuraStatus {
  return { ...store().oura };
}

/** Every day from `from` to `to` (default the 30 days through today), oldest first. */
export function mockActivityDays(from?: string, to?: string): ActivityDaysRead {
  const today = todayIso();
  const end = to ?? today;
  const start = from ?? addDays(end, -29);
  checkRange(start, end);
  return { from: start, to: end, today, ...ACTIVITY_TARGETS, days: daysBetween(start, end) };
}

/** Live workouts on those days (default the last 12 whole weeks), newest first, counted or not. */
export function mockActivityWorkouts(from?: string, to?: string): ActivityWorkoutsRead {
  const today = todayIso();
  const end = to ?? today;
  const start = from ?? addDays(startOfWeekMonday(end), -7 * 11);
  checkRange(start, end);
  return {
    from: start,
    to: end,
    today,
    workouts: store().workouts.filter((w) => w.day >= start && w.day <= end).map((w) => ({ ...w })),
  };
}

/** Live tags starting on those days (default the 30 days through today), newest first. */
export function mockActivityTags(from?: string, to?: string): ActivityTagsRead {
  const today = todayIso();
  const end = to ?? today;
  const start = from ?? addDays(end, -29);
  checkRange(start, end);
  return {
    from: start,
    to: end,
    today,
    tags: store()
      .tags.filter((t) => t.day >= start && t.day <= end)
      .map((t) => ({ ...t })),
  };
}

/** One day in full (default today): its row, both scores' contributors, the night, naps, workouts and tags. */
export function mockActivityDay(date?: string): ActivityDayDetailRead {
  const today = todayIso();
  const day = date ?? today;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day) || Number.isNaN(Date.parse(`${day}T00:00:00Z`))) {
    throw new Error(`'${day}' isn't a date: use YYYY-MM-DD.`);
  }
  const row = { ...(store().days.get(day) ?? emptyDay(day)) };
  return {
    day,
    today,
    ...ACTIVITY_TARGETS,
    summary: row,
    ...seedDetail(row),
    workouts: store()
      .workouts.filter((w) => w.day === day)
      .sort((a, b) => a.startedAt.localeCompare(b.startedAt))
      .map((w) => ({ ...w })),
    tags: store()
      .tags.filter((t) => t.day === day)
      .sort((a, b) => a.startedAt.localeCompare(b.startedAt) || a.id.localeCompare(b.id))
      .map((t) => ({ ...t })),
  };
}

/** Today so far, today's workouts, this week against last (Monday to Sunday), and the connection. */
export function mockActivitySummary(): ActivitySummaryRead {
  const today = todayIso();
  const monday = startOfWeekMonday(today);
  const lastMonday = addDays(monday, -7);
  const days = daysBetween(lastMonday, today);
  const workouts = store().workouts.filter((w) => w.day >= lastMonday && w.day <= today);
  return {
    today,
    ...ACTIVITY_TARGETS,
    todaySoFar: days[days.length - 1],
    todayWorkouts: workouts.filter((w) => w.day === today).map((w) => ({ ...w })),
    thisWeek: summarizeWeek(monday, addDays(monday, 6), days, workouts, today),
    lastWeek: summarizeWeek(lastMonday, addDays(monday, -1), days, workouts, today),
    oura: mockOuraStatus(),
  };
}

// ---------------------------------------------------------------------------
// Writes
// ---------------------------------------------------------------------------

const NOT_SET_UP = "Oura isn't set up on this server: Oura__ClientId and Oura__ClientSecret are missing.";

/** Begin connecting: the mock "Oura" sends the browser straight back with a code. */
export function mockAuthorizeOura(redirectUri: string): string {
  if (!store().oura.configured) throw new Error(NOT_SET_UP);
  const pending = `mock-state-${Math.random().toString(36).slice(2, 10)}`;
  store().pendingState = pending;
  const url = new URL(redirectUri);
  url.searchParams.set("code", "mock-code");
  url.searchParams.set("state", pending);
  return url.toString();
}

/** Finish connecting; like the API, a state that wasn't asked for is refused. */
export function mockConnectOura(code: string, connectState: string): OuraStatus {
  if (!code.trim()) throw new Error("Connecting Oura takes the code Oura sent back.");
  if (!store().pendingState || connectState !== store().pendingState) {
    throw new Error("That sign-in wasn't started here, or it expired. Connect Oura again.");
  }
  store().pendingState = null;
  const now = nowIso();
  store().oura = {
    ...store().oura,
    state: "connected",
    connected: true,
    needsReconnect: false,
    reconnectReason: null,
    connectedAt: now,
    lastSyncedAt: now,
    lastSyncError: null,
  };
  return mockOuraStatus();
}

/** The page's sync: skipped under 5 minutes unless forced; it changes nothing here. */
export function mockSyncActivity(force = false): ActivitySyncResult {
  const oura = store().oura;
  if (!oura.configured) throw new Error(NOT_SET_UP);
  if (!oura.connected) throw new Error("Oura isn't connected: press Connect Oura on the Activity page.");
  if (oura.needsReconnect) throw new Error("Oura needs reconnecting: press Reconnect Oura on the Activity page.");

  const last = oura.lastSyncedAt ? Date.parse(oura.lastSyncedAt) : null;
  if (!force && last != null && Date.now() - last >= 0 && Date.now() - last < PAGE_SYNC_MS) {
    return {
      ran: false,
      changed: false,
      from: null,
      to: null,
      records: 0,
      written: 0,
      voided: 0,
      historyFrom: null,
      historyDone: false,
      lastSyncedAt: oura.lastSyncedAt,
      awards: [],
      progress: [],
    };
  }

  const today = todayIso();
  const now = nowIso();
  store().oura = { ...oura, lastSyncedAt: now, lastSyncError: null };
  return {
    ran: true,
    changed: false,
    from: addDays(today, -14),
    to: addDays(today, 1),
    records: [...store().days.values()].filter((d) => d.day >= addDays(today, -14)).length,
    written: 0,
    voided: 0,
    historyFrom: oura.historyFrom,
    historyDone: oura.historyDone,
    lastSyncedAt: now,
    awards: [],
    progress: [],
  };
}

/** Set or clear a workout's note, refusing what the API refuses, in its words. */
export function mockSetWorkoutNote(id: string, text: string): WorkoutRow {
  const note = text.trim();
  if (/[\p{Cc}\u2028\u2029]/u.test(note)) {
    throw new Error("A workout note is one line: take out the line break.");
  }
  if (note.length > NOTE_MAX) {
    throw new Error(`A workout note is up to ${NOTE_MAX} characters; that one is ${note.length}.`);
  }
  const i = store().workouts.findIndex((w) => w.id === id);
  if (i < 0) {
    throw new Error(`LifeOS holds no workout '${id}': it may have been deleted in the Oura app, or not synced yet.`);
  }
  const workout = store().workouts[i];
  if ((workout.note ?? "") !== note) {
    store().workouts[i] = { ...workout, note: note || null, noteAt: note ? nowIso() : null };
  }
  return { ...store().workouts[i] };
}
