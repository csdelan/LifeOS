import { toast } from "sonner";
import type {
  CreatedSubject,
  LifeOsWriteClient,
  NewSubjectRequest,
} from "@/lib/production-ui-types";
import { inferChildRelation } from "@/lib/production-ui-types";
import { extensionForMime, sha256Hex } from "@/lib/artifacts";
import {
  addEdge,
  adhereHabit,
  appendJournal,
  applyAttrs,
  archiveSubject,
  byRef,
  captureDocument,
  captureNote,
  captureVoice,
  completeReviewDoc,
  createSubject,
  dropInboxItem,
  flagItem,
  involvePerson,
  relateEvent,
  removeInbox,
  saveReviewBody,
  setHabitRecurrence,
  setStatus,
  setTags,
} from "@/lib/mock/store";
import { getState } from "@/lib/mock/store";

const wait = (ms = 90) => new Promise((r) => setTimeout(r, ms));

function notify(message: string) {
  toast.success(message);
}

export function createMockWriteClient(onChange?: () => void): LifeOsWriteClient {
  const bump = () => onChange?.();

  return {
    async newSubject(req: NewSubjectRequest): Promise<CreatedSubject> {
      await wait();
      let relation = req.relation;
      if (req.parent && !relation) {
        const parent = byRef(req.parent);
        if (parent) relation = inferChildRelation(req.type, parent.type);
      }
      const created = createSubject({ ...req, relation });
      notify(`Created ${created.title}`);
      bump();
      return created;
    },

    async promote(source, type, title) {
      await wait();
      const inbox = getState().inbox.find((i) => i.itemId === source);
      const created = createSubject({ type, title });
      if (inbox) removeInbox(source);
      notify(`Promoted to ${type}`);
      bump();
      return created;
    },

    async setAttributes(subject, attrs) {
      await wait();
      applyAttrs(byRef(subject)?.id ?? subject, attrs);
      notify("Saved");
      bump();
    },

    async setStatus(subject, status) {
      await wait();
      setStatus(subject, status);
      notify(`Status → ${status}`);
      bump();
    },

    async archive(subject) {
      await wait();
      archiveSubject(subject, true);
      notify("Archived");
      bump();
    },

    async restore(subject) {
      await wait();
      archiveSubject(subject, false);
      notify("Restored");
      bump();
    },

    async tag(item, opts) {
      await wait();
      setTags(item, opts.add, opts.remove);
      bump();
    },

    async link(from, relation, to) {
      await wait();
      const a = byRef(from);
      const b = byRef(to);
      if (a && b) addEdge(a.id, relation, b.id);
      notify("Linked");
      bump();
    },

    async relate(eventId, subject, as) {
      await wait();
      relateEvent(eventId, subject, as);
      const target = byRef(subject);
      notify(target ? `Related to ${target.title}` : "Related");
      bump();
    },

    async flag(item) {
      await wait();
      flagItem(item);
      notify("Flagged to Inbox");
      bump();
    },

    async dismiss(item) {
      await wait();
      removeInbox(item);
      notify("Dismissed");
      bump();
    },

    async drop(item) {
      await wait();
      dropInboxItem(item);
      notify("Dropped");
      bump();
    },

    async adhere(habit, state, opts) {
      await wait();
      adhereHabit(habit, state, opts?.on, opts?.note);
      notify(
        state === "followed"
          ? "Marked followed"
          : state === "partial"
            ? "Partial credit"
            : "Marked missed",
      );
      bump();
    },

    async recur(habit, spec) {
      await wait();
      setHabitRecurrence(habit, spec);
      notify("Recurrence saved");
      bump();
    },

    async involve(subject, person, role, remove) {
      await wait();
      involvePerson(subject, person, role, remove);
      bump();
    },

    async appendJournal(subject, text) {
      await wait();
      appendJournal(subject, text);
      notify("Journal appended");
      bump();
    },

    async capture(text) {
      await wait();
      captureNote(text);
      notify("Captured to Inbox");
      bump();
    },

    async captureDocument(req) {
      await wait();
      // TODO(api): multipart upload to the kernel's attach write path (`bsk attach`).
      const sha256 = (await sha256Hex(req.file)) ?? null;
      const result = captureDocument({ ...req, sha256 });
      notify("Captured to Inbox");
      bump();
      return result;
    },

    async captureVoice(req) {
      await wait();
      // TODO(api): multipart upload to the kernel's voice write path (`bsk voice`).
      const keep = req.keepAudio || !req.transcript.trim();
      const sha256 = keep ? (await sha256Hex(req.audioBlob)) ?? null : null;
      const ext = extensionForMime(req.audioBlob.type || "audio/webm");
      const result = captureVoice({
        ...req,
        sha256,
        contentType: req.audioBlob.type || "audio/webm",
        filename: `voice.${ext}`,
      });
      notify("Captured to Inbox");
      bump();
      return result;
    },

    async saveReview(id, body) {
      await wait();
      saveReviewBody(id, body);
      notify("Review saved");
      bump();
    },

    async completeReview(id) {
      await wait();
      completeReviewDoc(id);
      notify("Review complete");
      bump();
    },
  };
}
