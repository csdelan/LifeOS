/** Browse's and the Map's cached reads (`lib/query-keys.ts` holds every feature's). */
import type { BrowseQuery } from "@/lib/features/browse/types";

export const browseKeys = {
  // Under ["subjects"], so every write that refreshes the subject lists refreshes these too.
  browse: (query: BrowseQuery) => ["subjects", "browse", query] as const,
  alignment: (includeArchived: boolean) => ["subjects", "alignment", includeArchived] as const,
};
