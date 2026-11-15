import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { StatusPill } from "@/components/primitives/status-pill";
import { useServerPages } from "@/components/primitives/use-paged";
import { useHistory } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";

/** How many status changes the tab reads at first, and how many more each Show older (code review CR121). */
export const HISTORY_PAGE = 50;

export function HistoryTab({ subjectId }: { subjectId: string }) {
  const pages = useServerPages(subjectId, HISTORY_PAGE);
  const { data } = useHistory(subjectId, pages.limit);
  if (!data?.length) {
    return (
      <EmptyState
        title="No status history"
        description="Status moves only by event. Changes will appear here."
      />
    );
  }
  return (
    <div className="space-y-2">
      <ol className="relative ml-2 space-y-0 border-l border-border">
        {data.map((e) => (
          <li key={e.id} className="relative py-2 pl-4">
            <span className="absolute top-3.5 -left-1 size-2 rounded-full bg-primary" />
            <StatusPill status={e.status} />
            <p className="mt-1 text-[0.6875rem] text-muted-foreground">
              {formatTimestamp(e.occurredAt)}
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
