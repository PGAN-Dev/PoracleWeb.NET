export const UICONS_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';

export const GRUNT_TYPE_ID: Record<string, number> = {
  bug: 7,
  dark: 17,
  dragon: 16,
  electric: 13,
  fairy: 18,
  fighting: 2,
  fire: 10,
  flying: 3,
  ghost: 8,
  grass: 12,
  ground: 5,
  ice: 15,
  metal: 9,
  normal: 1,
  poison: 4,
  psychic: 14,
  rock: 6,
  water: 11,
};

export const GRUNT_INVASION_ID: Record<string, number> = {
  arlo: 42,
  cliff: 41,
  darkness: 9,
  decoy: 46,
  giovanni: 44,
  mixed: 4,
  sierra: 43,
};

// Niantic InvasionCharacter enum IDs for grunts that have male/female variants.
// Source: WatWowMap Masterfile-Generator. Used by getGruntIconUrl to pick the right
// PogoAssets `invasion/<id>.png`. Two behavioural groups live together here:
//   1. Gender-fixed grunts (mixed, decoy) — the grunt_type string already encodes a
//      gender-specific NPC (Mixed Male = starter line, Female = Snorlax line;
//      Decoy Male doesn't spawn in-game). The gender dropdown is hidden for these.
//   2. Typed grunts (bug, fire, water, …) — the user picks gender separately via the
//      dropdown. Icon flips live based on the selection.
// The `GENDER_FIXED_GRUNT_TYPES` set below distinguishes the two groups for
// `isGenderFixed` without needing a second map.
export const GENDERED_INVASION_ID: Record<string, { male: number; female: number }> = {
  bug: { female: 6, male: 7 },
  dark: { female: 10, male: 11 },
  darkness: { female: 8, male: 9 },
  decoy: { female: 46, male: 45 },
  dragon: { female: 12, male: 13 },
  electric: { female: 49, male: 50 },
  fairy: { female: 14, male: 15 },
  fighting: { female: 16, male: 17 },
  fire: { female: 18, male: 19 },
  flying: { female: 20, male: 21 },
  ghost: { female: 47, male: 48 },
  grass: { female: 22, male: 23 },
  ground: { female: 24, male: 25 },
  ice: { female: 26, male: 27 },
  metal: { female: 28, male: 29 },
  mixed: { female: 5, male: 4 },
  normal: { female: 30, male: 31 },
  poison: { female: 32, male: 33 },
  psychic: { female: 34, male: 35 },
  rock: { female: 36, male: 37 },
  water: { female: 38, male: 39 },
};

// Grunts whose grunt_type already implies a gender-specific NPC; the gender dropdown
// is hidden for them in the edit dialog.
export const GENDER_FIXED_GRUNT_TYPES: ReadonlySet<string> = new Set(['mixed', 'decoy']);

export const EVENT_TYPE_INFO: Record<string, { color: string; displayName: string; icon: string; imgUrl?: string }> = {
  'gold-stop': { color: '#F9E418', displayName: 'Gold Stop', icon: 'paid' },
  kecleon: { color: '#B3CA78', displayName: 'Kecleon', icon: 'visibility_off', imgUrl: `${UICONS_BASE}/pokemon/352.png` },
  showcase: { color: '#03AEB6', displayName: 'Showcase', icon: 'emoji_events' },
};

export const DISPLAY_NAMES: Record<string, string> = {
  arlo: 'Arlo',
  cliff: 'Cliff',
  darkness: 'Shadow',
  decoy: 'Decoy Grunt',
  everything: 'All Invasions',
  giovanni: 'Giovanni',
  metal: 'Steel',
  mixed: 'Mixed Grunt',
  sierra: 'Sierra',
};

export function getDisplayName(gruntType: string | null, gender?: number): string {
  if (!gruntType) return 'All Invasions';
  const eventInfo = EVENT_TYPE_INFO[gruntType];
  if (eventInfo) return eventInfo.displayName;
  const mapped = DISPLAY_NAMES[gruntType] ?? gruntType.charAt(0).toUpperCase() + gruntType.slice(1);
  if (GENDER_FIXED_GRUNT_TYPES.has(gruntType)) {
    if (gender === 1) return `${mapped} (Male)`;
    if (gender === 2) return `${mapped} (Female)`;
  }
  return mapped;
}

export function isEventType(gruntType: string | null): boolean {
  return (gruntType ?? '') in EVENT_TYPE_INFO;
}

// Rocket Leaders and Giovanni are fixed NPCs with a single invasion character ID each —
// they have no male/female variants, so the gender filter is meaningless for them.
// Event grunts (kecleon, gold-stop, showcase) also have no gender, so `hasNoGenderVariants`
// combines both sets.
export const NO_GENDER_GRUNT_TYPES: ReadonlySet<string> = new Set(['cliff', 'arlo', 'sierra', 'giovanni']);

export function hasNoGenderVariants(gruntType: string | null): boolean {
  const type = gruntType ?? '';
  return isEventType(type) || NO_GENDER_GRUNT_TYPES.has(type);
}

// Mixed and Decoy have gender variants but the gender is implicit in the user's row
// choice (Mixed Male vs Mixed Female), so the gender dropdown should stay hidden for
// them in the edit dialog too — flipping gender would effectively swap the alarm to a
// different NPC (Mixed Male = starter line, Female = Snorlax line).
export function isGenderFixed(gruntType: string | null): boolean {
  const type = gruntType ?? '';
  return hasNoGenderVariants(type) || GENDER_FIXED_GRUNT_TYPES.has(type);
}

// Niantic's CHARACTER_UNSET — a generic grunt silhouette. Used when an unknown
// grunt_type arrives (e.g. a new Niantic addition this UI hasn't mapped yet) so
// cards render a valid icon instead of a broken image.
export const UNKNOWN_GRUNT_ICON_URL = `${UICONS_BASE}/invasion/0.png`;

export const GENDER_ANY = 0;
export const GENDER_MALE = 1;
export const GENDER_FEMALE = 2;

export function getGruntIconUrl(gruntType: string | null, gender?: number | null): string {
  const type = gruntType ?? '';
  const gendered = GENDERED_INVASION_ID[type];
  if (gendered && (gender === GENDER_MALE || gender === GENDER_FEMALE)) {
    return `${UICONS_BASE}/invasion/${gender === GENDER_MALE ? gendered.male : gendered.female}.png`;
  }
  const typeId = GRUNT_TYPE_ID[type];
  if (typeId) return `${UICONS_BASE}/type/${typeId}.png`;
  if (gendered) {
    // Gender-fixed grunt (mixed/decoy) with gender=Any — default to the female variant
    // for decoy (male never spawns) and male for mixed (starter line is the canonical display).
    return `${UICONS_BASE}/invasion/${type === 'decoy' ? gendered.female : gendered.male}.png`;
  }
  const invasionId = GRUNT_INVASION_ID[type];
  if (invasionId) return `${UICONS_BASE}/invasion/${invasionId}.png`;
  return UNKNOWN_GRUNT_ICON_URL;
}
