import { EmptyState } from "@/components/primitives/empty-state";
import { StatusPill } from "@/components/primitives/status-pill";
import { useHistory } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";

export function HistoryTab({ subjectId }: { subjectId: string }) {
  const { data } = useHistory(subjectId);
  if (!data?.length) {
    return (
      <EmptyState
        title="No status history"
        description="Status moves only by event. Changes will appear here."
      />
    );
  }
  return (
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
  );
}
