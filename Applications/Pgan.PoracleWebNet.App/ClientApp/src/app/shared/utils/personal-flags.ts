/**
 * Helpers for the PoracleNG pokemon rule `personal` column (PoracleNG PR #255), a bitmask:
 * bit 1 = shiny for one of the human's linked game accounts, bit 2 = event background for one
 * of them. Bits 4 and 8 are reserved for future per-player sizes. Mirrors the backend
 * PersonalFlags helper, so every write preserves bits the web UI does not surface.
 */

/** Shiny bit (bit 1). */
export const SHINY = 1;

/** Event background bit (bit 2). */
export const BACKGROUND = 2;

/** The bits PoracleWeb.NET understands and edits (3). */
export const KNOWN = SHINY | BACKGROUND;

/** True when the shiny bit (bit 1) is set. */
export function isShiny(personal: number): boolean {
  return (personal & SHINY) !== 0;
}

/** True when the event background bit (bit 2) is set. */
export function isBackground(personal: number): boolean {
  return (personal & BACKGROUND) !== 0;
}

/** True when a bit outside KNOWN is set (reserved by PoracleNG). */
export function hasUnknownBits(personal: number): boolean {
  return (personal & ~KNOWN) !== 0;
}

/** Composes a personal bitmask from the two known flags. */
export function compose(shiny: boolean, background: boolean): number {
  return (shiny ? SHINY : 0) | (background ? BACKGROUND : 0);
}

/**
 * Returns `existing` with only the bits in `mask` replaced by the corresponding bits from
 * `changes`. Bits outside the mask are left untouched, so reserved bits set elsewhere survive a save.
 */
export function preserve(existing: number, mask: number, changes: number): number {
  return (existing & ~mask) | (changes & mask);
}
