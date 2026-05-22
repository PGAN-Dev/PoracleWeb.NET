// Canonical raid level vocabulary, sourced from the WatWowMap masterfile:
// https://github.com/WatWowMap/Masterfile-Generator/blob/main/master-latest-poracle-v2.json
//
// PoracleNG accepts any positive integer as a raid/egg level. The UI maps the
// 19 currently-known integers to their canonical names; users can still add
// arbitrary integers via the custom input for forward compatibility with new
// raid types that haven't shipped to the frontend yet.
//
// Backend matching is purely integer-keyed — names are pure UI vocabulary.
// `resolveLevel(value)` is the single mapping from stored integer → display
// option, used by the selector dialog and the alarm cards alike so all
// surfaces speak the same vocabulary.

export type LevelCategory = 'star' | 'mega' | 'special' | 'shadow' | 'superMega' | 'coordinated' | 'any' | 'custom';

export interface LevelOption {
  /** Coarse grouping for the selector overflow menu and category badges. */
  category: LevelCategory;
  /** ngx-translate key for the singular human label. */
  labelKey: string;
  /** ngx-translate key for the plural form (used in card titles). */
  pluralKey?: string;
  /** Backend integer. PoracleNG accepts any positive integer. */
  value: number;
}

/** PoracleNG's wildcard sentinel for raid matching — matches any raid level. */
export const ANY_LEVEL_VALUE = 9000 as const;

/**
 * The 19 known raid levels. Order matters for menu rendering; categories cluster
 * naturally by integer (1-5 star, 6-7 mega, 8-10 special, 11-15 shadow,
 * 16-17 super mega, 18-19 coordinated).
 */
export const KNOWN_LEVELS: readonly LevelOption[] = [
  { category: 'star', labelKey: 'RAIDS.LEVEL.RAID_1', pluralKey: 'RAIDS.LEVEL.RAID_1_PLURAL', value: 1 },
  { category: 'star', labelKey: 'RAIDS.LEVEL.RAID_2', pluralKey: 'RAIDS.LEVEL.RAID_2_PLURAL', value: 2 },
  { category: 'star', labelKey: 'RAIDS.LEVEL.RAID_3', pluralKey: 'RAIDS.LEVEL.RAID_3_PLURAL', value: 3 },
  { category: 'star', labelKey: 'RAIDS.LEVEL.RAID_4', pluralKey: 'RAIDS.LEVEL.RAID_4_PLURAL', value: 4 },
  { category: 'star', labelKey: 'RAIDS.LEVEL.RAID_5', pluralKey: 'RAIDS.LEVEL.RAID_5_PLURAL', value: 5 },
  { category: 'mega', labelKey: 'RAIDS.LEVEL.RAID_6', pluralKey: 'RAIDS.LEVEL.RAID_6_PLURAL', value: 6 },
  { category: 'mega', labelKey: 'RAIDS.LEVEL.RAID_7', pluralKey: 'RAIDS.LEVEL.RAID_7_PLURAL', value: 7 },
  { category: 'special', labelKey: 'RAIDS.LEVEL.RAID_8', pluralKey: 'RAIDS.LEVEL.RAID_8_PLURAL', value: 8 },
  { category: 'special', labelKey: 'RAIDS.LEVEL.RAID_9', pluralKey: 'RAIDS.LEVEL.RAID_9_PLURAL', value: 9 },
  { category: 'special', labelKey: 'RAIDS.LEVEL.RAID_10', pluralKey: 'RAIDS.LEVEL.RAID_10_PLURAL', value: 10 },
  { category: 'shadow', labelKey: 'RAIDS.LEVEL.RAID_11', pluralKey: 'RAIDS.LEVEL.RAID_11_PLURAL', value: 11 },
  { category: 'shadow', labelKey: 'RAIDS.LEVEL.RAID_12', pluralKey: 'RAIDS.LEVEL.RAID_12_PLURAL', value: 12 },
  { category: 'shadow', labelKey: 'RAIDS.LEVEL.RAID_13', pluralKey: 'RAIDS.LEVEL.RAID_13_PLURAL', value: 13 },
  { category: 'shadow', labelKey: 'RAIDS.LEVEL.RAID_14', pluralKey: 'RAIDS.LEVEL.RAID_14_PLURAL', value: 14 },
  { category: 'shadow', labelKey: 'RAIDS.LEVEL.RAID_15', pluralKey: 'RAIDS.LEVEL.RAID_15_PLURAL', value: 15 },
  { category: 'superMega', labelKey: 'RAIDS.LEVEL.RAID_16', pluralKey: 'RAIDS.LEVEL.RAID_16_PLURAL', value: 16 },
  { category: 'superMega', labelKey: 'RAIDS.LEVEL.RAID_17', pluralKey: 'RAIDS.LEVEL.RAID_17_PLURAL', value: 17 },
  { category: 'coordinated', labelKey: 'RAIDS.LEVEL.RAID_18', pluralKey: 'RAIDS.LEVEL.RAID_18_PLURAL', value: 18 },
  { category: 'coordinated', labelKey: 'RAIDS.LEVEL.RAID_19', pluralKey: 'RAIDS.LEVEL.RAID_19_PLURAL', value: 19 },
];

/** Values 1-5: the visually star-rendered "N Star Raid" tier. */
export const STAR_LEVELS: readonly LevelOption[] = KNOWN_LEVELS.filter(l => l.category === 'star');

/** Eggs only realistically use 1-5 in current Pokémon GO. */
export const EGG_LEVELS: readonly LevelOption[] = STAR_LEVELS;

/** Mega + Mega Legendary (6, 7). The primary "common but not star" tier. */
export const MEGA_LEVELS: readonly LevelOption[] = KNOWN_LEVELS.filter(l => l.category === 'mega');

/** Levels surfaced in the primary chip row of the raid picker. */
export const PRIMARY_RAID_LEVELS: readonly LevelOption[] = [...STAR_LEVELS, ...MEGA_LEVELS];

/** Levels relegated to the "More raid types…" overflow on the raid picker. */
export const OVERFLOW_RAID_LEVELS: readonly LevelOption[] = KNOWN_LEVELS.filter(l => l.category !== 'star' && l.category !== 'mega');

export const ANY_LEVEL: LevelOption = {
  category: 'any',
  labelKey: 'RAIDS.LEVEL.ANY',
  pluralKey: 'RAIDS.LEVEL.ANY',
  value: ANY_LEVEL_VALUE,
};

/** Build a display option for an arbitrary integer level (unknown to the masterfile). */
export function makeCustomLevel(value: number): LevelOption {
  return { category: 'custom', labelKey: 'RAIDS.LEVEL.CUSTOM', pluralKey: 'RAIDS.LEVEL.CUSTOM_PLURAL', value };
}

/**
 * Resolve a raw stored integer to its display option. Returns the canonical
 * known option if recognized, the ANY sentinel for 9000, or a custom-category
 * option otherwise.
 */
export function resolveLevel(value: number): LevelOption {
  if (value === ANY_LEVEL_VALUE) return ANY_LEVEL;
  return KNOWN_LEVELS.find(l => l.value === value) ?? makeCustomLevel(value);
}

/** True if `value` is one of the masterfile-known levels (1-19) or the ANY sentinel. */
export function isKnownLevel(value: number): boolean {
  return value === ANY_LEVEL_VALUE || KNOWN_LEVELS.some(l => l.value === value);
}

/**
 * Backward-compat alias retained while callers migrate. Equivalent to `isKnownLevel`.
 * @deprecated Use isKnownLevel.
 */
export function isBuiltInLevel(value: number): boolean {
  return isKnownLevel(value);
}
