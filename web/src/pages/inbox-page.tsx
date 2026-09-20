import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { flexRender } from "@tanstack/react-table";
import {
  getCoreRowModel,
  useLegacyTable,
  type LegacyColumnDef,
} from "@tanstack/react-table/legacy";
import { toast } from "sonner";
import { InboxIcon } from "lucide-react";
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
import { CREATABLE_TYPES, typeLabel } from "@/lib/production-ui-types";
import type { InboxItem, SubjectType } from "@/lib/production-ui-types";
import { useInbox, useSubjects, useWrites } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { saveView } from "@/lib/view-state";

export function InboxPage() {
  const { data, isLoading, isError } = useInbox();
  const search = useSearch({ from: "/inbox" });
  const navigate = useNavigate({ from: "/inbox" });
  const selectedId = (search.item as string | undefined) ?? data?.[0]?.itemId;
  const selected = data?.find((i) => i.itemId === selectedId) ?? data?.[0];
  const writes = useWrites();
  const [dropOpen, setDropOpen] = useState(false);
  const [focusAction, setFocusAction] = useState<"promote" | "relate" | "dismiss" | "drop">(
    "promote",
  );

  useEffect(() => {
    if (selected) saveView("inbox", { item: selected.itemId });
  }, [selected]);

  function select(id: string) {
    void navigate({ search: { item: id } });
  }

  function move(delta: number) {
    if (!data?.length) return;
    const idx = Math.max(0, data.findIndex((i) => i.itemId === selected?.itemId));
    const next = data[Math.min(data.length - 1, Math.max(0, idx + delta))];
    if (next) select(next.itemId);
  }

  async function afterResolve() {
    if (!data || !selected) return;
    const remaining = data.filter((i) => i.itemId !== selected.itemId);
    if (remaining[0]) select(remaining[0].itemId);
  }

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
      if (e.key === "ArrowDown") {
        e.preventDefault();
        move(1);
      }
      if (e.key === "ArrowUp") {
        e.preventDefault();
        move(-1);
      }
      if (e.key === "Enter") {
        e.preventDefault();
        if (focusAction === "drop") setDropOpen(true);
        if (focusAction === "dismiss" && selected) {
          void writes.dismiss(selected.itemId).then(afterResolve);
        }
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
        cell: ({ row }) => (
          <div>
            <p className="truncate text-sm font-medium">
              {row.original.subjectTitle ??
                row.original.eventContent ??
                "Untitled capture"}
            </p>
            <p className="text-[0.6875rem] text-muted-foreground">
              {row.original.itemKind === "subject"
                ? typeLabel(row.original.subjectType ?? "Idea")
                : row.original.eventKind}
              {" · "}
              {formatTimestamp(row.original.triagedAt)}
            </p>
          </div>
        ),
      },
    ],
    [],
  );

  const table = useLegacyTable({
    data: data ?? [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    getRowId: (row) => row.itemId,
  });

  return (
    <div className="flex h-full min-h-0">
      <div className="flex w-[22rem] shrink-0 flex-col border-r">
        <header className="border-b px-4 py-3">
          <h1 className="font-heading text-2xl">Inbox</h1>
          <p className="text-xs text-muted-foreground">
            ↑↓ select · Enter acts · Drop always confirms
          </p>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto">
          {isLoading ? <div className="p-3"><ListSkeleton /></div> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={InboxIcon}
              title="Inbox failed to load"
              description="This is not Inbox Zero."
            />
          ) : null}
          {!isLoading && data?.length === 0 ? (
            <EmptyState
              icon={InboxIcon}
              title="Inbox is clear"
              description="Nothing needs a decision. That's the point."
            />
          ) : null}
          <table className="w-full table-fixed">
            <tbody>
              {table.getRowModel().rows.map((row) => (
                <tr
                  key={row.id}
                  onClick={() => select(row.original.itemId)}
                  className={cn(
                    "cursor-pointer border-b border-border/60",
                    selected?.itemId === row.original.itemId
                      ? "bg-primary/8"
                      : "hover:bg-muted/50",
                  )}
                >
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id} className="px-4 py-2.5">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <div className="min-w-0 flex-1 overflow-y-auto p-6">
        {selected ? (
          <InboxPreview
            item={selected}
            focusAction={focusAction}
            setFocusAction={setFocusAction}
            onDismiss={() => void writes.dismiss(selected.itemId).then(afterResolve)}
            onDrop={() => setDropOpen(true)}
            onPromoted={afterResolve}
            onRelated={afterResolve}
          />
        ) : (
          <EmptyState title="Select an item" description="Preview and triage open here." />
        )}
      </div>
      <AlertDialog open={dropOpen} onOpenChange={setDropOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Drop this item?</AlertDialogTitle>
            <AlertDialogDescription>
              Drop records that it's nothing — a terminal status for subjects, dismiss for
              events. This is not a delete.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                if (selected) void writes.drop(selected.itemId).then(afterResolve);
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

function InboxPreview({
  item,
  focusAction,
  setFocusAction,
  onDismiss,
  onDrop,
  onPromoted,
  onRelated,
}: {
  item: InboxItem;
  focusAction: "promote" | "relate" | "dismiss" | "drop";
  setFocusAction: (a: "promote" | "relate" | "dismiss" | "drop") => void;
  onDismiss: () => void;
  onDrop: () => void;
  onPromoted: () => void;
  onRelated: () => void;
}) {
  const writes = useWrites();
  const { data: subjects } = useSubjects();
  const [promoteType, setPromoteType] = useState<SubjectType>("Task");
  const [promoteTitle, setPromoteTitle] = useState(
    item.subjectTitle ?? item.eventContent?.slice(0, 80) ?? "",
  );
  const [relateTo, setRelateTo] = useState("");
  const [tag, setTag] = useState("");

  useEffect(() => {
    setPromoteTitle(item.subjectTitle ?? item.eventContent?.slice(0, 80) ?? "");
  }, [item]);

  return (
    <div className="mx-auto max-w-xl space-y-6">
      <div>
        {item.subjectType ? <TypeBadge type={item.subjectType} /> : (
          <span className="text-xs text-muted-foreground">{item.eventKind} capture</span>
        )}
        <h2 className="mt-1 font-heading text-3xl leading-tight">
          {item.subjectTitle ?? "Captured note"}
        </h2>
        <p className="mt-3 whitespace-pre-wrap text-sm leading-relaxed">
          {item.eventContent ??
            "A flagged subject. Organize it, then decide — tagging does not clear the Inbox."}
        </p>
        <p className="mt-2 text-xs text-muted-foreground">
          Flagged {formatTimestamp(item.triagedAt)}
        </p>
      </div>

      <section className="rounded-xl border border-dashed border-border p-4">
        <p className="type-scale-section text-muted-foreground">Tags</p>
        <p className="mt-1 text-xs text-muted-foreground">
          Classify here. Relating is a separate action.
        </p>
        <form
          className="mt-2 flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (!tag.trim()) return;
            void writes.tag(item.itemId, { add: [tag.trim()] });
            toast.message("Tagged — still in Inbox");
            setTag("");
          }}
        >
          <Input value={tag} onChange={(e) => setTag(e.target.value)} placeholder="Add tag" />
          <Button type="submit" variant="outline" size="sm">
            Tag
          </Button>
        </form>
        <div className="mt-2">
          <Tag>needs-triage</Tag>
        </div>
      </section>

      <div className="grid gap-3">
        <ActionCard
          active={focusAction === "promote"}
          onFocus={() => setFocusAction("promote")}
          title="Promote"
          hint="Turn this into a durable subject"
        >
          <div className="flex flex-wrap gap-2">
            <Select value={promoteType} onValueChange={(v) => setPromoteType(v as SubjectType)}>
              <SelectTrigger className="w-40">
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
            />
            <Button
              size="sm"
              onClick={() =>
                void writes
                  .promote(item.itemId, promoteType, promoteTitle || "Untitled")
                  .then(onPromoted)
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
              <SelectTrigger className="flex-1">
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
              onClick={() => void writes.relate(item.itemId, relateTo).then(onRelated)}
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
          Dismiss clears attention only. Drop moves a subject to a terminal status.
        </p>
      </div>
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
