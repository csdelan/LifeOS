import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { flexRender } from "@tanstack/react-table";
import {
  getCoreRowModel,
  useLegacyTable,
  type LegacyColumnDef,
} from "@tanstack/react-table/legacy";
import { toast } from "sonner";
import { InboxIcon, Undo2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Tag } from "@/components/primitives/tag";
import { TypeBadge } from "@/components/primitives/type-badge";
import { AttachmentChip } from "@/components/artifacts/attachment-chip";
import { Badge } from "@/components/ui/badge";
import { CREATABLE_TYPES, defaultStatus, typeLabel } from "@/lib/production-ui-types";
import type { InboxItem, ResolvedInboxItem, SubjectType } from "@/lib/production-ui-types";
import { inboxItemBody, inboxItemTitle } from "@/lib/capture";
import {
  useInbox,
  useResolvedInbox,
  useSubjects,
  useTagUniverse,
  useWrites,
} from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { saveView } from "@/lib/view-state";
import { useIsDesktop } from "@/lib/media-query";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";

function inboxRef(item: InboxItem): string {
  return item.itemKind === "event" ? item.itemId : (item.subjectUrn ?? item.itemId);
}

function inboxKindLabel(item: InboxItem): string {
  if (item.itemKind === "subject") return typeLabel(item.subjectType ?? "Idea");
  if (item.needsTranscription) return "voice · needs transcription";
  if (item.eventKind === "voice") return "voice";
  if (item.attachment) return "document";
  return item.eventKind ?? "note";
}

function isResolved(item: InboxItem): item is ResolvedInboxItem {
  return "resolution" in item;
}

function resolutionLabel(item: ResolvedInboxItem): string {
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
function reopenStatus(item: ResolvedInboxItem): string {
  return item.subjectStatusTerminal && item.subjectType ? defaultStatus(item.subjectType) : "";
}

function errorText(e: unknown, fallback: string): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

export function InboxPage() {
  const search = useSearch({ from: "/inbox" });
  const navigate = useNavigate({ from: "/inbox" });
  const resolvedView = search.view === "resolved";
  const open = useInbox();
  const resolved = useResolvedInbox(resolvedView);
  const { data, isLoading, isError } = resolvedView ? resolved : open;
  const rows: InboxItem[] | undefined = data;
  const isDesktop = useIsDesktop();
  const selectedId = search.item ?? (isDesktop ? rows?.[0]?.itemId : undefined);
  const selected = rows?.find((i) => i.itemId === selectedId);
  const writes = useWrites();
  const [dropOpen, setDropOpen] = useState(false);
  const [focusAction, setFocusAction] = useState<"promote" | "relate" | "dismiss" | "drop">(
    "promote",
  );

  useEffect(() => {
    if (selected && !resolvedView) saveView("inbox", { item: selected.itemId });
  }, [selected, resolvedView]);

  function select(id: string | undefined) {
    void navigate({ search: (prev) => ({ ...prev, item: id }) });
  }

  function setView(view: "open" | "resolved") {
    void navigate({ search: view === "resolved" ? { view: "resolved" } : {} });
  }

  function move(delta: number) {
    if (!rows?.length) return;
    const idx = Math.max(0, rows.findIndex((i) => i.itemId === selected?.itemId));
    const next = rows[Math.min(rows.length - 1, Math.max(0, idx + delta))];
    if (next) select(next.itemId);
  }

  /** After an item leaves the list, select its neighbour (the list is pre-removal). */
  function afterResolve(item: InboxItem) {
    if (!rows) return;
    const idx = rows.findIndex((i) => i.itemId === item.itemId);
    const remaining = rows.filter((i) => i.itemId !== item.itemId);
    select(remaining[Math.min(Math.max(idx, 0), remaining.length - 1)]?.itemId);
  }

  async function dismiss(item: InboxItem) {
    const ref = inboxRef(item);
    try {
      await writes.dismiss(ref);
    } catch (e) {
      toast.error(errorText(e, "Dismiss failed. It is still in your Inbox."));
      return;
    }
    toast.success("Dismissed", {
      description: "Find it again under Resolved.",
      // Long enough to reach for Undo; the default 4s is gone before you look.
      duration: 10_000,
      action: {
        label: "Undo",
        onClick: () => {
          writes.flag(ref).catch((e: unknown) => toast.error(errorText(e, "Undo failed.")));
        },
      },
    });
    afterResolve(item);
  }

  async function drop(item: InboxItem) {
    try {
      await writes.drop(inboxRef(item));
    } catch (e) {
      toast.error(errorText(e, "Drop failed. It is still in your Inbox."));
      return;
    }
    afterResolve(item);
  }

  async function backToInbox(item: ResolvedInboxItem) {
    const ref = inboxRef(item);
    const reopen = reopenStatus(item);
    try {
      // The Inbox hides finished subjects, so a bare re-flag would surface nothing.
      if (reopen) await writes.setStatus(ref, reopen);
      await writes.flag(ref);
    } catch (e) {
      toast.error(errorText(e, "Couldn't return it to the Inbox."));
      return;
    }
    void navigate({ search: { item: item.itemId } });
  }

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
      if (dropOpen) return;
      if (e.key === "ArrowDown") {
        e.preventDefault();
        move(1);
      }
      if (e.key === "ArrowUp") {
        e.preventDefault();
        move(-1);
      }
      if (e.key === "Enter" && !resolvedView) {
        e.preventDefault();
        if (focusAction === "drop") setDropOpen(true);
        if (focusAction === "dismiss" && selected) void dismiss(selected);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  const columns = useMemo<LegacyColumnDef<InboxItem>[]>(
    () => [
      {
        accessorKey: "preview",
        header: "Item",
        cell: ({ row }) => {
          const item = row.original;
          const tags = item.tags ?? [];
          return (
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">{inboxItemTitle(item)}</p>
              <p className="truncate text-[0.6875rem] text-muted-foreground">
                {inboxKindLabel(item)}
                {" · "}
                {isResolved(item)
                  ? `${resolutionLabel(item)} ${formatTimestamp(item.resolvedAt)}`
                  : formatTimestamp(item.triagedAt)}
                {tags.length ? ` · ${tags.map((t) => `#${t}`).join(" ")}` : null}
              </p>
              {item.attachment ? <AttachmentChip artifact={item.attachment} compact /> : null}
            </div>
          );
        },
      },
    ],
    [],
  );

  const table = useLegacyTable({
    data: rows ?? [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    getRowId: (row) => row.itemId,
  });

  function preview(item: InboxItem, onBack?: () => void) {
    return isResolved(item) ? (
      <ResolvedPreview item={item} onBackToInbox={() => backToInbox(item)} onBack={onBack} />
    ) : (
      <InboxPreview
        item={item}
        focusAction={focusAction}
        setFocusAction={setFocusAction}
        onDismiss={() => void dismiss(item)}
        onDrop={() => setDropOpen(true)}
        onResolved={() => afterResolve(item)}
        onBack={onBack}
      />
    );
  }

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 w-full shrink-0 flex-col border-r md:w-[22rem]">
        <header className="border-b px-4 py-3">
          <h1 className="font-heading text-2xl">Inbox</h1>
          <p className="text-xs text-muted-foreground">
            {resolvedView
              ? "Dismissed, promoted and dropped items. Nothing here is deleted."
              : "↑↓ select · Enter acts · Drop always confirms"}
          </p>
          <div
            role="group"
            aria-label="Inbox view"
            className="mt-2 inline-flex rounded-lg bg-muted p-[3px]"
          >
            {(["open", "resolved"] as const).map((view) => {
              const active = (view === "resolved") === resolvedView;
              return (
                <button
                  key={view}
                  type="button"
                  aria-pressed={active}
                  onClick={() => setView(view)}
                  className={cn(
                    "min-h-7 rounded-md px-3 text-xs font-medium transition-colors focus-visible:ring-2 focus-visible:ring-ring/50",
                    active
                      ? "bg-background text-foreground shadow-sm"
                      : "text-muted-foreground hover:text-foreground",
                  )}
                >
                  {view === "open"
                    ? `Open${open.data ? ` · ${open.data.length}` : ""}`
                    : "Resolved"}
                </button>
              );
            })}
          </div>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {isLoading ? <div className="p-3"><ListSkeleton /></div> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={InboxIcon}
              title={resolvedView ? "Resolved items failed to load" : "Inbox failed to load"}
              description="This is not Inbox Zero."
            />
          ) : null}
          {!isLoading && rows?.length === 0 ? (
            resolvedView ? (
              <EmptyState
                icon={InboxIcon}
                title="Nothing resolved yet"
                description="Dismissed, promoted and dropped items land here, so you can bring them back."
              />
            ) : (
              <EmptyState
                icon={InboxIcon}
                title="Inbox is clear"
                description="Nothing needs a decision. That's the point."
              />
            )
          ) : null}
          <ul className="divide-y divide-border/60">
            {table.getRowModel().rows.map((row) => (
              <li key={row.id}>
                <button
                  type="button"
                  onClick={() => select(row.original.itemId)}
                  aria-current={selected?.itemId === row.original.itemId ? "true" : undefined}
                  className={cn(
                    "flex min-h-11 w-full cursor-pointer px-4 py-3 text-left focus-visible:ring-2 focus-visible:ring-ring/50",
                    selected?.itemId === row.original.itemId
                      ? "bg-primary/8"
                      : "hover:bg-muted/50",
                  )}
                >
                  {row.getVisibleCells().map((cell) => (
                    <div key={cell.id} className="min-w-0 flex-1">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </div>
                  ))}
                </button>
              </li>
            ))}
          </ul>
        </div>
      </div>
      <div className="hidden min-w-0 flex-1 overflow-y-auto p-6 md:block">
        {selected ? (
          preview(selected)
        ) : (
          <EmptyState title="Select an item" description="Preview and triage open here." />
        )}
      </div>
      <Sheet
        open={!isDesktop && !!search.item && !!selected}
        onOpenChange={(next) => {
          if (!next) select(undefined);
        }}
      >
        <SheetContent
          side="right"
          showCloseButton={false}
          className="h-full w-full gap-0 overflow-y-auto p-6 sm:max-w-none data-[side=right]:w-full data-[side=right]:sm:max-w-none"
        >
          <SheetHeader className="sr-only">
            <SheetTitle>Inbox item</SheetTitle>
            <SheetDescription>Triage this capture. Close returns to the list.</SheetDescription>
          </SheetHeader>
          {selected ? preview(selected, () => select(undefined)) : null}
        </SheetContent>
      </Sheet>
      <AlertDialog open={dropOpen} onOpenChange={setDropOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Drop this item?</AlertDialogTitle>
            <AlertDialogDescription>
              Drop records that it's nothing — a terminal status for subjects, dismiss for
              events. This is not a delete: it stays under Resolved.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                if (selected) void drop(selected);
              }}
            >
              Drop
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

/**
 * The item's real tags with add / remove. Each write is confirmed by the server
 * before the toast, and a failure says so and keeps the draft for a retry.
 */
function TagEditor({ item, hint }: { item: InboxItem; hint: string }) {
  const writes = useWrites();
  const { data: universe } = useTagUniverse();
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState(false);
  const tags = item.tags ?? [];
  const ref = inboxRef(item);
  const listId = `inbox-tag-universe-${item.itemId}`;

  async function apply(opts: { add?: string[]; remove?: string[] }, done: string) {
    setPending(true);
    try {
      await writes.tag(ref, opts);
      toast.success(done);
      return true;
    } catch (e) {
      toast.error(errorText(e, "Couldn't save the tag."));
      return false;
    } finally {
      setPending(false);
    }
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
      <form
        className="mt-2 flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const t = draft.trim();
          if (!t || pending) return;
          void apply({ add: [t] }, `Tagged #${t.toLowerCase()}`).then((ok) => {
            if (ok) setDraft("");
          });
        }}
      >
        <Input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Add tag"
          aria-label="Add tag"
          list={listId}
        />
        <datalist id={listId}>
          {(universe ?? []).map((u) => (
            <option key={u.tag} value={u.tag} />
          ))}
        </datalist>
        <Button type="submit" variant="outline" size="sm" disabled={pending || !draft.trim()}>
          {pending ? "Saving…" : "Tag"}
        </Button>
      </form>
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

function InboxPreview({
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
  const [promoteTitle, setPromoteTitle] = useState(inboxItemTitle(item));
  const [relateTo, setRelateTo] = useState("");

  useEffect(() => {
    setPromoteTitle(inboxItemTitle(item));
  }, [item]);

  async function resolveWith(write: () => Promise<unknown>, failure: string) {
    try {
      await write();
    } catch (e) {
      toast.error(errorText(e, failure));
      return;
    }
    onResolved();
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
            <Select value={relateTo} onValueChange={setRelateTo}>
              <SelectTrigger className="flex-1" aria-label="Relate to subject">
                <SelectValue placeholder="Subject…" />
              </SelectTrigger>
              <SelectContent>
                {(subjects ?? []).slice(0, 30).map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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

function ResolvedPreview({
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
