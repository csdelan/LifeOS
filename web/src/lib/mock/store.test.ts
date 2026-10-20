import { beforeEach, describe, expect, it } from "vitest";
import {
  addEdge,
  adhereHabit,
  applyTimer,
  byId,
  captureDocument,
  captureVoice,
  createSubject,
  dropInboxItem,
  filesForSubject,
  flagItem,
  getState,
  relateEvent,
  removeInbox,
  renameSubject,
  renameTag,
  resetStore,
  setStatus,
  setTags,
  timerFor,
} from "@/lib/mock/store";
import { mockReads } from "@/lib/mock/api";
import { createMockWriteClient } from "@/lib/mock/write-client";

describe("mock store reducers", () => {
  beforeEach(() => {
    resetStore();
  });

  it("creates a subject and links it to a parent", () => {
    const created = createSubject({
      type: "Task",
      title: "Hardening pass",
      parent: "proj-web",
      relation: "serves",
    });
    expect(byId(created.id)?.title).toBe("Hardening pass");
    expect(
      getState().edges.some(
        (e) => e.fromId === created.id && e.toId === "proj-web" && e.relation === "serves",
      ),
    ).toBe(true);
  });

  it("moves status only by setStatus", () => {
    setStatus("task-longrun", "In progress");
    expect(byId("task-longrun")?.status).toBe("In progress");
    expect(getState().history["task-longrun"]?.[0]?.status).toBe("In progress");
  });

  it("records adherence and recomputes the streak", () => {
    adhereHabit("habit-mobility", "followed");
    const habit = getState().habits.find((h) => h.id === "habit-mobility");
    expect(habit?.lastState).toBe("followed");
    expect(habit?.currentStreak).toBeGreaterThanOrEqual(1);
  });

  it("drops a subject to a terminal status and clears Inbox", () => {
    const before = getState().inbox.find((i) => i.itemId === "inbox-2");
    expect(before?.subjectType).toBe("Idea");
    dropInboxItem("inbox-2");
    expect(getState().inbox.some((i) => i.itemId === "inbox-2")).toBe(false);
    const idea = getState().subjects.find((s) => s.title === "Family cookbook");
    expect(idea?.status).toBe("Rejected");
  });

  it("drops an event by dismissing it, without inventing a subject status", () => {
    const subjects = getState().subjects.length;
    dropInboxItem("inbox-1");
    expect(getState().inbox.some((i) => i.itemId === "inbox-1")).toBe(false);
    expect(getState().subjects.length).toBe(subjects);
  });

  it("keeps a dismissed item under Resolved and restores it on flag", async () => {
    setTags("inbox-1", ["desk"]);
    removeInbox("inbox-1");
    expect(getState().inbox.some((i) => i.itemId === "inbox-1")).toBe(false);

    const resolved = await mockReads.inboxResolved();
    const row = resolved.find((r) => r.itemId === "inbox-1");
    expect(row?.resolution).toBe("dismissed");
    expect(row?.tags).toEqual(["desk"]);

    flagItem("inbox-1");
    const inbox = await mockReads.inbox();
    expect(inbox.find((i) => i.itemId === "inbox-1")?.eventContent).toMatch(/standing desk/);
    expect((await mockReads.inboxResolved()).some((r) => r.itemId === "inbox-1")).toBe(false);
  });

  it("lists a dropped subject as resolved by its terminal status", async () => {
    dropInboxItem("inbox-2");
    const row = (await mockReads.inboxResolved()).find((r) => r.itemId === "inbox-2");
    expect(row?.resolution).toBe("status");
    expect(row?.subjectStatus).toBe("Rejected");
    expect(row?.subjectStatusTerminal).toBe(true);
  });

  it("shows a subject inbox row's own tags", async () => {
    const urn = getState().inbox.find((i) => i.itemId === "inbox-2")?.subjectUrn;
    setTags(urn!, ["Garden"]);
    const row = (await mockReads.inbox()).find((i) => i.itemId === "inbox-2");
    expect(row?.tags).toEqual(["family", "food", "garden"]);
  });

  it("carries a capture's tags onto the subject it is promoted to", async () => {
    setTags("inbox-5", ["family"]);
    const created = await createMockWriteClient().promote("inbox-5", "Task", "Ask Maya about dinner");
    expect(getState().tagsByItem[created.id]).toEqual(["family"]);
    expect(
      (await mockReads.inboxResolved()).find((r) => r.itemId === "inbox-5")?.resolution,
    ).toBe("promoted");
  });

  it("does not resolve Inbox when tagging", () => {
    const count = getState().inbox.length;
    setTags("inbox-2", ["parked"]);
    expect(getState().inbox.length).toBe(count);
    expect(getState().inbox.some((i) => i.itemId === "inbox-2")).toBe(true);
  });

  it("addEdge is idempotent for the same triple", () => {
    const n = getState().edges.length;
    addEdge("task-longrun", "serves", "proj-base");
    addEdge("task-longrun", "serves", "proj-base");
    expect(getState().edges.length).toBe(n);
  });

  it("captures a document into Inbox with artifact metadata", () => {
    const file = new File(["statement"], "comcast.pdf", { type: "application/pdf" });
    const result = captureDocument({ file, description: "Disputed charge", type: "Note" });
    expect(result.kind).toBe("note");
    const item = getState().inbox.find((i) => i.itemId === result.eventId);
    expect(item?.attachment?.filename).toBe("comcast.pdf");
    expect(item?.eventContent).toBe("Disputed charge");
  });

  it("retains voice audio when the transcript is empty", () => {
    const blob = new Blob(["aaaa"], { type: "audio/webm" });
    const result = captureVoice({
      audioBlob: blob,
      transcript: "  ",
      type: "Note",
      keepAudio: false,
      durationSeconds: 9,
    });
    expect(result.needsTranscription).toBe(true);
    expect(result.artifactId).toBeTruthy();
    const item = getState().inbox.find((i) => i.itemId === result.eventId);
    expect(item?.needsTranscription).toBe(true);
    expect(item?.attachment?.hasBytes).toBe(true);
  });

  it("discards voice audio after a successful transcript-only submit", () => {
    const blob = new Blob(["aaaa"], { type: "audio/webm" });
    const result = captureVoice({
      audioBlob: blob,
      transcript: "Park the phone in the kitchen.",
      type: "Note",
      keepAudio: false,
    });
    expect(result.artifactId ?? null).toBeNull();
    const item = getState().inbox.find((i) => i.itemId === result.eventId);
    expect(item?.eventKind).toBe("voice");
    expect(item?.eventContent).toContain("Park the phone");
    expect(item?.attachment ?? null).toBeNull();
  });

  it("seeds a filed document onto the base-build project", () => {
    const files = filesForSubject("proj-base");
    expect(files.some((f) => f.filename === "week-4-base-build.txt")).toBe(true);
    expect(getState().inbox.some((i) => i.itemId === "inbox-doc-1")).toBe(true);
    expect(getState().inbox.some((i) => i.needsTranscription)).toBe(true);
  });

  it("files a related document onto the subject's Files list", () => {
    const file = new File(["notes"], "week-4.txt", { type: "text/plain" });
    const created = captureDocument({ file, description: "Week 4 notes", type: "Note" });
    relateEvent(created.eventId, "proj-base");
    const files = filesForSubject("proj-base");
    expect(files.some((f) => f.filename === "week-4.txt")).toBe(true);
    expect(getState().inbox.some((i) => i.itemId === created.eventId)).toBe(false);
  });
});

describe("mock rename parity", () => {
  beforeEach(() => {
    resetStore();
  });

  it("renames a habit everywhere the mock copied its name, keeping the urn", () => {
    const habit = createSubject({ type: "Habit", title: "Stretch" });
    const result = renameSubject(habit.id, "  Morning   stretch ");

    expect(result).toMatchObject({ changed: true, previousTitle: "Stretch", title: "Morning stretch" });
    expect(byId(habit.id)?.title).toBe("Morning stretch");
    expect(byId(habit.id)?.urn).toBe(habit.urn);
    expect(getState().details[habit.id]?.title).toBe("Morning stretch");
    expect(getState().habits.find((h) => h.id === habit.id)?.name).toBe("Morning stretch");
    expect(getState().occurrences.find((o) => o.habitId === habit.id)?.habitName).toBe("Morning stretch");
    expect(getState().inbox.some((i) => i.subjectTitle === "Stretch")).toBe(false);
  });

  it("shows a renamed Area's new name on the items filed under it", () => {
    const area = createSubject({ type: "Area", title: "Hobbies" });
    const task = createSubject({ type: "Task", title: "Restring guitar", area: area.urn });
    renameSubject(area.id, "Music");

    expect(byId(task.id)?.areaName).toBe("Music");
    expect(getState().details[task.id]?.areaName).toBe("Music");
    expect(getState().areas.find((a) => a.id === area.id)?.name).toBe("Music");
  });

  it("refuses to give a Problem another Problem's title", () => {
    const problem = createSubject({ type: "Problem", title: "Why am I tired?" });
    expect(() => renameSubject(problem.id, "How do I sleep through the night?")).toThrow(/already exists/);
    expect(byId(problem.id)?.title).toBe("Why am I tired?");
  });

  it("carries a series rename to upcoming occurrences that still have the old title", () => {
    const series = createSubject({ type: "Appointment", title: "Dentist" });
    const past = createSubject({ type: "Appointment", title: "Dentist", attrs: { series: series.urn, date: "2020-01-01" } });
    const upcoming = createSubject({ type: "Appointment", title: "Dentist", attrs: { series: series.urn, date: "2999-01-01" } });
    const custom = createSubject({ type: "Appointment", title: "Dentist (kids)", attrs: { series: series.urn, date: "2999-02-01" } });

    const result = renameSubject(series.id, "Dr. Lee – cleaning");

    expect(result.occurrencesRenamed).toBe(1);
    expect(byId(upcoming.id)?.title).toBe("Dr. Lee – cleaning");
    expect(byId(past.id)?.title).toBe("Dentist");
    expect(byId(custom.id)?.title).toBe("Dentist (kids)");
  });

  it("renames a tag on every item and merges where the new name already exists", () => {
    const before = Object.values(getState().tagsByItem);
    const both = before.filter((t) => t.includes("sleep") && t.includes("health")).length;
    const onlySleep = before.filter((t) => t.includes("sleep") && !t.includes("health")).length;

    const result = renameTag("Sleep", "health");

    expect(result).toEqual({ from: "sleep", to: "health", renamed: onlySleep, merged: both });
    expect(Object.values(getState().tagsByItem).some((t) => t.includes("sleep"))).toBe(false);
    expect(getState().tagsByItem["prob-sleep"]).toEqual(["health"]);
    expect(byId("prob-sleep")?.tags).toBe("health");
    expect(() => renameTag("sleep", "rest")).toThrow(/No item is tagged/);
  });
});

describe("mock task timer", () => {
  beforeEach(() => {
    resetStore();
  });

  it("starts, pauses, resumes and stops on the same rules as the kernel", () => {
    expect(applyTimer("task-shoes", "start").timer.state).toBe("running");
    expect(() => applyTimer("task-shoes", "start")).toThrow(/already running/);
    expect(applyTimer("task-shoes", "pause").timer.state).toBe("paused");
    expect(() => applyTimer("task-shoes", "pause")).toThrow(/not running|paused/);
    expect(applyTimer("task-shoes", "resume").timer.state).toBe("running");
    const stopped = applyTimer("task-shoes", "stop").timer;
    expect(stopped.state).toBe("stopped");
    expect(stopped.sessions).toBe(1);
    expect(() => applyTimer("task-shoes", "resume")).toThrow(/start it instead/);
  });

  it("keeps one timer running: starting another pauses the first", () => {
    applyTimer("task-shoes", "start");
    const result = applyTimer("task-phone", "start");
    expect(result.paused.map((p) => p.id)).toEqual(["task-shoes"]);
    expect(timerFor("task-shoes")?.state).toBe("paused");
    expect(timerFor("task-phone")?.state).toBe("running");
  });

  it("completes: stops the timer and marks the Task Completed", () => {
    applyTimer("task-shoes", "start");
    const result = applyTimer("task-shoes", "complete");
    expect(result.completed).toBe(true);
    expect(result.timer.state).toBe("stopped");
    expect(byId("task-shoes")?.status).toBe("Completed");
  });

  it("keeps the timer on Tasks only", () => {
    expect(() => applyTimer("goal-lifeos", "start")).toThrow(/not a Task/);
  });

  it("reads the seeded session on the long run", () => {
    const timer = timerFor("task-longrun");
    expect(timer?.state).toBe("stopped");
    expect(timer?.totalSeconds).toBe(25 * 60);
    expect(timerFor("task-shoes")).toBeNull();
  });
});
