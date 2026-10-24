import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  applyAttrs,
  applyTimer,
  byId,
  createSubject,
  earnStatus,
  getState,
  mockSetBounty,
  mockSettleTasks,
  mockTaskSeries,
  mockTaskSeriesHistory,
  renameSubject,
  resetStore,
  setRecurrence,
  setTags,
} from "@/lib/mock/store";
import { mockReads } from "@/lib/mock/api";
import { addDays } from "@/lib/dates";

/** Wednesday 21 October 2026, at noon. */
const WED = "2026-10-21";

function series(title: string) {
  return createSubject({ type: "Task", title }).id;
}

function attrs(id: string): Record<string, unknown> {
  return JSON.parse(getState().details[id]?.attributes ?? "{}") as Record<string, unknown>;
}

describe("repeating Tasks in the mock store (TASKS-3, D16)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 9, 21, 12));
    resetStore();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("makes a Task with a pattern a series, listed only when asked, with its first repetition", async () => {
    const id = series("Water plants");
    const made = setRecurrence(id, { freq: "weekly", on: ["wed"] }, WED);
    expect(made).toHaveLength(1);
    const repetition = byId(made[0].id)!;
    expect(repetition.due).toBe(WED);
    expect(attrs(repetition.id)).toMatchObject({ series: byId(id)!.urn, occurrence: WED, due: WED });

    const tasks = await mockReads.listSubjects({ type: "Task" });
    expect(tasks.some((s) => s.id === id)).toBe(false);
    const row = tasks.find((s) => s.id === repetition.id)!;
    expect(row).toMatchObject({ isSeries: false, seriesId: id, occurrence: WED });
    expect(row.repeat).toContain('"weekly"');
    const withSeries = await mockReads.listSubjects({ type: "Task", includeSeries: true });
    expect(withSeries.find((s) => s.id === id)?.isSeries).toBe(true);

    const detail = await mockReads.getSubject(repetition.id);
    expect(detail?.repeating).toMatchObject({ isSeries: false, seriesId: id, occurrence: WED, openCount: 1 });
    expect(detail?.bountyFromSeries).toBe(true);
    expect((await mockReads.getSubject(id))?.repeating?.isSeries).toBe(true);
  });

  it("pays the series' price for each repetition and makes the next when one is done", () => {
    const id = series("Take out recycling");
    mockSetBounty({ subject: id, trigger: "completed", amounts: { freedom: 0, xp: 7 } });
    const [first] = setRecurrence(id, { freq: "weekly", on: ["wed"] }, WED);

    const done = earnStatus(first.id, "Completed");
    expect(done.awards.map((a) => a.amounts)).toEqual([{ xp: 7 }]);
    const history = mockTaskSeriesHistory(id)!;
    expect(history.repetitions.map((r) => r.occurrence)).toEqual([addDays(WED, 7), WED]);
    expect(history.series).toMatchObject({ completedCount: 1, openCount: 1, nextDue: addDays(WED, 7) });

    const second = history.repetitions[0];
    expect(earnStatus(second.id, "Completed").awards.map((a) => a.amounts)).toEqual([{ xp: 7 }]);
  });

  it("closes a missed one without paying, and the next is made", () => {
    const id = series("Mow lawn");
    const [first] = setRecurrence(id, { freq: "weekly", on: ["wed"] }, WED);
    expect(earnStatus(first.id, "Missed").awards).toEqual([]);
    expect(mockTaskSeriesHistory(id)!.series).toMatchObject({ missedCount: 1, completedCount: 0, openCount: 1 });
  });

  it("keeps a late calendar date open beside the next", () => {
    const id = series("Pay the sitter");
    const made = setRecurrence(id, { freq: "weekly", on: ["wed"] }, addDays(WED, -14));
    expect(made.map((m) => byId(m.id)?.due)).toEqual([addDays(WED, -14), addDays(WED, -7), WED]);
  });

  it("dates once each week Monday to Sunday", () => {
    const id = series("Clean the car");
    const [week] = setRecurrence(id, { freq: "each", period: "week" }, WED);
    expect(byId(week.id)).toMatchObject({ scheduled: "2026-10-19", due: "2026-10-25" });
  });

  it("counts every N from the day the last one was done", () => {
    const id = series("Clean fridge");
    const [first] = setRecurrence(id, { freq: "interval", every: 30, unit: "days", from: "completion" }, addDays(WED, -5));
    expect(byId(first.id)?.due).toBe(addDays(WED, -5));
    earnStatus(first.id, "Completed");
    expect(mockTaskSeriesHistory(id)!.series.nextDue).toBe(addDays(WED, 30));
  });

  it("keeps a series Not started or Cancelled: Cancelled stops it, Not started restarts it from today", () => {
    const id = series("Water lawn");
    const [first] = setRecurrence(id, { freq: "daily" }, addDays(WED, -10));
    expect(() => earnStatus(id, "Completed")).toThrow(/repeating task/);

    earnStatus(id, "Cancelled");
    const before = mockTaskSeriesHistory(id)!.repetitions.length;
    earnStatus(first.id, "Completed");
    expect(mockTaskSeriesHistory(id)!.repetitions).toHaveLength(before);
    expect(mockTaskSeries().find((s) => s.id === id)?.stopped).toBe(true);

    earnStatus(id, "Not started");
    expect(attrs(id).start).toBe(WED);
    expect(mockTaskSeries().find((s) => s.id === id)?.stopped).toBe(false);
  });

  it("catches up the dates that passed while nothing ran", () => {
    const id = series("Feed fish");
    setRecurrence(id, { freq: "daily" }, WED);
    vi.setSystemTime(new Date(2026, 9, 23, 9));
    const settled = mockSettleTasks();
    // The seeded trash's Wednesday passed too, so it has next week's.
    expect(settled.made.filter((m) => m.title === "Feed fish").map((m) => byId(m.id)?.due)).toEqual([
      "2026-10-22",
      "2026-10-23",
    ]);
    expect(settled.made.some((m) => m.title === "Take out trash")).toBe(true);
    expect(mockSettleTasks().made).toEqual([]);
  });

  it("carries a series edit to open repetitions still holding the old value", () => {
    const id = series("Sweep porch");
    applyAttrs(id, { description: "Front steps too" });
    const made = setRecurrence(id, { freq: "daily" }, addDays(WED, -1));
    const [kept, followed] = made.map((m) => m.id);
    applyAttrs(kept, { description: "Did the back instead" });

    applyAttrs(id, { description: "Front and side" });
    expect(attrs(followed).description).toBe("Front and side");
    expect(attrs(kept).description).toBe("Did the back instead");

    setTags(id, ["chores"]);
    expect(getState().tagsByItem[followed]).toContain("chores");
    expect(renameSubject(id, "Sweep the porch").occurrencesRenamed).toBe(2);
  });

  it("refuses what the kernel refuses", () => {
    const id = series("Floss");
    expect(() => setRecurrence(id, { freq: "trigger", cue: "after dinner" })).toThrow(/needs dates/);
    const [first] = setRecurrence(id, { freq: "daily" }, WED);
    expect(() => setRecurrence(id, null)).toThrow(/Cancelled/);
    expect(() => setRecurrence(first.id, { freq: "weekly", on: ["mon"] })).toThrow(/one repetition/);
    expect(() => applyTimer(id, "start")).toThrow(/time one of its repetitions/);
    expect(() => setRecurrence("habit-mobility", { freq: "each", period: "week" })).toThrow(/for Tasks/);
  });

  it("seeds four repeating tasks with a history to read", () => {
    const rows = mockTaskSeries();
    expect(rows.map((r) => r.title)).toEqual(["Clean bathroom", "Clean fridge", "Pay rent", "Take out trash"]);
    const trash = rows.find((r) => r.id === "task-trash")!;
    expect(trash).toMatchObject({ missedCount: 1, openCount: 1, nextDue: WED, stopped: false });
    expect(trash.completedCount).toBeGreaterThanOrEqual(3);
    expect(rows.find((r) => r.id === "task-fridge")?.nextDue).toBe(addDays(WED, 12));
    expect(rows.find((r) => r.id === "task-bathroom")).toMatchObject({ openCount: 1, nextDue: "2026-10-25" });
    expect(rows.find((r) => r.id === "task-rent")).toMatchObject({ openCount: 1, nextDue: "2026-11-01" });
  });
});
