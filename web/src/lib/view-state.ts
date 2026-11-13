import { useCallback, useState } from "react";

const PREFIX = "lifeos.view.";

export function loadView<T>(name: string, fallback: T): T {
  if (typeof window === "undefined") return fallback;
  try {
    const raw = localStorage.getItem(PREFIX + name);
    if (!raw) return fallback;
    return { ...fallback, ...(JSON.parse(raw) as Partial<T>) };
  } catch {
    return fallback;
  }
}

/** Remembers `state` under `name`. Storage can be full or blocked (a private window): then the choice holds for this visit only, and nothing throws. */
export function saveView<T>(name: string, state: T) {
  if (typeof window === "undefined") return;
  try {
    localStorage.setItem(PREFIX + name, JSON.stringify(state));
  } catch {
    // Not remembered; the page goes on with the choice.
  }
}

/** A choice's options: the values, or a list of `{ id }` (a chart's ranges, a toggle's options). */
export type ChoiceOptions<T> = readonly (T | { readonly id: T })[];

function ids<T>(options: ChoiceOptions<T>): T[] {
  return options.map((o) => (typeof o === "object" && o !== null && "id" in o ? o.id : (o as T)));
}

/**
 * The choice remembered as `field` of what `name` holds, when it is still one of `options`; else
 * `fallback`. A stored value from an option since renamed or removed (or a hand-edited one) can't
 * reach a render or a read (code review CR75).
 */
export function loadChoice<T>(name: string, field: string, options: ChoiceOptions<T>, fallback: T): T {
  const stored = loadView<Record<string, unknown>>(name, {})[field];
  return ids(options).some((id) => id === stored) ? (stored as T) : fallback;
}

/**
 * A choice remembered on this device (a chart's range, a period, a panel left open), kept as
 * `field` of what `name` holds so two choices can share a name: the stored one when it is still
 * one of `options` (`loadChoice`), else `fallback`. Setting it remembers it, keeping the other
 * fields; it never throws.
 */
export function usePersistedChoice<T extends string | boolean>(
  name: string,
  field: string,
  options: ChoiceOptions<T>,
  fallback: T,
): [T, (next: T) => void] {
  const [value, setValue] = useState<T>(() => loadChoice(name, field, options, fallback));
  const set = useCallback(
    (next: T) => {
      setValue(next);
      saveView(name, { ...loadView<Record<string, unknown>>(name, {}), [field]: next });
    },
    [name, field],
  );
  return [value, set];
}

export function loadExpanded(): Set<string> {
  const stored = loadView<{ ids: string[] }>("map.expanded", { ids: [] });
  return new Set(stored.ids);
}

export function saveExpanded(ids: Set<string>) {
  saveView("map.expanded", { ids: [...ids] });
}
