/** Browse's and the Map's reads: what the read seam asks for, and how the live app asks the API. */
import { withNormalCover } from "@/lib/cover";
import type { AlignmentRead, BrowseQuery, BrowseRead } from "@/lib/features/browse/types";
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";

export interface BrowseReads {
  /**
   * A page of Browse (BROWSE-2, `GET /api/browse`, code review CR120): the type's items through the
   * filters, sorted, the first `limit` of them, with the type list's counts and the type's filter
   * options and columns. The server counts and pages, so Browse reads a page, not every subject.
   */
  browse(query: BrowseQuery): Promise<BrowseRead>;
  /**
   * What the Map draws (BROWSE-1, `GET /api/alignment`): the alignment types and any item an edge
   * links, a repeating Task once as its series, and the edges among them; archived ones when asked.
   */
  alignment(opts: { includeArchived: boolean }): Promise<AlignmentRead>;
}

const many = (values: string[]) => (values.length > 0 ? values : undefined);

export const liveBrowseReads: BrowseReads = {
  async browse(query) {
    const data = await unwrap(
      api().GET("/api/browse", {
        params: {
          query: {
            type: query.type,
            text: query.text.trim() || undefined,
            status: many(query.statuses),
            area: many(query.areas),
            tag: many(query.tags),
            due: query.due,
            entered: query.entered,
            archived: query.archived,
            sort: query.sort,
            dir: query.dir,
            limit: query.limit,
          },
        },
      }),
    );
    const read = fits<BrowseRead>()(data);
    return { ...read, rows: read.rows.map(withNormalCover) };
  },

  async alignment({ includeArchived }) {
    const data = await unwrap(api().GET("/api/alignment", { params: { query: { includeArchived } } }));
    const read = fits<AlignmentRead>()(data);
    return { ...read, subjects: read.subjects.map(withNormalCover) };
  },
};
