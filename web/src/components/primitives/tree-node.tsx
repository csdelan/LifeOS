import { ChevronRightIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { DateChip } from "@/components/primitives/date-chip";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeIcon } from "@/components/primitives/type-icon";
import type { SubjectListItem } from "@/lib/production-ui-types";
import { defaultStatus } from "@/lib/production-ui-types";

export function TreeNode({
  subject,
  depth,
  expanded,
  expandable,
  selected,
  extraParents,
  onToggle,
  onSelect,
  onCreateChild,
  creating,
  children,
}: {
  subject: SubjectListItem;
  depth: number;
  expanded?: boolean;
  expandable?: boolean;
  selected?: boolean;
  extraParents?: { title: string }[];
  onToggle?: () => void;
  onSelect?: () => void;
  onCreateChild?: () => void;
  creating?: React.ReactNode;
  children?: React.ReactNode;
}) {
  const status = subject.status || defaultStatus(subject.type) || null;

  return (
    <div>
      <div
        className={cn(
          "group flex items-center gap-1 rounded-lg py-1 pr-2 text-sm transition-colors ease-hearth",
          selected && "bg-primary/8 ring-1 ring-primary/15",
          !selected && "hover:bg-muted/60",
        )}
        style={{ paddingLeft: 8 + depth * 18 }}
      >
        <button
          type="button"
          className={cn(
            "flex size-5 items-center justify-center rounded-md text-muted-foreground",
            expandable ? "hover:bg-muted" : "opacity-0",
          )}
          aria-label={expanded ? "Collapse" : "Expand"}
          disabled={!expandable}
          onClick={onToggle}
        >
          <ChevronRightIcon
            className={cn(
              "size-3.5 transition-transform duration-150 ease-hearth",
              expanded && "rotate-90",
            )}
          />
        </button>
        <button
          type="button"
          className="flex min-w-0 items-center gap-2 py-0.5 text-left"
          onClick={onSelect}
        >
          <span className="flex w-4 shrink-0 items-center justify-center" aria-hidden>
            <TypeIcon type={subject.type} className="size-3.5" />
          </span>
          <span className="truncate font-medium">{subject.title}</span>
        </button>
        {onCreateChild ? (
          <button
            type="button"
            onClick={onCreateChild}
            className={cn(
              "shrink-0 rounded-md px-1.5 py-0.5 text-[0.6875rem] text-muted-foreground transition-opacity hover:bg-muted hover:text-foreground",
              selected ? "opacity-100" : "opacity-0 group-hover:opacity-100",
            )}
          >
            + {subject.type === "Task" ? "sibling" : "child"}
          </button>
        ) : null}
        {extraParents && extraParents.length > 0 ? (
          <span className="hidden min-w-0 truncate text-[0.6875rem] text-muted-foreground sm:inline">
            also under {extraParents.map((p) => p.title).join(", ")}
          </span>
        ) : null}
        <div className="ml-auto flex shrink-0 items-center gap-1.5">
          <DateChip date={subject.due} />
          <StatusPill status={status} />
        </div>
      </div>
      {creating}
      {expanded ? children : null}
    </div>
  );
}
