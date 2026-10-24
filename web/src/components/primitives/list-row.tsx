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
  className,
}: {
  item: SubjectListItem;
  selected?: boolean;
  onSelect?: () => void;
  trailing?: React.ReactNode;
  /** Lead with the subject's cover, when it has one (COVER-3: Today's goals and projects). */
  withCover?: boolean;
  className?: string;
}) {
  const status = item.status || defaultStatus(item.type) || null;
  // One repetition of a repeating Task (TASKS-3) says how it repeats.
  const repeats = repeatLabel(item.repeat);
  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "flex min-h-11 w-full flex-wrap items-center gap-3 rounded-lg px-3 py-2 text-left transition-colors ease-hearth md:flex-nowrap",
        "hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/40",
        selected && "bg-primary/8 ring-1 ring-primary/20",
        className,
      )}
    >
      {withCover && item.cover ? <CoverThumb cover={item.cover} size={36} /> : null}
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-sm font-medium">{item.title}</span>
          {item.archived ? (
            <span className="text-[0.6875rem] text-muted-foreground">Archived</span>
          ) : null}
        </div>
        <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
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
        </div>
      </div>
      <div className="flex shrink-0 flex-wrap items-center gap-1.5">
        {trailing}
        {/* What finishing it pays; once it's done, it has paid. */}
        {isTerminal(status) ? null : <BountyChip amounts={item.bounty} />}
        <DateChip date={item.due} kind="due" />
        {item.scheduled && item.scheduled !== item.due ? (
          <DateChip date={item.scheduled} kind="scheduled" />
        ) : null}
        <StatusPill status={status} />
      </div>
    </button>
  );
}
