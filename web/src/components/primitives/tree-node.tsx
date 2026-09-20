import { ChevronRightIcon, Link2Icon } from "lucide-react";
import { cn } from "@/lib/utils";
import { DateChip } from "@/components/primitives/date-chip";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeIcon } from "@/components/primitives/type-icon";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
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
  onNavigateParent,
  onCreateChild,
  creating,
  children,
}: {
  subject: SubjectListItem;
  depth: number;
  expanded?: boolean;
  expandable?: boolean;
  selected?: boolean;
  extraParents?: { id: string; title: string }[];
  onToggle?: () => void;
  onSelect?: () => void;
  onNavigateParent?: (id: string) => void;
  onCreateChild?: () => void;
  creating?: React.ReactNode;
  children?: React.ReactNode;
}) {
  const status = subject.status || defaultStatus(subject.type) || null;
  const createLabel = subject.type === "Task" ? "sibling" : "child";

  return (
    <div
      role="treeitem"
      aria-expanded={expandable ? Boolean(expanded) : undefined}
      aria-selected={Boolean(selected)}
      aria-level={depth + 1}
    >
      <div
        data-map-node={subject.id}
        className={cn(
          "group flex min-h-11 items-center gap-1 rounded-lg py-1 pr-2 text-sm transition-colors ease-hearth md:min-h-0",
          selected && "bg-primary/8 ring-1 ring-primary/15",
          !selected && "hover:bg-muted/60",
        )}
        style={{ paddingLeft: 8 + depth * 18 }}
      >
        <button
          type="button"
          className={cn(
            "flex size-11 shrink-0 items-center justify-center rounded-md text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring/50 md:size-5",
            expandable ? "hover:bg-muted" : "opacity-0",
          )}
          aria-label={expanded ? `Collapse ${subject.title}` : `Expand ${subject.title}`}
          disabled={!expandable}
          tabIndex={expandable ? 0 : -1}
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
          className="flex min-h-11 min-w-0 flex-1 items-center gap-2 py-0.5 text-left focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0"
          onClick={onSelect}
        >
          <span className="flex w-4 shrink-0 items-center justify-center" aria-hidden>
            <TypeIcon type={subject.type} className="size-3.5" />
          </span>
          <span className="truncate font-medium">{subject.title}</span>
        </button>
        {extraParents?.map((parent) => (
          <Tooltip key={parent.id}>
            <TooltipTrigger asChild>
              <button
                type="button"
                aria-label={`Navigate to ${parent.title}`}
                className="flex size-11 shrink-0 items-center justify-center rounded-md text-yellow-700 hover:bg-yellow-400/15 hover:text-yellow-800 focus-visible:ring-2 focus-visible:ring-ring/50 md:size-5 dark:text-yellow-400 dark:hover:text-yellow-300"
                onClick={() => onNavigateParent?.(parent.id)}
              >
                <Link2Icon className="size-3.5" strokeWidth={1.75} />
              </button>
            </TooltipTrigger>
            <TooltipContent side="top">Navigate to {parent.title}</TooltipContent>
          </Tooltip>
        ))}
        {onCreateChild ? (
          <button
            type="button"
            onClick={onCreateChild}
            aria-label={`Add ${createLabel} under ${subject.title}`}
            className={cn(
              "min-h-11 shrink-0 rounded-md px-2 text-[0.6875rem] text-muted-foreground transition-opacity hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0 md:px-1.5 md:py-0.5",
              selected ? "opacity-100" : "opacity-100 md:opacity-0 md:group-hover:opacity-100",
            )}
          >
            + {createLabel}
          </button>
        ) : null}
        <div className="ml-auto flex shrink-0 flex-wrap items-center justify-end gap-1.5">
          <DateChip date={subject.due} />
          <StatusPill status={status} />
        </div>
      </div>
      {creating}
      {expanded ? (
        <div role="group" aria-label={`Children of ${subject.title}`}>
          {children}
        </div>
      ) : null}
    </div>
  );
}
