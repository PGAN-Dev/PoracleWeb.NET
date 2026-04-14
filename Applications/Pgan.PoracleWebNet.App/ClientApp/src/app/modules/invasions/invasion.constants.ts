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
  decoy: 45,
  giovanni: 44,
  mixed: 4,
  sierra: 43,
};

// Some grunt_types have distinct male/female invasion character IDs in PogoAssets.
// Mixed: id 4 (male, starter line) / id 5 (female, Snorlax line).
// Decoy: id 45 (male) / id 46 (female).
// Used by getGruntIconUrl to pick the right icon when an alarm specifies a gender.
export const GENDERED_INVASION_ID: Record<string, { male: number; female: number }> = {
  decoy: { female: 46, male: 45 },
  mixed: { female: 5, male: 4 },
};

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
  if (gruntType in GENDERED_INVASION_ID) {
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

// Niantic's CHARACTER_UNSET — a generic grunt silhouette. Used when an unknown
// grunt_type arrives (e.g. a new Niantic addition this UI hasn't mapped yet) so
// cards render a valid icon instead of a broken image.
export const UNKNOWN_GRUNT_ICON_URL = `${UICONS_BASE}/invasion/0.png`;

export function getGruntIconUrl(gruntType: string | null, gender?: number): string {
  const type = gruntType ?? '';
  const typeId = GRUNT_TYPE_ID[type];
  if (typeId) return `${UICONS_BASE}/type/${typeId}.png`;
  const gendered = GENDERED_INVASION_ID[type];
  if (gendered && gender === 1) return `${UICONS_BASE}/invasion/${gendered.male}.png`;
  if (gendered && gender === 2) return `${UICONS_BASE}/invasion/${gendered.female}.png`;
  const invasionId = GRUNT_INVASION_ID[type];
  if (invasionId) return `${UICONS_BASE}/invasion/${invasionId}.png`;
  return UNKNOWN_GRUNT_ICON_URL;
}
