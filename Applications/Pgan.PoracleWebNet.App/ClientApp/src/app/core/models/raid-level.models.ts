// PoracleNG accepts any positive integer as a raid/egg level. The UI presents
// a curated vocabulary (Standard tiers, Special tiers like Mega/Elite, and a
// per-user Custom palette) on top of that integer space, plus the canonical
// "Any" sentinel (9000) that PoracleNG treats as a wildcard.
//
// `resolveLevel(value)` is the single place that maps a stored integer to the
// option it should render as — used by both the level selector dialog and the
// alarm cards so the vocabulary stays consistent across surfaces.

export type LevelCategory = 'standard' | 'special' | 'any' | 'custom';

export interface LevelOption {
  /** Optional integer shown as inline metadata (e.g., 6 next to "Mega"). */
  badge?: number;
  category: LevelCategory;
  /** ngx-translate key for the human label (e.g., `RAIDS.LEVEL.MEGA`). */
  labelKey: string;
  /** Backend integer. PoracleNG accepts any positive integer. */
  value: number;
}

/** PoracleNG's wildcard sentinel — matches any raid level. */
export const ANY_LEVEL_VALUE = 9000 as const;

export const STANDARD_LEVELS: readonly LevelOption[] = [
  { category: 'standard', labelKey: 'RAIDS.LEVEL.T1', value: 1 },
  { category: 'standard', labelKey: 'RAIDS.LEVEL.T2', value: 2 },
  { category: 'standard', labelKey: 'RAIDS.LEVEL.T3', value: 3 },
  { category: 'standard', labelKey: 'RAIDS.LEVEL.T4', value: 4 },
  { category: 'standard', labelKey: 'RAIDS.LEVEL.T5', value: 5 },
];

export const SPECIAL_LEVELS: readonly LevelOption[] = [
  { badge: 6, category: 'special', labelKey: 'RAIDS.LEVEL.MEGA', value: 6 },
  { badge: 7, category: 'special', labelKey: 'RAIDS.LEVEL.ELITE', value: 7 },
];

export const ANY_LEVEL: LevelOption = {
  category: 'any',
  labelKey: 'RAIDS.LEVEL.ANY',
  value: ANY_LEVEL_VALUE,
};

/** Build a display option for an arbitrary integer level. */
export function makeCustomLevel(value: number): LevelOption {
  return { badge: value, category: 'custom', labelKey: 'RAIDS.LEVEL.CUSTOM', value };
}

/**
 * Resolve a raw stored integer to its display option. Used by the dialog
 * (to highlight the right chip) and by alarm cards (to render the right label).
 */
export function resolveLevel(value: number): LevelOption {
  if (value === ANY_LEVEL_VALUE) return ANY_LEVEL;
  return STANDARD_LEVELS.find(l => l.value === value) ?? SPECIAL_LEVELS.find(l => l.value === value) ?? makeCustomLevel(value);
}

/**
 * True if `value` is one of the standard or special levels (i.e., something
 * the selector renders as a built-in chip rather than a custom one).
 */
export function isBuiltInLevel(value: number): boolean {
  return value === ANY_LEVEL_VALUE || STANDARD_LEVELS.some(l => l.value === value) || SPECIAL_LEVELS.some(l => l.value === value);
}
