/**
 * What the Inbox says about an item, for its list, its preview and its writes: the reference a
 * write takes, its kind, and how a resolved one was resolved.
 */
import {
  defaultStatus,
  typeLabel,
  type InboxItem,
  type ResolvedInboxItem,
} from "@/lib/production-ui-types";

/** The reference a write takes: a capture's event, else the subject's URN. */
export function inboxRef(item: InboxItem): string {
  return item.itemKind === "event" ? item.itemId : (item.subjectUrn ?? item.itemId);
}

/** The kind under its title: the subject's type, or what the capture was. */
export function inboxKindLabel(item: InboxItem): string {
  if (item.itemKind === "subject") return typeLabel(item.subjectType ?? "Idea");
  if (item.needsTranscription) return "voice · needs transcription";
  if (item.eventKind === "voice") return "voice";
  if (item.attachment) return "document";
  return item.eventKind ?? "note";
}

export function isResolved(item: InboxItem): item is ResolvedInboxItem {
  return "resolution" in item;
}

export function resolutionLabel(item: ResolvedInboxItem): string {
  switch (item.resolution) {
    case "promoted":
      return "Promoted";
    case "status":
      return item.subjectStatus ? `Dropped · ${item.subjectStatus}` : "Dropped";
    case "dropped":
      return "Dropped";
    default:
      // `dismissed`, and the retired `filed` state it replaced.
      return "Dismissed";
  }
}

/** The status Back to Inbox re-opens a finished subject to, or "" when none is needed. */
export function reopenStatus(item: ResolvedInboxItem): string {
  return item.subjectStatusTerminal && item.subjectType ? defaultStatus(item.subjectType) : "";
}
