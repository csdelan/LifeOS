import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import type { InboxItem } from "@/lib/production-ui-types";
import { inboxItemTitle } from "@/lib/capture";
import { formatTimestamp } from "@/lib/dates";
import { inboxKindLabel, isResolved, resolutionLabel } from "@/lib/inbox";

/**
 * The item's real tags with add / remove. Each write is confirmed by the server
 * before the toast, and a failure says so and keeps the draft for a retry.
 */
/**
 * A row of the Inbox list: what it is, its kind, when it was captured or resolved, its tags and
 * any attachment. A plain list, where a table library weighed most of the page's code (code
 * review CR100).
 */
export function InboxRow({ item }: { item: InboxItem }) {
  const tags = item.tags ?? [];
  return (
    <div className="min-w-0 flex-1">
      <p className="truncate text-sm font-medium">{inboxItemTitle(item)}</p>
      <p className="truncate text-[0.6875rem] text-muted-foreground">
        {inboxKindLabel(item)}
        {" · "}
        {isResolved(item) ? `${resolutionLabel(item)} ${formatTimestamp(item.resolvedAt)}` : formatTimestamp(item.triagedAt)}
        {tags.length ? ` · ${tags.map((t) => `#${t}`).join(" ")}` : null}
      </p>
      {item.attachment ? <AttachmentChip artifact={item.attachment} compact /> : null}
    </div>
  );
}
