import { describe, expect, it } from "vitest";
import {
  availableActions,
  foldTimer,
  formatClock,
  formatTracked,
  liveSeconds,
  parseEstimateSeconds,
  type TimerEvent,
} from "@/lib/timer";

const task = { id: "t1", urn: "urn:bsk:task:t1", title: "Long run" };
const at = (minutes: number) => new Date(Date.UTC(2026, 9, 20, 9, 0, 0) + minutes * 60_000).toISOString();
const ev = (action: TimerEvent["action"], minutes: number, subjectId = "t1"): TimerEvent => ({
  subjectId,
  action,
  occurredAt: at(minutes),
});

describe("foldTimer", () => {
  it("is null for a task that was never timed", () => {
    expect(foldTimer([ev("start", 0, "other")], task)).toBeNull();
  });

  it("sums every running segment across pauses and sessions", () => {
    const timer = foldTimer(
      [ev("start", 0), ev("pause", 60), ev("resume", 70), ev("stop", 100), ev("start", 200), ev("stop", 210)],
      task,
    )!;
    expect(timer.state).toBe("stopped");
    expect(timer.totalSeconds).toBe(100 * 60);
    expect(timer.sessionSeconds).toBe(10 * 60);
    expect(timer.sessions).toBe(2);
    expect(timer.sessionStartedAt).toBe(at(200));
    expect(timer.runningSince).toBeNull();
  });

  it("leaves the running segment out and reports when it began", () => {
    const timer = foldTimer([ev("start", 0), ev("pause", 30), ev("resume", 40)], task)!;
    expect(timer.state).toBe("running");
    expect(timer.totalSeconds).toBe(30 * 60);
    expect(timer.runningSince).toBe(at(40));
    expect(liveSeconds(timer, Date.parse(at(45)))).toEqual({ total: 35 * 60, session: 35 * 60 });
  });

  it("reads a pause as paused, frozen at its time", () => {
    const timer = foldTimer([ev("start", 0), ev("pause", 15)], task)!;
    expect(timer.state).toBe("paused");
    expect(liveSeconds(timer, Date.parse(at(90))).total).toBe(15 * 60);
  });
});

describe("liveSeconds", () => {
  it("never goes negative when the client clock is behind", () => {
    const timer = foldTimer([ev("start", 10)], task)!;
    expect(liveSeconds(timer, Date.parse(at(5)))).toEqual({ total: 0, session: 0 });
  });
});

describe("availableActions", () => {
  it("offers only what the kernel accepts", () => {
    expect(availableActions("stopped")).toEqual(["start"]);
    expect(availableActions("running")).toEqual(["pause", "stop", "complete"]);
    expect(availableActions("paused")).toEqual(["resume", "stop", "complete"]);
    // A closed task: nothing to start, and a timer left on it can only be stopped.
    expect(availableActions("stopped", true)).toEqual([]);
    expect(availableActions("running", true)).toEqual(["stop"]);
    expect(availableActions("paused", true)).toEqual(["stop"]);
  });
});

describe("formatting", () => {
  it("formats the stopwatch", () => {
    expect(formatClock(0)).toBe("0:00");
    expect(formatClock(65)).toBe("1:05");
    expect(formatClock(3600 + 2 * 60 + 3)).toBe("1:02:03");
  });

  it("formats recorded time", () => {
    expect(formatTracked(45)).toBe("45s");
    expect(formatTracked(25 * 60)).toBe("25m");
    expect(formatTracked(80 * 60)).toBe("1h 20m");
    expect(formatTracked(3 * 3600)).toBe("3h");
  });
});

describe("parseEstimateSeconds", () => {
  it.each([
    ["90m", 5400],
    ["1h 30m", 5400],
    ["1.5h", 5400],
    ["45 min", 2700],
    ["2 hours", 7200],
    ["30", 1800],
  ])("reads %s", (text, seconds) => {
    expect(parseEstimateSeconds(text)).toBe(seconds);
  });

  it.each([null, "", "soon", "1h then some"])("rejects %s", (text) => {
    expect(parseEstimateSeconds(text)).toBeNull();
  });
});
