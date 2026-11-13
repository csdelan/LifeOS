/**
 * The search behind every subject picker (code review CR15). The words typed match anywhere in
 * an item's label, ignoring case; labels that start with them come first, then by label. A
 * picker shows at most `limit` of them and says how many more there are, so a long list never
 * renders whole and nothing past the first page is out of reach.
 */
export interface SubjectMatches<T> {
  shown: T[];
  /** How many more match than are shown. */
  more: number;
}

export function matchSubjects<T>(
  options: readonly T[],
  query: string,
  label: (option: T) => string,
  limit: number,
): SubjectMatches<T> {
  const wanted = query.trim().toLocaleLowerCase();
  const matching = wanted
    ? options
        .map((option) => ({ option, text: label(option).toLocaleLowerCase() }))
        .filter((m) => m.text.includes(wanted))
        .sort(
          (a, b) =>
            Number(b.text.startsWith(wanted)) - Number(a.text.startsWith(wanted)) ||
            a.text.localeCompare(b.text),
        )
        .map((m) => m.option)
    : [...options];
  return { shown: matching.slice(0, limit), more: Math.max(0, matching.length - limit) };
}
