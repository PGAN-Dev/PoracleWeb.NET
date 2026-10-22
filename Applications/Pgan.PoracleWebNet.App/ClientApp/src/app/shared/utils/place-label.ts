/**
 * The name rules a saved place's label shares with the API (`PlaceLabelRules`, which reuses
 * `ProfileNameRules`' character rule).
 */

/** `humans_locations.label` is varchar(64). */
export const PLACE_LABEL_MAX_LENGTH = 64;

/**
 * Control characters (C0, DEL, C1), line and paragraph separators, and the Unicode bidirectional controls:
 * the client twin of `ProfileNameRules.IsRefused`. U+202E made "Gym" + "evil" display as "Gymlive".
 * Deliberately not every format character: emoji sequences are joined with U+200D, which is one.
 */
const REFUSED_NAME_CHARACTER = /[\u0000-\u001F\u007F-\u009F\u2028\u2029\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/;

export function hasRefusedNameCharacter(value: string): boolean {
  return REFUSED_NAME_CHARACTER.test(value);
}

/** The prompt options a place name is asked for with, shared by Areas & Places and the scope picker. */
export const PLACE_NAME_PROMPT = {
  refuseHiddenCharacters: true,
  conflictKey: 'WHERE.PLACE_NAME_CONFLICT',
  maxLength: PLACE_LABEL_MAX_LENGTH,
} as const;

/** PoracleNG's tracking type to the nav label that names its list. */
const TYPE_LABEL_KEYS: Record<string, string> = {
  incident: 'NAV.POKESTOP_EVENTS',
  raid: 'NAV.RAIDS',
  egg: 'NAV.RAIDS',
  fort: 'NAV.FORT_CHANGES',
  gym: 'NAV.GYMS',
  invasion: 'NAV.INVASIONS',
  lure: 'NAV.LURES',
  maxbattle: 'NAV.MAX_BATTLES',
  monster: 'NAV.POKEMON',
  nest: 'NAV.NESTS',
  pokemon: 'NAV.POKEMON',
  quest: 'NAV.QUESTS',
};

/**
 * One alarm a place delete is refused for, as the API sends it ("pokemon 37337"), in words: "Pokémon #37337".
 * Anything not in that shape is shown as it came.
 */
export function describeReferencingRule(rule: string, instant: (key: string) => string): string {
  const match = /^(\S+)\s+(\d+)$/.exec(rule.trim());
  const key = match ? TYPE_LABEL_KEYS[match[1].toLowerCase()] : undefined;
  return match && key ? `${instant(key)} #${match[2]}` : rule;
}
