/**
 * Task timer helpers (TASKS-2): the fold the mock store shares with the kernel's
 * `bsk.v_task_timer`, live elapsed time, and duration formatting / parsing.
 */
import { useEffect, useState } from "react";
import type { TaskTimer, TimerAction, TimerState } from "@/lib/production-ui-types";

/** One recorded timer action. The kernel keeps these as append-only activity events. */
export interface TimerEvent {
  subjectId: string;
  action: Exclude<TimerAction, "complete">;
  occurredAt: string; // ISO timestamp
}

/**
 * Fold one task's timer actions, exactly as `bsk.v_task_timer` does: every start /
 * resume runs until the next action, a running segment is left out of the totals,
 * and sessions are counted by their starts. Returns null when there are no actions.
 */
export function foldTimer(
  events: TimerEvent[],
  subject: { id: string; urn: string; title: string },
): TaskTimer | null {
  const ordered = events
    .filter((e) => e.subjectId === subject.id)
    .sort((a, b) => a.occurredAt.localeCompare(b.occurredAt));
  if (ordered.length === 0) return null;

  let total = 0;
  let session = 0;
  let sessions = 0;
  let sessionStartedAt: string | null = null;
  ordered.forEach((e, i) => {
    if (e.action === "start") {
      sessions += 1;
      session = 0;
      sessionStartedAt = e.occurredAt;
    }
    const next = ordered[i + 1];
    if ((e.action === "start" || e.action === "resume") && next) {
      const seconds = (Date.parse(next.occurredAt) - Date.parse(e.occurredAt)) / 1000;
      total += seconds;
      session += seconds;
    }
  });

  const last = ordered[ordered.length - 1];
  const state: TimerState =
    last.action === "start" || last.action === "resume"
      ? "running"
      : last.action === "pause"
        ? "paused"
        : "stopped";
  return {
    subjectId: subject.id,
    subjectUrn: subject.urn,
    subjectTitle: subject.title,
    state,
    runningSince: state === "running" ? last.occurredAt : null,
    totalSeconds: Math.floor(total),
    sessionSeconds: Math.floor(session),
    sessionStartedAt,
    sessions,
    lastChangedAt: last.occurredAt,
  };
}

/**
 * The live figures: the stored seconds plus the running segment, if any. Clamped so
 * a client clock that runs behind the server never shows negative time.
 */
export function liveSeconds(
  timer: TaskTimer | null | undefined,
  nowMs: number,
): { total: number; session: number } {
  if (!timer) return { total: 0, session: 0 };
  const running =
    timer.state === "running" && timer.runningSince
      ? Math.max(0, Math.floor((nowMs - Date.parse(timer.runningSince)) / 1000))
      : 0;
  return { total: timer.totalSeconds + running, session: timer.sessionSeconds + running };
}

/** Which actions fit the timer's state (the kernel refuses the rest). */
export function availableActions(state: TimerState): TimerAction[] {
  switch (state) {
    case "running":
      return ["pause", "stop", "complete"];
    case "paused":
      return ["resume", "stop", "complete"];
    default:
      return ["start"];
  }
}

/** Stopwatch format: `m:ss` under an hour, `h:mm:ss` from there. */
export function formatClock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = String(s % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${sec}` : `${m}:${sec}`;
}

/** Summary format for recorded time: `45s`, `25m`, `1h 20m`, `3h`. */
export function formatTracked(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  if (s < 60) return `${s}s`;
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (h === 0) return `${m}m`;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

/**
 * Read a free-text estimate (the Task's `estimated_duration`) as seconds: `90m`,
 * `1h`, `1h 30m`, `1.5h`, `45 min`, `2 hours`, or a bare number of minutes. Returns
 * null when the text is not a duration.
 */
export function parseEstimateSeconds(text?: string | null): number | null {
  const raw = (text ?? "").trim().toLowerCase();
  if (!raw) return null;
  if (/^\d+(\.\d+)?$/.test(raw)) return Math.round(Number(raw) * 60);
  const unit = /(\d+(?:\.\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes)\b/g;
  let seconds = 0;
  let matched = "";
  for (const part of raw.matchAll(unit)) {
    const value = Number(part[1]);
    seconds += part[2].startsWith("h") ? value * 3600 : value * 60;
    matched += part[0];
  }
  // Every non-space character must belong to a recognized part.
  if (!matched || matched.replace(/\s/g, "").length !== raw.replace(/\s/g, "").length) return null;
  return Math.round(seconds);
}

/** The current time, re-rendering every `intervalMs` while `active`. */
export function useNow(active: boolean, intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    // Catch up straight away (the clock may have been idle), then tick.
    const first = window.setTimeout(() => setNow(Date.now()), 0);
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => {
      window.clearTimeout(first);
      window.clearInterval(id);
    };
  }, [active, intervalMs]);
  return now;
}
