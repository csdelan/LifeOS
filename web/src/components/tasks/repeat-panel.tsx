import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { RepeatIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DetailSection, PropertyList, PropertyRow } from "@/components/primitives/detail-section";
import { StatusPill } from "@/components/primitives/status-pill";
import { LIST_PAGE, MAX_READ } from "@/components/primitives/use-paged";
import { formatDay, formatDayFull } from "@/lib/dates";
import { isTerminal } from "@/lib/production-ui-types";
import type { TaskRepeat, TaskRepetitionRow, TaskSeriesCounts } from "@/lib/production-ui-types";
import { useListItem, useTaskSeriesHistory, useWrites } from "@/lib/queries";
import { repeatLabel, seriesTally } from "@/lib/task-series";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";

const FIRST_ROWS = 6;

/**
 * A repeating Task on its detail (TASKS-3, D16), everything about its repeat in one group: how
 * it repeats and from when, how it has gone ("3 of 4 done"), and its repetitions, latest first.
 * On one repetition it says which date this one is for and offers Mark missed, which closes it
 * without paying, the next being made as for any close. On the series it offers Stop repeating
 * (Cancelled) and Repeat again, which counts from today. While the item is edited, the series'
 * pattern and start are the form's inputs (`editRows`), and the buttons wait: a status changed
 * here would be undone by the Save, as the Decision's are.
 */
export function RepeatPanel({
  subjectId,
  repeating,
  onOpenSubject,
  editing = false,
  editRows,
}: {
  subjectId: string;
  repeating: TaskRepeat;
  /** Opens another Task in the detail: the series, or one of its repetitions. */
  onOpenSubject?: (id: string) => void;
  editing?: boolean;
  /** The form's rows for the pattern and its start, shown in place of them while editing. */
  editRows?: ReactNode;
}) {
  const writes = useWrites();
  // The newest few repetitions, then a page more each Show more, read on the server: a daily
  // Task's years of them don't all load to show six (code review CR121).
  const [limit, setLimit] = useState(FIRST_ROWS);
  const { data: history } = useTaskSeriesHistory(repeating.seriesId, limit);
  // A repetition's own row, to offer Mark missed: it may be older than the rows read.
  const { data: own } = useListItem(repeating.isSeries ? undefined : subjectId);
  const changing = useWriteAction({ report: "toast", fallback: "Couldn't change it." });
  const busy = changing.pending;
  const counts: TaskSeriesCounts = history?.series ?? repeating;
  const stopped = history?.series.stopped ?? repeating.stopped;
  const rows = history?.repetitions ?? [];
  const count = history?.repetitionCount ?? rows.length;
  const label = repeatLabel(repeating.recurrence) ?? "Repeats";

  async function move(id: string, status: string, done: string) {
    await changing.run(() => writes.setStatus(id, status), () => toast.success(done));
  }

  const action = editing ? null : repeating.isSeries ? (
    stopped ? (
      <Button
        variant="outline"
        size="xs"
        disabled={busy}
        onClick={() => void move(repeating.seriesId, "Not started", "Repeating again, from today")}
      >
        Repeat again
      </Button>
    ) : (
      <Button
        variant="outline"
        size="xs"
        disabled={busy}
        onClick={() => void move(repeating.seriesId, "Cancelled", "Stopped repeating")}
      >
        Stop repeating
      </Button>
    )
  ) : own && !isTerminal(own.status) && !own.archived ? (
    <Button variant="outline" size="xs" disabled={busy} onClick={() => void move(subjectId, "Missed", "Marked missed")}>
      Mark missed
    </Button>
  ) : null;

  return (
    <DetailSection
      title="Repeats"
      icon={RepeatIcon}
      action={
        <>
          {stopped ? <span className="text-xs font-medium text-muted-foreground">Stopped</span> : null}
          {action}
        </>
      }
    >
      <PropertyList>
        {editRows ?? (
          <>
            <PropertyRow label="Repeat">
              <p>{label}</p>
            </PropertyRow>
            {repeating.isSeries && repeating.start ? (
              <PropertyRow label="Repeats from">{formatDayFull(repeating.start)}</PropertyRow>
            ) : null}
          </>
        )}
        {!repeating.isSeries && repeating.occurrence ? (
          <PropertyRow label="Repetition for">{formatDayFull(repeating.occurrence)}</PropertyRow>
        ) : null}
        <PropertyRow label="So far">
          <p className="font-medium">
            {seriesTally(counts)}
            {counts.openCount > 0 ? (
              <span className="font-normal text-muted-foreground"> · {counts.openCount} open</span>
            ) : null}
            {counts.nextDue && repeating.isSeries ? (
              <span className="font-normal text-muted-foreground"> · next due {formatDay(counts.nextDue)}</span>
            ) : null}
          </p>
        </PropertyRow>
      </PropertyList>

      {repeating.isSeries ? (
        <p className="text-xs text-muted-foreground">
          Each repetition is a Task of its own, done or missed on its own; this one holds the repeat,
          the price and the details they copy.
        </p>
      ) : (
        <p className="text-xs text-muted-foreground">
          The repeat, the price and Stop repeating are on{" "}
          {onOpenSubject ? (
            <button
              type="button"
              className="font-medium text-primary underline-offset-2 hover:underline"
              onClick={() => onOpenSubject(repeating.seriesId)}
            >
              the repeating task
            </button>
          ) : (
            "the repeating task"
          )}
          .
        </p>
      )}

      {rows.length > 0 ? (
        <div className="space-y-1">
          <p className="text-[0.8125rem] text-muted-foreground">Repetitions</p>
          <ul className="-mx-2 space-y-0.5" aria-label="Repetitions">
            {rows.map((row) => (
              <RepetitionItem
                key={row.id}
                row={row}
                current={row.id === subjectId}
                onOpen={onOpenSubject && row.id !== subjectId ? () => onOpenSubject(row.id) : undefined}
              />
            ))}
          </ul>
        </div>
      ) : null}
      {rows.length < count && limit < MAX_READ ? (
        <Button
          variant="ghost"
          size="xs"
          onClick={() => setLimit((l) => Math.min(l === FIRST_ROWS ? LIST_PAGE : l + LIST_PAGE, MAX_READ))}
        >
          {limit === FIRST_ROWS && count <= LIST_PAGE ? `Show all ${count}` : `Show ${Math.min(count - rows.length, LIST_PAGE)} more`}
        </Button>
      ) : limit > FIRST_ROWS ? (
        <Button variant="ghost" size="xs" onClick={() => setLimit(FIRST_ROWS)}>
          Show fewer
        </Button>
      ) : null}
    </DetailSection>
  );
}

function RepetitionItem({
  row,
  current,
  onOpen,
}: {
  row: TaskRepetitionRow;
  current: boolean;
  onOpen?: () => void;
}) {
  const when = row.occurrence ? formatDay(row.occurrence) : "—";
  const closed =
    row.closed && row.closedOn && isTerminal(row.status) ? ` on ${formatDay(row.closedOn)}` : "";
  const body = (
    <>
      <span className="w-16 shrink-0 tabular-nums">{when}</span>
      <StatusPill status={row.status ?? null} />
      <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
        {row.closed ? `${(row.status ?? "").toLowerCase()}${closed}` : row.due ? `due ${formatDay(row.due)}` : ""}
        {row.archived ? " · archived" : ""}
      </span>
      {current ? <span className="text-[0.6875rem] text-muted-foreground">This one</span> : null}
    </>
  );
  return (
    <li>
      {onOpen ? (
        <button
          type="button"
          onClick={onOpen}
          className={cn(
            "flex w-full items-center gap-2 rounded-md px-2 py-1 text-left text-sm hover:bg-muted/70",
            "focus-visible:ring-2 focus-visible:ring-ring/40",
          )}
        >
          {body}
        </button>
      ) : (
        <div className={cn("flex items-center gap-2 rounded-md px-2 py-1 text-sm", current && "bg-muted/60")}>
          {body}
        </div>
      )}
    </li>
  );
}
