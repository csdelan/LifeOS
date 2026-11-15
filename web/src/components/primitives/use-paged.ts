import { useState } from "react";

/** How many rows a long list draws at first, and how many more each Show more adds (code review CR76). */
export const LIST_PAGE = 200;

/**
 * How much of a long list to draw: the first `LIST_PAGE` rows, down to the open one if it's
 * further, and a page more each time Show more is pressed, so thousands of finished repetitions
 * don't all reach the page at once. A new `pagedFor` (other filters, another sort) starts again
 * from the first page. Browse and Tasks both page this way, with `ShowMore` under the list (code
 * review CR76, CR99).
 */
export function usePaged(rows: readonly { id: string }[], pagedFor: string, selected?: string) {
  const [pages, setPages] = useState(1);
  const [pagesFor, setPagesFor] = useState(pagedFor);
  if (pagesFor !== pagedFor) {
    setPagesFor(pagedFor);
    setPages(1);
  }
  const selectedAt = selected ? rows.findIndex((r) => r.id === selected) : -1;
  const drawn = Math.min(rows.length, Math.max(pages * LIST_PAGE, selectedAt + 1));
  return { drawn, total: rows.length, more: () => setPages(Math.ceil(drawn / LIST_PAGE) + 1) };
}

export type Paged = ReturnType<typeof usePaged>;

/** The most rows one read asks the server for: the API's limits top out there. */
export const MAX_READ = 1000;

/**
 * How many rows to ask the server for (code review CR120, CR121): `page` of them, a page more
 * each Show more or Show older, and one page again when `pagedFor` changes (another item, type
 * or filter). `full` says whether a read of `count` rows filled the ask, so there may be more.
 */
export function useServerPages(pagedFor: string, page = LIST_PAGE) {
  const [pages, setPages] = useState(1);
  const [pagesFor, setPagesFor] = useState(pagedFor);
  if (pagesFor !== pagedFor) {
    setPagesFor(pagedFor);
    setPages(1);
  }
  const limit = Math.min(pages * page, MAX_READ);
  return {
    limit,
    more: () => setPages((p) => p + 1),
    full: (count: number) => count >= limit && limit < MAX_READ,
  };
}
