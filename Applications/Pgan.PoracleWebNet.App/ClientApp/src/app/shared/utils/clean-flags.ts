/**
 * Helpers for the PoracleNG alarm `clean` column, which is a 3-bit bitmask:
 * bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary. Mirrors PoracleNG's
 * db.IsClean / db.IsEdit / db.IsSummary (processor/internal/db/clean.go) and the
 * backend CleanFlags helper, so reads and writes preserve bits the web UI does not surface.
 */

/** Auto-delete bit (bit 1). PoracleNG db.IsClean. */
export const AUTO_DELETE = 1;

/** Edit-in-place bit (bit 2). PoracleNG db.IsEdit. */
export const EDIT = 2;

/** Summary bit (bit 4). PoracleNG db.IsSummary. */
export const SUMMARY = 4;

/** All known bits combined (7). */
export const ALL = AUTO_DELETE | EDIT | SUMMARY;

/** True when the auto-delete bit (bit 1) is set. */
export function isAutoDelete(clean: number): boolean {
  return (clean & AUTO_DELETE) !== 0;
}

/** True when the edit-in-place bit (bit 2) is set. */
export function isEdit(clean: number): boolean {
  return (clean & EDIT) !== 0;
}

/** True when the summary bit (bit 4) is set. */
export function isSummary(clean: number): boolean {
  return (clean & SUMMARY) !== 0;
}

/** Composes a clean bitmask from the three known flags. */
export function compose(autoDelete: boolean, edit: boolean, summary: boolean): number {
  return (autoDelete ? AUTO_DELETE : 0) | (edit ? EDIT : 0) | (summary ? SUMMARY : 0);
}

/**
 * Returns `existing` with only the bits in `mask` replaced by the corresponding bits from
 * `changes`. Bits outside the mask are left untouched, so bot-set bits the web UI does not
 * edit survive a save.
 */
export function preserve(existing: number, mask: number, changes: number): number {
  return (existing & ~mask) | (changes & mask);
}
