import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { ReadFailed } from "@/components/primitives/read-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { StatusPill } from "@/components/primitives/status-pill";
import { useServerPages } from "@/components/primitives/use-paged";
import { ViaAgent } from "@/components/primitives/via-agent";
import { useHistory } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";
import { changeText, fieldLabel } from "@/lib/history";
import type { FieldChange, SubjectType } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** How many changes the tab reads at first, and how many more each Show older (code review CR121). */
export const HISTORY_PAGE = 50;

/**
 * An item's history, newest first: its status changes, and each rename or field change from and to
 * (agent-access plan §5.6 step 2.4). A change an agent made says which ("via Claude").
 */
export function HistoryTab({ subjectId, type }: { subjectId: string; type?: SubjectType }) {
  const pages = useServerPages(subjectId, HISTORY_PAGE);
  const history = useHistory(subjectId, pages.limit);
  const { data } = history;
  if (history.isLoading) return <ListSkeleton rows={3} />;
  if (history.isError && !data?.length) return <ReadFailed read={history} what="The history" />;
  if (!data?.length) {
    return (
      <EmptyState
        title="No history yet"
        description="Status changes, renames and field changes will appear here."
      />
    );
  }
  return (
    <div className="space-y-2">
      <ol className="relative ml-2 space-y-0 border-l border-border">
        {data.map((e) => (
          <li key={e.id} className="relative py-2 pl-4">
            <span
              className={cn(
                "absolute top-3.5 -left-1 size-2 rounded-full",
                e.status ? "bg-primary" : "bg-muted-foreground/60",
              )}
            />
            {e.status ? <StatusPill status={e.status} /> : <FieldChanges type={type} changes={e.changes} />}
            <p className="mt-1 text-[0.6875rem] text-muted-foreground">
              {formatTimestamp(e.occurredAt)}
              {e.via ? (
                <>
                  {" · "}
                  <ViaAgent via={e.via} />
                </>
              ) : null}
            </p>
          </li>
        ))}
      </ol>
      {pages.full(data.length) ? (
        <Button variant="ghost" size="xs" onClick={pages.more}>
          Show older
        </Button>
      ) : null}
    </div>
  );
}

function FieldChanges({ type, changes }: { type?: SubjectType; changes: FieldChange[] }) {
  return (
    <div className="space-y-0.5 text-sm">
      {changes.map((c) => (
        <p key={c.field} className="break-words">
          <span className="font-medium">{fieldLabel(type, c.field)}</span>{" "}
          <span className="text-muted-foreground">{changeText(c)}</span>
        </p>
      ))}
    </div>
  );
}
