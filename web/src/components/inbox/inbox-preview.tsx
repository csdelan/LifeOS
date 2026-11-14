import { useState } from "react";
import { toast } from "sonner";
import { Undo2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { SubjectCombobox } from "@/components/subject-combobox";
import { Tag } from "@/components/primitives/tag";
import { TagInput } from "@/components/primitives/tag-input";
import { TypeBadge } from "@/components/primitives/type-badge";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import { Badge } from "@/components/ui/badge";
import { CREATABLE_TYPES, typeLabel } from "@/lib/production-ui-types";
import type { InboxItem, ResolvedInboxItem, SubjectType } from "@/lib/production-ui-types";
import { inboxItemBody, inboxItemTitle } from "@/lib/capture";
import { useSubjects, useWrites } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";
import { splitTags } from "@/lib/tag-suggest";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";
import { inboxKindLabel, inboxRef, reopenStatus, resolutionLabel } from "@/lib/inbox";

function TagEditor({ item, hint }: { item: InboxItem; hint: string }) {
  const writes = useWrites();
  const [draft, setDraft] = useState("");
  const tagging = useWriteAction({ report: "toast", fallback: "Couldn't save the tag." });
  const pending = tagging.pending;
  const tags = item.tags ?? [];
  const ref = inboxRef(item);

  function apply(opts: { add?: string[]; remove?: string[] }, done: string, after?: () => void) {
    return tagging.run(
      () => writes.tag(ref, opts),
      () => {
        toast.success(done);
        after?.();
      },
    );
  }

  function add(added: string[]) {
    if (added.length === 0 || pending) return;
    const label = added.map((t) => `#${t}`).join(", ");
    void apply({ add: added }, `Tagged ${label}`, () => setDraft(""));
  }

  return (
    <section className="rounded-xl border border-dashed border-border p-4">
      <p className="type-scale-section text-muted-foreground">Tags</p>
      <p className="mt-1 text-xs text-muted-foreground">{hint}</p>
      <div className="mt-2 flex flex-wrap gap-1.5">
        {tags.length === 0 ? (
          <p className="text-sm text-muted-foreground">No tags yet.</p>
        ) : (
          tags.map((t) => (
            <Tag
              key={t}
              onRemove={pending ? undefined : () => void apply({ remove: [t] }, `Removed #${t}`)}
            >
              {t}
            </Tag>
          ))
        )}
      </div>
      <div className="mt-2 flex gap-2">
        <TagInput
          value={draft}
          onValueChange={setDraft}
          exclude={tags}
          onCommit={add}
          disabled={pending}
          placeholder="Add tag"
          aria-label="Add tag"
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={pending || !splitTags(draft).length}
          onClick={() => add(splitTags(draft))}
        >
          {pending ? "Saving…" : "Tag"}
        </Button>
      </div>
    </section>
  );
}

function ItemHeading({ item }: { item: InboxItem }) {
  return (
    <div>
      <div className="flex flex-wrap items-center gap-2">
        {item.subjectType ? <TypeBadge type={item.subjectType} /> : (
          <span className="text-xs text-muted-foreground">{inboxKindLabel(item)} capture</span>
        )}
        {item.needsTranscription ? (
          <Badge variant="outline">Needs transcription</Badge>
        ) : null}
      </div>
      <h2 className="mt-1 font-heading text-3xl leading-tight">
        {inboxItemTitle(item)}
      </h2>
      <p className="mt-3 whitespace-pre-wrap text-sm leading-relaxed">
        {inboxItemBody(item)}
      </p>
      {item.attachment ? (
        <div className="mt-3">
          <AttachmentChip artifact={item.attachment} />
        </div>
      ) : null}
    </div>
  );
}

export function InboxPreview({
  item,
  focusAction,
  setFocusAction,
  onDismiss,
  onDrop,
  onResolved,
  onBack,
}: {
  item: InboxItem;
  focusAction: "promote" | "relate" | "dismiss" | "drop";
  setFocusAction: (a: "promote" | "relate" | "dismiss" | "drop") => void;
  onDismiss: () => void;
  onDrop: () => void;
  onResolved: () => void;
  onBack?: () => void;
}) {
  const writes = useWrites();
  const { data: subjects } = useSubjects();
  const [promoteType, setPromoteType] = useState<SubjectType>("Task");
  // Starts from the item's own title; a new read of the same item (it was tagged) keeps what's typed.
  const [promoteTitle, setPromoteTitle] = useState(() => inboxItemTitle(item));
  const [relateTo, setRelateTo] = useState("");
  const resolving = useWriteAction({ report: "toast" });

  async function resolveWith(write: () => Promise<unknown>, failure: string) {
    await resolving.run(write, { fallback: failure, onDone: () => onResolved() });
  }

  return (
    <div className="mx-auto max-w-xl space-y-6">
      {onBack ? (
        <Button variant="ghost" className="-ml-2" onClick={onBack}>
          Back to list
        </Button>
      ) : null}
      <div>
        <ItemHeading item={item} />
        <p className="mt-2 text-xs text-muted-foreground">
          Flagged {formatTimestamp(item.triagedAt)}
        </p>
      </div>

      <TagEditor item={item} hint="Classify here. Tagging keeps it in the Inbox; relating is a separate action." />

      <div className="grid gap-3">
        <ActionCard
          active={focusAction === "promote"}
          onFocus={() => setFocusAction("promote")}
          title="Promote"
          hint="Turn this into a durable subject. Its tags come along."
        >
          <div className="flex flex-wrap gap-2">
            <Select value={promoteType} onValueChange={(v) => setPromoteType(v as SubjectType)}>
              <SelectTrigger className="w-40" aria-label="Promote to type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CREATABLE_TYPES.map((t) => (
                  <SelectItem key={t} value={t}>
                    {typeLabel(t)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input
              value={promoteTitle}
              onChange={(e) => setPromoteTitle(e.target.value)}
              className="flex-1"
              aria-label="Promote title"
            />
            <Button
              size="sm"
              onClick={() =>
                void resolveWith(
                  () => writes.promote(inboxRef(item), promoteType, promoteTitle || "Untitled"),
                  "Promote failed.",
                )
              }
            >
              Promote
            </Button>
          </div>
        </ActionCard>

        <ActionCard
          active={focusAction === "relate"}
          onFocus={() => setFocusAction("relate")}
          title="Relate"
          hint="File against an existing subject — still a resolution"
        >
          <div className="flex gap-2">
            <SubjectCombobox
              className="flex-1"
              options={subjects ?? []}
              value={relateTo}
              onChange={setRelateTo}
              ariaLabel="Relate to subject"
              placeholder="Subject…"
            />
            <Button
              size="sm"
              variant="outline"
              disabled={!relateTo}
              onClick={() =>
                void resolveWith(() => writes.relate(item.itemId, relateTo), "Relate failed.")
              }
            >
              Relate
            </Button>
          </div>
        </ActionCard>

        <div className="flex gap-2">
          <Button
            variant={focusAction === "dismiss" ? "default" : "outline"}
            className="flex-1"
            onFocus={() => setFocusAction("dismiss")}
            onClick={onDismiss}
          >
            Dismiss
          </Button>
          <Button
            variant={focusAction === "drop" ? "destructive" : "outline"}
            className="flex-1"
            onFocus={() => setFocusAction("drop")}
            onClick={onDrop}
          >
            Drop
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          Dismiss clears attention only. Drop moves a subject to a terminal status. Either way
          it stays under Resolved.
        </p>
      </div>
    </div>
  );
}

export function ResolvedPreview({
  item,
  onBackToInbox,
  onBack,
}: {
  item: ResolvedInboxItem;
  onBackToInbox: () => Promise<void>;
  onBack?: () => void;
}) {
  const [pending, setPending] = useState(false);
  const reopen = reopenStatus(item);

  return (
    <div className="mx-auto max-w-xl space-y-6">
      {onBack ? (
        <Button variant="ghost" className="-ml-2" onClick={onBack}>
          Back to list
        </Button>
      ) : null}
      <div>
        <ItemHeading item={item} />
        <p className="mt-2 text-xs text-muted-foreground">
          {resolutionLabel(item)} {formatTimestamp(item.resolvedAt)}
        </p>
      </div>

      <TagEditor item={item} hint="Tagging here doesn't change where it lives." />

      <section className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <p className="text-sm font-medium">Back to Inbox</p>
        <p className="mb-3 text-xs text-muted-foreground">
          {reopen
            ? `It's ${item.subjectStatus}, and the Inbox hides finished items, so this reopens it as ${reopen} first.`
            : "Puts it back in the queue to triage again."}
        </p>
        <Button
          disabled={pending}
          onClick={() => {
            setPending(true);
            void onBackToInbox().finally(() => setPending(false));
          }}
        >
          <Undo2Icon /> {reopen ? `Reopen as ${reopen} and return to Inbox` : "Back to Inbox"}
        </Button>
      </section>
    </div>
  );
}

function ActionCard({
  title,
  hint,
  active,
  onFocus,
  children,
}: {
  title: string;
  hint: string;
  active: boolean;
  onFocus: () => void;
  children: React.ReactNode;
}) {
  return (
    <section
      onFocus={onFocus}
      className={cn(
        "rounded-xl bg-card p-4 ring-1 ring-foreground/10",
        active && "ring-primary/40",
      )}
    >
      <p className="text-sm font-medium">{title}</p>
      <p className="mb-2 text-xs text-muted-foreground">{hint}</p>
      {children}
    </section>
  );
}
