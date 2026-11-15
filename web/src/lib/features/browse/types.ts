/** Browse's and the Map's reads (BROWSE-1, BROWSE-2): what the server answers each in (code review CR120). */
import type { AlignmentEdge } from "@/lib/derive";
import type { BrowseColumn, BrowseFilters, BrowseOptions, BrowseType, TypeCount } from "@/lib/browse";
import type { ApiSchema, Retyped, SubjectListItem } from "@/lib/production-ui-types";

/** What Browse asks for: the type, the filters, and how many rows to draw. */
export interface BrowseQuery extends BrowseFilters {
  type: BrowseType;
  /** At most this many rows, from 1 to 1000. */
  limit: number;
}

/**
 * A page of Browse (`GET /api/browse`): the type list's held counts, the type's items (`total`)
 * and how many the filters keep (`matching`), its filter options and columns, and the first
 * `limit` rows. The app's own vocabularies narrow the generated shape.
 */
export type BrowseRead = Retyped<
  ApiSchema<"BrowseRead">,
  { types: TypeCount[]; options: BrowseOptions; columns: BrowseColumn[]; rows: SubjectListItem[] }
>;

/**
 * What the Map draws (`GET /api/alignment`): every Identity Statement, Goal, Project and Task, a
 * repeating Task once as its series, any other item an edge links, and the edges among them.
 */
export type AlignmentRead = Retyped<ApiSchema<"AlignmentRead">, { subjects: SubjectListItem[]; edges: AlignmentEdge[] }>;
