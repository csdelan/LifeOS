import type { InboxItem } from "@/lib/production-ui-types";

export function deriveTitle(text: string, max = 80): string {
  const line = text.trim().split(/\n/)[0] ?? "";
  if (!line) return "Untitled";
  if (line.length <= max) return line;
  return `${line.slice(0, max - 1).trimEnd()}…`;
}

/** Inbox list / preview heading. Voice with no transcript is labeled for later work. */
export function inboxItemTitle(item: InboxItem): string {
  if (item.needsTranscription && !item.eventContent?.trim() && !item.subjectTitle) {
    return "Needs transcription";
  }
  if (item.subjectTitle) return item.subjectTitle;
  const text = item.eventContent?.trim();
  if (text) return deriveTitle(text);
  if (item.attachment?.filename) return item.attachment.filename;
  if (item.eventKind === "voice") return "Voice note";
  return "Untitled capture";
}

export function inboxItemBody(item: InboxItem): string {
  if (item.eventContent?.trim()) return item.eventContent;
  if (item.needsTranscription) return "Needs transcription.";
  if (item.attachment?.filename) return item.attachment.filename;
  return "A flagged subject. Organize it, then decide — tagging does not clear the Inbox.";
}
