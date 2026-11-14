import { useState } from "react";
import { PickerShell } from "@/components/subject-combobox";
import { useSubjectSearch } from "@/lib/queries";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";

const NO_TYPES: readonly SubjectType[] = [];

/**
 * A picker over every item, searched on the server as Chris types (code review CR107): it reads
 * the best matches, as the palette does, where the Inbox's Relate and a detail's Link had read
 * every subject there is to filter them in the browser. Nothing is listed until something's typed.
 */
export function SubjectSearchCombobox({
  value,
  onChange,
  types = NO_TYPES,
  includeArchived = false,
  exclude,
  ariaLabel,
  placeholder,
  label,
  className,
}: {
  /** The chosen item, or null for none. */
  value: SubjectListItem | null;
  onChange: (item: SubjectListItem | null) => void;
  /** The types to search; every type when empty. */
  types?: readonly SubjectType[];
  /** Lists archived matches too. */
  includeArchived?: boolean;
  /** An item never to offer, such as the one being linked from. */
  exclude?: string;
  ariaLabel: string;
  placeholder?: string;
  label?: (item: SubjectListItem) => string;
  className?: string;
}) {
  const [query, setQuery] = useState("");
  const searched = useDebouncedValue(query, 150);
  const { data } = useSubjectSearch(searched, types);
  const typed = query.trim() !== "";
  const matches = typed ? (data?.matches ?? []) : [];
  const shown = matches.filter((s) => s.id !== exclude && (includeArchived || !s.archived));

  return (
    <PickerShell
      selected={value ?? undefined}
      shown={shown}
      more={typed && data ? Math.max(0, data.total - data.matches.length) : 0}
      query={query}
      onQueryChange={setQuery}
      onPick={onChange}
      ariaLabel={ariaLabel}
      placeholder={placeholder}
      label={label}
      emptyText={!typed ? "Type to find an item." : data ? "No matching items." : "Searching…"}
      className={className}
    />
  );
}
