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

export function saveView<T>(name: string, state: T) {
  if (typeof window === "undefined") return;
  localStorage.setItem(PREFIX + name, JSON.stringify(state));
}

export function loadExpanded(): Set<string> {
  const stored = loadView<{ ids: string[] }>("map.expanded", { ids: [] });
  return new Set(stored.ids);
}

export function saveExpanded(ids: Set<string>) {
  saveView("map.expanded", { ids: [...ids] });
}
