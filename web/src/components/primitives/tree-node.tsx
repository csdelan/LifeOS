import { ChevronRightIcon, Link2Icon, RepeatIcon } from "lucide-react";
import { Children } from "react";
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
import { isTerminal } from "@/lib/production-ui-types";
import { repeatLabel, shownStatus } from "@/lib/task-series";

/**
 * A row of an outline, the Map's or the gallery's: a list item in a nested list, so a screen
 * reader hears the list, its depth and its size, and Tab moves through each row's buttons. The
 * outline is lists, not an ARIA tree, which would promise the arrow keys a tree has (code review
 * CR87). The toggle says whether the row is open, the selected row's title is the current one,
 * and Enter on it adds under it, as an outliner's Enter does.
 */
export function TreeNode({
  subject,
  depth,
  expanded,
  expandable,
  selected,
  finished,
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
  /** Finished work still shown (the Map's Show filter): its title recedes. */
  finished?: boolean;
  extraParents?: { id: string; title: string }[];
  onToggle?: () => void;
  onSelect?: () => void;
  onNavigateParent?: (id: string) => void;
  onCreateChild?: () => void;
  creating?: React.ReactNode;
  children?: React.ReactNode;
}) {
  const status = shownStatus(subject);
  // A repeating Task is one row, its series (TASKS-3), saying how it repeats.
  const repeats = subject.isSeries ? repeatLabel(subject.repeat) : null;
  const createLabel = subject.type === "Task" ? "sibling" : "child";
  const hasChildren = Children.count(children) > 0;

  return (
    <li>
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
          aria-expanded={expandable ? Boolean(expanded) : undefined}
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
          aria-current={selected ? "true" : undefined}
          onClick={onSelect}
          onKeyDown={(e) => {
            if (e.key !== "Enter" || !selected || !onCreateChild) return;
            e.preventDefault();
            onCreateChild();
          }}
        >
          <span className="flex w-4 shrink-0 items-center justify-center" aria-hidden>
            <TypeIcon type={subject.type} className="size-3.5" />
          </span>
          <span className={cn("truncate font-medium", finished && "text-muted-foreground")}>
            {subject.title}
          </span>
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
              selected
                ? "opacity-100"
                : "opacity-100 md:opacity-0 md:group-hover:opacity-100 md:group-focus-within:opacity-100 md:focus-visible:opacity-100",
            )}
          >
            + {createLabel}
          </button>
        ) : null}
        <div className="ml-auto flex shrink-0 flex-wrap items-center justify-end gap-1.5">
          {repeats ? (
            <span className="inline-flex items-center gap-1 text-[0.6875rem] text-muted-foreground">
              <RepeatIcon className="size-3" aria-hidden />
              {repeats}
            </span>
          ) : null}
          <DateChip date={subject.due} closed={isTerminal(subject.status)} />
          <StatusPill status={status} />
        </div>
      </div>
      {creating}
      {expanded && hasChildren ? <ul aria-label={`Children of ${subject.title}`}>{children}</ul> : null}
    </li>
  );
}
