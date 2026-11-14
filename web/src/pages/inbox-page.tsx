import { useEffect, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { toast } from "sonner";
import { InboxIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
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
import type { InboxItem, ResolvedInboxItem } from "@/lib/production-ui-types";
import { RESOLVED_MAX, RESOLVED_PAGE, useInbox, useResolvedInbox, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { keyBelongsToFocus } from "@/lib/keyboard";
import { saveView } from "@/lib/view-state";
import { useIsDesktop } from "@/lib/media-query";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { useWriteAction } from "@/lib/write-action";
import { ReadFailed } from "@/components/primitives/read-state";
import { InboxPreview, ResolvedPreview } from "@/components/inbox/inbox-preview";
import { InboxRow } from "@/components/inbox/inbox-row";
import { inboxRef, isResolved, reopenStatus } from "@/lib/inbox";

export function InboxPage() {
  const search = useSearch({ from: "/inbox" });
  const navigate = useNavigate({ from: "/inbox" });
  const resolvedView = search.view === "resolved";
  const open = useInbox();
  // Resolved reads the newest few and Load older reads further back (code review CR76).
  const [resolvedLimit, setResolvedLimit] = useState(RESOLVED_PAGE);
  const resolved = useResolvedInbox(resolvedView, resolvedLimit);
  const olderLeft = resolvedView && !!resolved.data && resolved.data.length >= resolvedLimit && resolvedLimit < RESOLVED_MAX;
  const shownRead = resolvedView ? resolved : open;
  const { data, isLoading } = shownRead;
  const rows: InboxItem[] | undefined = data;
  const isDesktop = useIsDesktop();
  const selectedId = search.item ?? (isDesktop ? rows?.[0]?.itemId : undefined);
  const selected = rows?.find((i) => i.itemId === selectedId);
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });
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
    await reported.run(() => writes.dismiss(ref), {
      fallback: "Dismiss failed. It is still in your Inbox.",
      onDone: () => {
        toast.success("Dismissed", {
          description: "Find it again under Resolved.",
          // Long enough to reach for Undo; the default 4s is gone before you look.
          duration: 10_000,
          action: {
            label: "Undo",
            onClick: () => void reported.run(() => writes.flag(ref), { fallback: "Undo failed." }),
          },
        });
        afterResolve(item);
      },
    });
  }

  async function drop(item: InboxItem) {
    await reported.run(() => writes.drop(inboxRef(item)), {
      fallback: "Drop failed. It is still in your Inbox.",
      onDone: () => afterResolve(item),
    });
  }

  async function backToInbox(item: ResolvedInboxItem) {
    const ref = inboxRef(item);
    const reopen = reopenStatus(item);
    await reported.run(
      async () => {
        // The Inbox hides finished subjects, so a bare re-flag would surface nothing.
        if (reopen) await writes.setStatus(ref, reopen);
        await writes.flag(ref);
      },
      { fallback: "Couldn't return it to the Inbox.", onDone: () => void navigate({ search: { item: item.itemId } }) },
    );
  }

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      // Enter on a button, and any key in a field, a Select or a dialog, is theirs.
      if (keyBelongsToFocus(e) || dropOpen) return;
      if (e.key === "ArrowDown") {
        e.preventDefault();
        move(1);
      }
      if (e.key === "ArrowUp") {
        e.preventDefault();
        move(-1);
      }
      if (e.key === "Enter" && !resolvedView) {
        if (focusAction === "drop") {
          e.preventDefault();
          setDropOpen(true);
        }
        if (focusAction === "dismiss" && selected) {
          e.preventDefault();
          void dismiss(selected);
        }
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  // Keyed by the item, so what was typed for one (a tag, a title, a subject to relate it to) is
  // never offered for the next (code review CR91).
  function preview(item: InboxItem, onBack?: () => void) {
    return isResolved(item) ? (
      <ResolvedPreview key={item.itemId} item={item} onBackToInbox={() => backToInbox(item)} onBack={onBack} />
    ) : (
      <InboxPreview
        key={item.itemId}
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
          <ReadFailed
            read={shownRead}
            what={resolvedView ? "Resolved items" : "Inbox"}
            notEmpty={resolvedView ? "an empty list" : "Inbox Zero"}
            size="page"
            icon={InboxIcon}
          />
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
          <ul aria-label={resolvedView ? "Resolved items" : "Inbox items"} className="divide-y divide-border/60">
            {(rows ?? []).map((item) => (
              <li key={item.itemId}>
                <button
                  type="button"
                  onClick={() => select(item.itemId)}
                  aria-current={selected?.itemId === item.itemId ? "true" : undefined}
                  className={cn(
                    "flex min-h-11 w-full cursor-pointer px-4 py-3 text-left focus-visible:ring-2 focus-visible:ring-ring/50",
                    selected?.itemId === item.itemId ? "bg-primary/8" : "hover:bg-muted/50",
                  )}
                >
                  <InboxRow item={item} />
                </button>
              </li>
            ))}
          </ul>
          {olderLeft ? (
            <div className="p-3">
              <Button
                variant="outline"
                size="sm"
                className="w-full"
                disabled={resolved.isPlaceholderData}
                onClick={() => setResolvedLimit((n) => Math.min(n + RESOLVED_PAGE, RESOLVED_MAX))}
              >
                {resolved.isPlaceholderData ? "Loading older…" : "Load older"}
              </Button>
            </div>
          ) : null}
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
