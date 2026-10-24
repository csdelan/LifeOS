import { RepeatIcon } from "lucide-react";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { StatusPill } from "@/components/primitives/status-pill";
import { BountyChip } from "@/components/rewards/bounty-chip";
import type { TaskSeriesRow } from "@/lib/production-ui-types";
import { useTaskSeries } from "@/lib/queries";
import { repeatLabel, seriesTally } from "@/lib/task-series";
import { cn } from "@/lib/utils";

/**
 * Every repeating Task (TASKS-3), the Tasks page's Repeating view: how each repeats, how it
 * has gone, what a repetition pays and when the next is due. Opening one shows the series,
 * where its repeat, price and Stop repeating live.
 */
export function SeriesList({
  area,
  selected,
  onSelect,
}: {
  /** Only the ones filed under this Area (its urn), when set. */
  area?: string;
  selected?: string;
  onSelect: (id: string) => void;
}) {
  const { data, isLoading, isError } = useTaskSeries();
  if (isLoading) return <ListSkeleton rows={4} />;
  if (isError) {
    return (
      <EmptyState
        tone="error"
        icon={RepeatIcon}
        title="Repeating tasks failed to load"
        description="This is not an empty list — the reader never returned."
      />
    );
  }
  const rows = (data ?? []).filter((r) => !area || r.area === area);
  if (rows.length === 0) {
    return (
      <EmptyState
        icon={RepeatIcon}
        title="No repeating tasks"
        description="Give a Task a repeat (More… or its Edit) and it shows here."
      />
    );
  }
  return (
    <section className="mb-5">
      <h2 className="type-scale-section mb-1 px-2 text-muted-foreground">
        Repeating
        <span className="ml-2 tabular-nums">{rows.length}</span>
      </h2>
      {rows.map((row) => (
        <SeriesRow key={row.id} row={row} selected={selected === row.id} onSelect={() => onSelect(row.id)} />
      ))}
    </section>
  );
}

function SeriesRow({ row, selected, onSelect }: { row: TaskSeriesRow; selected: boolean; onSelect: () => void }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "flex min-h-11 w-full flex-wrap items-center gap-3 rounded-lg px-3 py-2 text-left transition-colors ease-hearth md:flex-nowrap",
        "hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/40",
        selected && "bg-primary/8 ring-1 ring-primary/20",
      )}
    >
      <div className="min-w-0 flex-1">
        <span className="truncate text-sm font-medium">{row.title}</span>
        <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[0.6875rem] text-muted-foreground">
          <span className="inline-flex items-center gap-1">
            <RepeatIcon className="size-3" aria-hidden />
            {repeatLabel(row.recurrence) ?? "Repeats"}
          </span>
          {row.areaName ? <span>{row.areaName}</span> : null}
          <span>{seriesTally(row)}</span>
        </div>
      </div>
      <div className="flex shrink-0 flex-wrap items-center gap-1.5">
        {row.stopped ? null : <BountyChip amounts={row.bounty} />}
        {row.stopped ? null : <DateChip date={row.nextDue} kind="due" />}
        <StatusPill status={row.stopped ? "Stopped" : "Repeating"} />
      </div>
    </button>
  );
}
