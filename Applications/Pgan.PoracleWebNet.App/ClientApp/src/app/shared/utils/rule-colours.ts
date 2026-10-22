import { DARK_CARD_SURFACE, LIGHT_CARD_SURFACE, readableGraphicOn } from './contrast';

/**
 * The colours an alarm card may carry, and the only place they are defined.
 *
 * Colour on a card is data or nothing: a gym or raid rule takes its team, an egg rule its tier, a
 * single-species Pokémon rule its primary type, and every other card has no edge at all. Each hue is
 * the game's own, moved only as far as it takes to reach 3:1 on the light and the dark card, which is
 * why an edge is a pair rather than one value.
 */
export interface RuleEdge {
  dark: string;
  light: string;
}

/** PoracleNG's "any team" (v1 `team` 4). */
export const TEAM_ANY = 4;

const NEUTRAL = '#9e9e9e';

/** v1 team ids: 0 harmony (uncontested), 1 Mystic, 2 Valor, 3 Instinct, 4 any. */
const TEAM_HUES: Record<number, string> = {
  1: '#1e88e5',
  2: '#e53935',
  3: '#fdd835',
};

/** The game's type colours, keyed by the English type name (see `pokemon-types.ts`). */
const TYPE_HUES: Record<string, string> = {
  Bug: '#a6b91a',
  Dark: '#705746',
  Dragon: '#6f35fc',
  Electric: '#f7d02c',
  Fairy: '#d685ad',
  Fighting: '#c22e28',
  Fire: '#ee8130',
  Flying: '#a98ff3',
  Ghost: '#735797',
  Grass: '#7ac74c',
  Ground: '#e2bf65',
  Ice: '#96d9d6',
  Normal: '#a8a77a',
  Poison: '#a33ea1',
  Psychic: '#f95587',
  Rock: '#b6a136',
  Steel: '#b7b7ce',
  Water: '#6390f0',
};

/**
 * The egg each raid tier hatches from, grouped the way the game draws them (see `raid-level.models.ts`
 * for the tiers): pink 1-2, gold 3-4, legendary 5, mega 6-7, the specials 8-10, shadow 11-15, super
 * mega 16-17, coordinated 18-19.
 */
const TIER_HUES: Record<number, string> = {
  1: '#e8649b',
  2: '#e8649b',
  3: '#f2c30f',
  4: '#f2c30f',
  5: '#8e5bd1',
  6: '#e0602b',
  7: '#e0602b',
  8: '#2aa198',
  9: '#c9a227',
  10: '#c62828',
  11: '#6a3d9a',
  12: '#6a3d9a',
  13: '#6a3d9a',
  14: '#6a3d9a',
  15: '#6a3d9a',
  16: '#d81b60',
  17: '#d81b60',
  18: '#3949ab',
  19: '#3949ab',
};

const cache = new Map<string, RuleEdge>();

function edgeOf(hue: string): RuleEdge {
  let edge = cache.get(hue);
  if (!edge) {
    edge = { dark: readableGraphicOn(hue, DARK_CARD_SURFACE), light: readableGraphicOn(hue, LIGHT_CARD_SURFACE) };
    cache.set(hue, edge);
  }
  return edge;
}

/** Mystic blue, Valor red, Instinct yellow; harmony and "any team" neutral grey. */
export function teamEdge(team: number): RuleEdge {
  return edgeOf(TEAM_HUES[team] ?? NEUTRAL);
}

/** The tier's egg colour; every tier (9000) and a level this build does not know are neutral. */
export function tierEdge(level: number): RuleEdge {
  return edgeOf(TIER_HUES[level] ?? NEUTRAL);
}

/** The species' primary type, or null when its types are not known (the card then has no edge). */
export function speciesEdge(types: readonly string[]): null | RuleEdge {
  const hue = types.length > 0 ? TYPE_HUES[types[0]] : undefined;
  return hue ? edgeOf(hue) : null;
}
