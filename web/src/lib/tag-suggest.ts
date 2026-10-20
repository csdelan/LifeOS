import type { TagUniverseItem } from "@/lib/production-ui-types";

/** A tag as the kernel stores it (Tags.Normalize): trimmed, lowercased, no leading `#`. */
export function normalizeTag(raw: string): string {
  return raw.trim().replace(/^#+/, "").trim().toLowerCase();
}

/** Split comma-separated tag text into normalized, de-duplicated tags. */
export function splitTags(text: string): string[] {
  return [...new Set(text.split(",").map(normalizeTag).filter(Boolean))];
}

/**
 * Existing tags to offer while typing. With no query, the most-used tags; otherwise
 * tags containing the query, those that start with it first, then by use. Tags in
 * `exclude` (already on the item) are never offered.
 */
export function suggestTags(
  universe: readonly TagUniverseItem[],
  query: string,
  exclude: Iterable<string> = [],
  limit = 8,
): TagUniverseItem[] {
  const q = normalizeTag(query);
  const skip = new Set([...exclude].map(normalizeTag));
  const byUse = (a: TagUniverseItem, b: TagUniverseItem) =>
    b.itemCount - a.itemCount || a.tag.localeCompare(b.tag);
  const pool = universe.filter((t) => !skip.has(t.tag));
  if (!q) return [...pool].sort(byUse).slice(0, limit);
  const rank = (t: TagUniverseItem) => (t.tag.startsWith(q) ? 0 : 1);
  return pool
    .filter((t) => t.tag.includes(q))
    .sort((a, b) => rank(a) - rank(b) || byUse(a, b))
    .slice(0, limit);
}
