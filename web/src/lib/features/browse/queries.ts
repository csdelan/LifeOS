/** Browse's and the Map's reads, as TanStack Query hooks. */
import { useQuery } from "@tanstack/react-query";
import { getReads } from "@/lib/client";
import { browseKeys } from "@/lib/features/browse/keys";
import type { BrowseQuery } from "@/lib/features/browse/types";

/**
 * A page of Browse. The last page stays shown while a new filter or Show more reads the next; a
 * new type starts afresh, as another type's rows and columns under its heading would mislead.
 */
export function useBrowse(query: BrowseQuery) {
  return useQuery({
    queryKey: browseKeys.browse(query),
    queryFn: () => getReads().browse(query),
    placeholderData: (previous, previousQuery) =>
      (previousQuery?.queryKey[2] as BrowseQuery | undefined)?.type === query.type ? previous : undefined,
  });
}

/** What the Map draws: its outline and its graph both fold this one read. */
export function useAlignment(includeArchived: boolean) {
  return useQuery({
    queryKey: browseKeys.alignment(includeArchived),
    queryFn: () => getReads().alignment({ includeArchived }),
  });
}
