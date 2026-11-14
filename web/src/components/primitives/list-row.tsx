import { RepeatIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { CoverThumb } from "@/components/cover/cover-thumb";
import { DateChip } from "@/components/primitives/date-chip";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { BountyChip } from "@/components/rewards/bounty-chip";
import type { SubjectListItem } from "@/lib/production-ui-types";
import { defaultStatus, isTerminal } from "@/lib/production-ui-types";
import { repeatLabel } from "@/lib/task-series";

export function ListRow({
  item,
  selected,
  onSelect,
  trailing,
  withCover = false,
  stacked = false,
  className,
}: {
  item: SubjectListItem;
  selected?: boolean;
  onSelect?: () => void;
  trailing?: React.ReactNode;
  /** Lead with the subject's cover, when it has one (COVER-3: Today's goals and projects). */
  withCover?: boolean;
  /**
   * The title has the first line to itself, wrapping rather than cut off, and everything else
   * goes on the line under it. For a narrow column, where the chips beside the title would leave
   * it too little room to say what the item is (Focus: Due & overdue).
   */
  stacked?: boolean;
  className?: string;
}) {
  const status = item.status || defaultStatus(item.type) || null;
  const closed = isTerminal(status);
  // One repetition of a repeating Task (TASKS-3) says how it repeats.
  const repeats = repeatLabel(item.repeat);
  const chips = (
    <>
      {trailing}
      {/* What finishing it pays; once it's done, it has paid. */}
      {closed ? null : <BountyChip amounts={item.bounty} />}
      {/* A finished item isn't overdue: its dates show plain. */}
      <DateChip date={item.due} kind="due" closed={closed} />
      {item.scheduled && item.scheduled !== item.due ? (
        <DateChip date={item.scheduled} kind="scheduled" closed={closed} />
      ) : null}
      <StatusPill status={status} />
    </>
  );
  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "flex min-h-11 w-full gap-3 rounded-lg px-3 py-2 text-left transition-colors ease-hearth",
        stacked ? "items-start" : "flex-wrap items-center md:flex-nowrap",
        "hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/40",
        selected && "bg-primary/8 ring-1 ring-primary/20",
        className,
      )}
    >
      {withCover && item.cover ? <CoverThumb cover={item.cover} size={36} /> : null}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span
            className={cn("text-sm font-medium", stacked ? "min-w-0 break-words" : "truncate")}
          >
            {item.title}
          </span>
          {item.archived ? (
            <span className="shrink-0 text-[0.6875rem] text-muted-foreground">Archived</span>
          ) : null}
        </div>
        <div
          className={cn("flex flex-wrap items-center gap-1.5", stacked ? "mt-1" : "mt-0.5")}
        >
          <TypeBadge type={item.type} />
          {item.areaName ? (
            <span className="text-[0.6875rem] text-muted-foreground">{item.areaName}</span>
          ) : null}
          {repeats ? (
            <span className="inline-flex items-center gap-1 text-[0.6875rem] text-muted-foreground">
              <RepeatIcon className="size-3" aria-hidden />
              {repeats}
            </span>
          ) : null}
          {stacked ? chips : null}
        </div>
      </div>
      {stacked ? null : (
        <div className="flex shrink-0 flex-wrap items-center gap-1.5">{chips}</div>
      )}
    </button>
  );
}
