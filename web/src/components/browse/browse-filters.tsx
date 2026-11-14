import { useId } from "react";
import { ArrowDownIcon, ArrowUpIcon, LibraryIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { TypeIcon } from "@/components/primitives/type-icon";
import {
  clearedFilters,
  DUE_OPTIONS,
  ENTERED_OPTIONS,
  activeFilterCount,
  nextSort,
  sortOptionsFor,
  type BrowseColumn,
  type BrowseFilters,
  type BrowseOptions,
  type BrowseSort,
  type BrowseType,
  type FilterOption,
  type TypeCount,
} from "@/lib/browse";
import { typeLabel } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

export function TypeList({
  counts,
  type,
  onPick,
}: {
  counts: { total: number; types: TypeCount[] };
  type: BrowseType;
  onPick: (type: BrowseType) => void;
}) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="type-scale-section px-2 text-muted-foreground">
        Type
      </h2>
      <ul className="mt-1.5 space-y-0.5">
        <li>
          <TypeButton
            label="All types"
            count={counts.total}
            active={type === "all"}
            onClick={() => onPick("all")}
          />
        </li>
        {counts.types.map((t) => (
          <li key={t.type}>
            <TypeButton
              label={typeLabel(t.type)}
              icon={<TypeIcon type={t.type} className="size-3.5" />}
              count={t.count}
              active={type === t.type}
              onClick={() => onPick(t.type)}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}

function TypeButton({
  label,
  icon,
  count,
  active,
  onClick,
}: {
  label: string;
  icon?: React.ReactNode;
  count: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? "true" : undefined}
      className={cn(
        "flex min-h-8 w-full items-center gap-2 rounded-lg px-2 text-left text-sm transition-colors ease-hearth focus-visible:ring-2 focus-visible:ring-ring/50",
        active ? "bg-primary/10 font-medium text-foreground" : "hover:bg-muted/70",
        !active && count === 0 && "text-muted-foreground",
      )}
    >
      {icon ?? <LibraryIcon className="size-3.5" aria-hidden />}
      <span className="min-w-0 flex-1 truncate">{label}</span>
      <span className="text-xs tabular-nums text-muted-foreground">{count}</span>
    </button>
  );
}

export function TypeSelect({
  counts,
  type,
  onPick,
}: {
  counts: { total: number; types: TypeCount[] };
  type: BrowseType;
  onPick: (type: BrowseType) => void;
}) {
  return (
    <Select value={type} onValueChange={(v) => onPick(v as BrowseType)}>
      <SelectTrigger size="sm" className="h-8 min-w-36" aria-label="Type">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">All types ({counts.total})</SelectItem>
        {counts.types.map((t) => (
          <SelectItem key={t.type} value={t.type}>
            {typeLabel(t.type)} ({t.count})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

export function FilterControls({
  filters,
  options,
  showDue,
  onChange,
}: {
  filters: BrowseFilters;
  options: BrowseOptions;
  showDue: boolean;
  onChange: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const archivedId = useId();
  const narrowing = activeFilterCount(filters) - (filters.text.trim() ? 1 : 0);
  return (
    <div className="space-y-5">
      {options.statuses.length ? (
        <CheckList
          legend="Status"
          options={options.statuses}
          picked={filters.statuses}
          onChange={(statuses) => onChange((f) => ({ ...f, statuses }))}
        />
      ) : null}
      {showDue ? (
        <LabeledSelect
          label="Due date"
          value={filters.due}
          options={DUE_OPTIONS}
          onChange={(due) => onChange((f) => ({ ...f, due }))}
        />
      ) : null}
      <LabeledSelect
        label="Entered"
        value={filters.entered}
        options={ENTERED_OPTIONS}
        onChange={(entered) => onChange((f) => ({ ...f, entered }))}
      />
      {options.areas.length ? (
        <CheckList
          legend="Area"
          options={options.areas}
          picked={filters.areas}
          onChange={(areas) => onChange((f) => ({ ...f, areas }))}
        />
      ) : null}
      {options.tags.length ? (
        <CheckList
          legend="Tags"
          options={options.tags}
          picked={filters.tags}
          onChange={(tags) => onChange((f) => ({ ...f, tags }))}
          scroll
        />
      ) : null}
      <div className="flex items-center justify-between gap-2 px-1">
        <Label htmlFor={archivedId} className="font-normal">
          Include archived
        </Label>
        <Switch
          id={archivedId}
          checked={filters.archived}
          onCheckedChange={(archived) => onChange((f) => ({ ...f, archived }))}
        />
      </div>
      {narrowing > 0 ? (
        <Button variant="outline" size="sm" onClick={() => onChange((f) => ({ ...clearedFilters(f), text: f.text }))}>
          Clear filters
        </Button>
      ) : null}
    </div>
  );
}

/** Picked values inside one category combine with OR (BROWSE-2). */
function CheckList({
  legend,
  options,
  picked,
  onChange,
  scroll = false,
}: {
  legend: string;
  options: FilterOption[];
  picked: string[];
  onChange: (picked: string[]) => void;
  scroll?: boolean;
}) {
  const idPrefix = useId();
  return (
    <fieldset>
      <legend className="type-scale-section px-1 text-muted-foreground">{legend}</legend>
      <ul className={cn("mt-1.5 space-y-1.5 px-1", scroll && "max-h-48 overflow-y-auto py-0.5")}>
        {options.map((o, i) => {
          const id = `${idPrefix}-${i}`;
          const on = picked.includes(o.value);
          return (
            <li key={o.value} className="flex min-h-8 items-center gap-2 md:min-h-0">
              <Checkbox
                id={id}
                checked={on}
                onCheckedChange={(next) =>
                  onChange(
                    next === true
                      ? [...picked, o.value]
                      : picked.filter((p) => p !== o.value),
                  )
                }
              />
              <Label htmlFor={id} className="min-w-0 flex-1 truncate font-normal">
                {o.label}
              </Label>
              <span className="text-xs tabular-nums text-muted-foreground">{o.count}</span>
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

function LabeledSelect<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div className="space-y-1.5 px-1">
      <p className="type-scale-section text-muted-foreground">{label}</p>
      <Select value={value} onValueChange={(v) => onChange(v as T)}>
        <SelectTrigger size="sm" className="h-8 w-full" aria-label={label}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options.map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

/** The sort as a menu, for a list too narrow for its column headers. */
export function SortControl({
  filters,
  columns,
  onChange,
}: {
  filters: BrowseFilters;
  columns: BrowseColumn[];
  onChange: (update: (f: BrowseFilters) => BrowseFilters) => void;
}) {
  const asc = filters.dir === "asc";
  return (
    <div className="flex items-center gap-1 @xl:hidden">
      <Select
        value={filters.sort}
        onValueChange={(v) => onChange((f) => ({ ...nextSort(f, v as BrowseSort) }))}
      >
        <SelectTrigger size="sm" className="h-8" aria-label="Sort by">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {sortOptionsFor(columns, filters.sort).map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        variant="outline"
        size="icon"
        aria-label={asc ? "Sorted ascending: sort descending" : "Sorted descending: sort ascending"}
        onClick={() => onChange((f) => ({ ...f, dir: f.dir === "asc" ? "desc" : "asc" }))}
      >
        {asc ? <ArrowUpIcon /> : <ArrowDownIcon />}
      </Button>
    </div>
  );
}
