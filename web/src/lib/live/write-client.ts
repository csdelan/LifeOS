import { toast } from "sonner";
import { api, unwrap } from "@/lib/live/http";
import type {
  CreatedSubject,
  LifeOsWriteClient,
} from "@/lib/production-ui-types";

function notify(message: string) {
  toast.success(message);
}

export function createLiveWriteClient(): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = (await unwrap(api().POST("/api/subjects", { body: req }))) as unknown as CreatedSubject;
      notify(`Created ${created.title}`);
      return created;
    },

    async promote(source, type, title) {
      const created = (await unwrap(
        api().POST("/api/promote", { body: { source, type, title } }),
      )) as unknown as CreatedSubject;
      notify(`Promoted to ${type}`);
      return created;
    },

    async setAttributes(subject, attrs) {
      await unwrap(api().POST("/api/attributes", { body: { subject, attrs } }));
      notify("Saved");
    },

    async setStatus(subject, status) {
      await unwrap(api().POST("/api/status", { body: { subject, status } }));
      notify(`Status → ${status}`);
    },

    async archive(subject) {
      await unwrap(api().POST("/api/archive", { body: { subject } }));
      notify("Archived");
    },

    async restore(subject) {
      await unwrap(api().POST("/api/restore", { body: { subject } }));
      notify("Restored");
    },

    async tag(item, opts) {
      await unwrap(
        api().POST("/api/tag", {
          body: { item, add: opts.add, remove: opts.remove },
        }),
      );
    },

    async link(from, relation, to) {
      await unwrap(api().POST("/api/link", { body: { from, relation, to } }));
      notify("Linked");
    },

    async relate(eventId, subject, as) {
      await unwrap(
        api().POST("/api/relate", {
          body: { eventId, subject, as },
        }),
      );
      notify("Related");
    },

    async flag(item) {
      await unwrap(api().POST("/api/flag", { body: { item } }));
      notify("Flagged to Inbox");
    },

    async dismiss(item) {
      await unwrap(api().POST("/api/dismiss", { body: { item } }));
      notify("Dismissed");
    },

    async drop(item) {
      await unwrap(api().POST("/api/drop", { body: { item } }));
      notify("Dropped");
    },

    async adhere(habit, state, opts) {
      await unwrap(
        api().POST("/api/adhere", {
          body: { habit, state, on: opts?.on, note: opts?.note },
        }),
      );
      notify(
        state === "followed"
          ? "Marked followed"
          : state === "partial"
            ? "Partial credit"
            : "Marked missed",
      );
    },

    async involve(subject, person, role, remove) {
      await unwrap(
        api().POST("/api/involve", { body: { subject, person, role, remove } }),
      );
    },

    async appendJournal(subject, text) {
      await unwrap(api().POST("/api/journal", { body: { subject, text } }));
      notify("Journal appended");
    },

    async capture(text) {
      await unwrap(api().POST("/api/capture", { body: { text } }));
      notify("Captured to Inbox");
    },
  };
}
