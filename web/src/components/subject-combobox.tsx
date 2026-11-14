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
 * {@link SUBJECT_PICKER_LIMIT} items listed at once. A new item's parent uses it over the few
 * types it can serve; a picker over every item searches on the server instead
 * (`SubjectSearchCombobox`, code review CR107).
 */
export function SubjectCombobox<T extends { id: string; title: string }>({
  options,
  value,
  onChange,
  label = byTitle,
  ...shell
}: SubjectComboboxProps<T>) {
  const [query, setQuery] = useState("");
  const { shown, more } = useMemo(
    () => matchSubjects(options, query, label, SUBJECT_PICKER_LIMIT),
    [options, query, label],
  );

  return (
    <PickerShell
      {...shell}
      selected={options.find((o) => o.id === value)}
      shown={shown}
      more={more}
      query={query}
      onQueryChange={setQuery}
      onPick={(option) => onChange(option?.id ?? "")}
      label={label}
    />
  );
}

interface PickerShellProps<T extends { id: string; title: string }> {
  selected: T | undefined;
  /** What the list shows for the query. */
  shown: readonly T[];
  /** How many more match than it shows. */
  more: number;
  query: string;
  onQueryChange: (query: string) => void;
  /** The item picked, or null for the none choice. */
  onPick: (option: T | null) => void;
  ariaLabel: string;
  placeholder?: string;
  noneLabel?: string;
  label?: (option: T) => string;
  emptyText?: string;
  className?: string;
}

/** The picker's trigger, search box and list, over options its caller finds. */
export function PickerShell<T extends { id: string; title: string }>({
  selected,
  shown,
  more,
  query,
  onQueryChange,
  onPick,
  ariaLabel,
  placeholder = "Choose…",
  noneLabel,
  label = byTitle,
  emptyText = "No matching items.",
  className,
}: PickerShellProps<T>) {
  const [open, setOpen] = useState(false);

  const pick = (option: T | null) => {
    onPick(option);
    setOpen(false);
    onQueryChange("");
  };

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) onQueryChange("");
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
          <CommandInput value={query} onValueChange={onQueryChange} placeholder="Search…" />
          <CommandList>
            <CommandEmpty>{emptyText}</CommandEmpty>
            {noneLabel && !query.trim() ? (
              <CommandItem value="__none" onSelect={() => pick(null)} data-checked={!selected}>
                {noneLabel}
              </CommandItem>
            ) : null}
            {shown.map((option) => (
              <CommandItem
                key={option.id}
                value={option.id}
                onSelect={() => pick(option)}
                data-checked={option.id === selected?.id}
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
