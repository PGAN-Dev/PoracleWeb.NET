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
  decoy: 50,
  giovanni: 44,
  mixed: 41,
};

export const EVENT_TYPE_INFO: Record<string, { color: string; displayName: string; icon: string; imgUrl?: string }> = {
  'gold-stop': { color: '#F9E418', displayName: 'Gold Stop', icon: 'paid' },
  kecleon: { color: '#B3CA78', displayName: 'Kecleon', icon: 'visibility_off', imgUrl: `${UICONS_BASE}/pokemon/352.png` },
  showcase: { color: '#03AEB6', displayName: 'Showcase', icon: 'emoji_events' },
};

export const DISPLAY_NAMES: Record<string, string> = {
  decoy: 'Decoy Grunt',
  everything: 'All Invasions',
  giovanni: 'Giovanni',
  metal: 'Steel',
  mixed: 'Rocket Leader',
};

export function getDisplayName(gruntType: string | null): string {
  if (!gruntType) return 'All Invasions';
  const eventInfo = EVENT_TYPE_INFO[gruntType];
  if (eventInfo) return eventInfo.displayName;
  const mapped = DISPLAY_NAMES[gruntType];
  if (mapped) return mapped;
  return gruntType.charAt(0).toUpperCase() + gruntType.slice(1);
}

export function isEventType(gruntType: string | null): boolean {
  return (gruntType ?? '') in EVENT_TYPE_INFO;
}

export function getGruntIconUrl(gruntType: string | null): string {
  const type = gruntType ?? '';
  const typeId = GRUNT_TYPE_ID[type];
  if (typeId) return `${UICONS_BASE}/type/${typeId}.png`;
  const invasionId = GRUNT_INVASION_ID[type];
  if (invasionId) return `${UICONS_BASE}/invasion/${invasionId}.png`;
  return '';
}
