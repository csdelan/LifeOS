import { useMemo, useState } from "react";
import { ChevronDownIcon } from "lucide-react";
import { cn } from "cn";
import {
  Command,
  CommandEmpty,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { matchSubjects } from "@/lib/subject-search";

const byTitle = (option: { title: string }) => option.title;

/** The most items a picker lists at once; the rest are a search away. */
export const SUBJECT_PICKER_LIMIT = 50;

interface SubjectComboboxProps<T extends { id: string; title: string }> {
  /** The items to choose from, in the order to list them before anything is typed. */
  options: readonly T[];
  /** The chosen item's id, or "" for none. */
  value: string;
  onChange: (id: string) => void;
  /** The picker's accessible name. */
  ariaLabel: string;
  /** What the trigger says when nothing is chosen. */
  placeholder?: string;
  /** Offers a first choice that clears the pick, labelled this. */
  noneLabel?: string;
  /** How an item reads in the list and on the trigger; its title when left out. */
  label?: (option: T) => string;
  /** What the list says when nothing matches. */
  emptyText?: string;
  className?: string;
}

/**
 * One searchable subject picker (code review CR15): type to narrow, at most
 * {@link SUBJECT_PICKER_LIMIT} items listed at once. The Inbox's Relate, a new item's parent
 * and a detail's Link all use it, so none of them can only reach the first few items.
 */
export function SubjectCombobox<T extends { id: string; title: string }>({
  options,
  value,
  onChange,
  ariaLabel,
  placeholder = "Choose…",
  noneLabel,
  label = byTitle,
  emptyText = "No matching items.",
  className,
}: SubjectComboboxProps<T>) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const selected = options.find((o) => o.id === value);
  const { shown, more } = useMemo(
    () => matchSubjects(options, query, label, SUBJECT_PICKER_LIMIT),
    [options, query, label],
  );

  const pick = (id: string) => {
    onChange(id);
    setOpen(false);
    setQuery("");
  };

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setQuery("");
      }}
    >
      <PopoverTrigger
        role="combobox"
        aria-expanded={open}
        aria-label={ariaLabel}
        className={cn(
          "flex h-8 min-w-0 items-center justify-between gap-1.5 rounded-lg border border-input bg-transparent py-2 pr-2 pl-2.5 text-left text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30 dark:hover:bg-input/50",
          !selected && "text-muted-foreground",
          className,
        )}
      >
        <span className="truncate">{selected ? label(selected) : (noneLabel ?? placeholder)}</span>
        <ChevronDownIcon className="size-4 shrink-0 opacity-50" aria-hidden />
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) min-w-64 p-0" align="start">
        {/* cmdk names its search box from the Command's label. */}
        <Command shouldFilter={false} label={`Search ${ariaLabel.toLocaleLowerCase()}`}>
          <CommandInput value={query} onValueChange={setQuery} placeholder="Search…" />
          <CommandList>
            <CommandEmpty>{emptyText}</CommandEmpty>
            {noneLabel && !query.trim() ? (
              <CommandItem value="__none" onSelect={() => pick("")} data-checked={value === ""}>
                {noneLabel}
              </CommandItem>
            ) : null}
            {shown.map((option) => (
              <CommandItem
                key={option.id}
                value={option.id}
                onSelect={() => pick(option.id)}
                data-checked={option.id === value}
              >
                <span className="truncate">{label(option)}</span>
              </CommandItem>
            ))}
            {more > 0 ? (
              <p className="px-2 py-1.5 text-xs text-muted-foreground">
                {more} more: type to narrow.
              </p>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
