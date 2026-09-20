import { beforeEach, describe, expect, it } from "vitest";
import {
  addEdge,
  adhereHabit,
  byId,
  captureDocument,
  captureVoice,
  createSubject,
  dropInboxItem,
  filesForSubject,
  getState,
  relateEvent,
  resetStore,
  setStatus,
  setTags,
} from "@/lib/mock/store";

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
