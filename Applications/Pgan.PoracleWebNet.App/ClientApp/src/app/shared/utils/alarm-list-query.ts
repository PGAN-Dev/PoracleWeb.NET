/**
 * Search and sort for the ten alarm lists, so that "Giratina" finds the same card on the raid page
 * that "Pikachu" finds on the Pokémon page.
 *
 * Every list hands in its rules already in a stable order (`orderAlarms`), and sorting here is stable,
 * so two rules with the same key keep that order: an edit that re-keys a rule does not move its card.
 */

/** Lowercase, accents dropped and spaces collapsed: "Pokéstop" and "pokestop" are one search. */
export function searchKey(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase().replace(/\s+/g, ' ').trim();
}

/** True when any of `fields` contains `query`; a blank query matches everything. */
export function matchesSearch(query: string, ...fields: (null | number | string | undefined)[]): boolean {
  const key = searchKey(query);
  if (!key) return true;
  return fields.some(field => field !== null && field !== undefined && searchKey(String(field)).includes(key));
}

/** A comparator on a display string, in the reader's own collation. */
export function byText<T>(text: (item: T) => string): (a: T, b: T) => number {
  return (a, b) => text(a).localeCompare(text(b));
}

/** A comparator on a number, smallest first. */
export function byNumber<T>(value: (item: T) => number): (a: T, b: T) => number {
  return (a, b) => value(a) - value(b);
}

/**
 * The rules a list shows: those matching `query` on any of `fields(item)`, sorted by `compare` (or left
 * in their incoming order without one). Returns a new array.
 */
export function queryAlarms<T>(
  list: readonly T[],
  query: string,
  fields: (item: T) => (null | number | string | undefined)[],
  compare: ((a: T, b: T) => number) | null,
): T[] {
  const shown = list.filter(item => matchesSearch(query, ...fields(item)));
  return compare ? shown.sort(compare) : shown;
}

/**
 * The selected keys that are still on screen, in screen order. Bulk Delete and Change radius act on this,
 * never on the raw selection, so a rule the search has hidden is never deleted or resized unseen. The
 * selection itself is kept, so clearing the search brings the hidden ticks back.
 */
export function visibleSelection<K>(selected: ReadonlySet<K>, visible: Iterable<K>): K[] {
  return [...visible].filter(key => selected.has(key));
}
