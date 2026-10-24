import { useState } from "react";
import { toast } from "sonner";
import { RepeatIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { StatusPill } from "@/components/primitives/status-pill";
import { formatDay, formatDayFull } from "@/lib/dates";
import { isTerminal } from "@/lib/production-ui-types";
import type { TaskRepeat, TaskRepetitionRow, TaskSeriesCounts } from "@/lib/production-ui-types";
import { useTaskSeriesHistory, useWrites } from "@/lib/queries";
import { repeatLabel, seriesTally } from "@/lib/task-series";
import { cn } from "@/lib/utils";

const FIRST_ROWS = 6;

/**
 * A repeating Task on its detail (TASKS-3, D16): how it repeats, how it has gone ("3 of 4
 * done"), and its repetitions, latest first. On one repetition it says which date this one is
 * for and offers Mark missed, which closes it without paying, the next being made as for any
 * close. On the series it offers Stop repeating (Cancelled) and Repeat again, which counts
 * from today.
 */
export function RepeatPanel({
  subjectId,
  repeating,
  onOpenSubject,
}: {
  subjectId: string;
  repeating: TaskRepeat;
  /** Opens another Task in the detail: the series, or one of its repetitions. */
  onOpenSubject?: (id: string) => void;
}) {
  const writes = useWrites();
  const { data: history } = useTaskSeriesHistory(repeating.seriesId);
  const [showAll, setShowAll] = useState(false);
  const [busy, setBusy] = useState(false);
  const counts: TaskSeriesCounts = history?.series ?? repeating;
  const stopped = history?.series.stopped ?? repeating.stopped;
  const rows = history?.repetitions ?? [];
  const shown = showAll ? rows : rows.slice(0, FIRST_ROWS);
  const own = rows.find((r) => r.id === subjectId);
  const label = repeatLabel(repeating.recurrence) ?? "Repeats";

  async function move(id: string, status: string, done: string) {
    setBusy(true);
    try {
      await writes.setStatus(id, status);
      toast.success(done);
    } catch (err) {
      toast.error(err instanceof Error && err.message ? err.message : "Couldn't change it.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-2" aria-labelledby={`repeat-${subjectId}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p id={`repeat-${subjectId}`} className="type-scale-section text-muted-foreground">
          <span className="inline-flex items-center gap-1.5">
            <RepeatIcon className="size-3.5" aria-hidden />
            Repeats
          </span>
        </p>
        {repeating.isSeries ? (
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
        ) : own && !own.closed && !own.archived ? (
          <Button
            variant="outline"
            size="xs"
            disabled={busy}
            onClick={() => void move(subjectId, "Missed", "Marked missed")}
          >
            Mark missed
          </Button>
        ) : null}
      </div>

      <p className="text-sm">
        {label}
        {stopped ? <span className="text-muted-foreground"> · Stopped</span> : null}
      </p>
      {repeating.isSeries ? (
        <p className="text-xs text-muted-foreground">
          {repeating.start ? `Repeats from ${formatDayFull(repeating.start)}. ` : null}
          Each repetition is a Task of its own, done or missed on its own; this one holds the
          repeat, the price and the details they copy.
        </p>
      ) : (
        <p className="text-xs text-muted-foreground">
          {repeating.occurrence ? `This one is for ${formatDayFull(repeating.occurrence)}. ` : null}
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

      <p className="text-sm font-medium">
        {seriesTally(counts)}
        {counts.openCount > 0 ? (
          <span className="font-normal text-muted-foreground"> · {counts.openCount} open</span>
        ) : null}
        {counts.nextDue && repeating.isSeries ? (
          <span className="font-normal text-muted-foreground"> · next due {formatDay(counts.nextDue)}</span>
        ) : null}
      </p>

      {shown.length > 0 ? (
        <ul className="space-y-1" aria-label="Repetitions">
          {shown.map((row) => (
            <RepetitionItem
              key={row.id}
              row={row}
              current={row.id === subjectId}
              onOpen={onOpenSubject && row.id !== subjectId ? () => onOpenSubject(row.id) : undefined}
            />
          ))}
        </ul>
      ) : null}
      {rows.length > FIRST_ROWS ? (
        <Button variant="ghost" size="xs" onClick={() => setShowAll((v) => !v)}>
          {showAll ? "Show fewer" : `Show all ${rows.length}`}
        </Button>
      ) : null}
    </section>
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
