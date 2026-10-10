/**
 * The max level a Pokemon rule stores for "no filter". It matches `MonsterCreate.MaxLevel` and the add
 * dialog's default; 35 was the top once, and cards that still compared against it hid a cap of 40 or 50.
 */
export const NO_FILTER_MAX_LEVEL = 55;

/** True when a level range is narrower than the no-filter default, which is when a card shows it. */
export function hasLevelFilter(minLevel: null | number | undefined, maxLevel: null | number | undefined): boolean {
  return (minLevel ?? 0) > 0 || (maxLevel ?? NO_FILTER_MAX_LEVEL) < NO_FILTER_MAX_LEVEL;
}
