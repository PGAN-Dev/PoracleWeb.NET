import { cleanRuleSummary } from './rule-summary';

/**
 * The name rules a saved place's label shares with the API (`PlaceLabelRules`, which reuses
 * `ProfileNameRules`' character rule).
 */

/** `user_locations.label` is varchar(64). */
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
  incident: 'WHERE.RULE_TYPE_INCIDENT',
  raid: 'QUICK_PICKS.ALARM_TYPES.RAID',
  egg: 'QUICK_PICKS.ALARM_TYPES.EGG',
  fort: 'WHERE.RULE_TYPE_FORT',
  gym: 'QUICK_PICKS.ALARM_TYPES.GYM',
  invasion: 'QUICK_PICKS.ALARM_TYPES.INVASION',
  lure: 'QUICK_PICKS.ALARM_TYPES.LURE',
  maxbattle: 'QUICK_PICKS.ALARM_TYPES.MAXBATTLE',
  monster: 'QUICK_PICKS.ALARM_TYPES.MONSTER',
  nest: 'QUICK_PICKS.ALARM_TYPES.NEST',
  pokemon: 'QUICK_PICKS.ALARM_TYPES.MONSTER',
  quest: 'QUICK_PICKS.ALARM_TYPES.QUEST',
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

/** One alarm from the usage read, in words: the singular type, then PoracleNG's summary or the rule number. */
/**
 * PoracleNG's description is chat markdown and every filter in one line ("**Pikachu** | distance: 1000m |
 * iv: 0%-100% | cp: 0-9000 | ..."). The list keeps its first part, the subject, without the markdown, and
 * says the radius from the place the way the scope chip does: "Pokémon: Pikachu · Within 1 km of Work".
 */
export function describeUsageRule(
  rule: { description: null | string; type: string; uid: number },
  instant: (key: string, params?: Record<string, unknown>) => string,
  place?: string,
): string {
  const key = TYPE_LABEL_KEYS[rule.type.toLowerCase()];
  const type = key ? instant(key) : rule.type;
  if (!rule.description) return `${type} #${rule.uid}`;

  const parts = cleanRuleSummary(rule.description).split(' | ');
  const subject = parts[0];
  const metres = parts.map(part => /^distance:\s*(\d+(?:\.\d+)?)\s*m$/i.exec(part)).find(match => match)?.[1];
  const line = `${type}: ${subject}`;
  if (!metres || !place || Number(metres) <= 0) return line;

  const distance = `${Math.round(Number(metres)) / 1000}`;
  return `${line} · ${instant('WHERE.NEAR_PLACE', { distance, place })}`;
}
