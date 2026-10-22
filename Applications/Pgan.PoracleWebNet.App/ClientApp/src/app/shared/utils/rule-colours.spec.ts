import { DARK_CARD_SURFACE, LIGHT_CARD_SURFACE, contrastRatio } from './contrast';
import { POKEMON_TYPE_NAMES_BY_ID } from './pokemon-types';
import { RuleEdge, TEAM_ANY, speciesEdge, teamEdge, tierEdge, typeEdge } from './rule-colours';

/**
 * Colour on an alarm card is data: a gym or raid team, an egg's tier, a species' primary type. The
 * edge is a 4px stripe, a graphical object, so it has to reach 3:1 against the card it sits on in
 * both themes or the data it carries is not there for a good part of the people reading it.
 */
describe('rule card edges', () => {
  const edges: [string, RuleEdge][] = [
    ...[0, 1, 2, 3, 4].map((team): [string, RuleEdge] => [`team ${team}`, teamEdge(team)]),
    ...[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 9000, 42].map((level): [string, RuleEdge] => [
      `tier ${level}`,
      tierEdge(level),
    ]),
    ...Object.values(POKEMON_TYPE_NAMES_BY_ID).map((type): [string, RuleEdge] => [type, speciesEdge([type])!]),
  ];

  it.each(edges)('%s reads at 3:1 on the light card and on the dark card', (_name, edge) => {
    expect(contrastRatio(edge.light, LIGHT_CARD_SURFACE)).toBeGreaterThanOrEqual(3);
    expect(contrastRatio(edge.dark, DARK_CARD_SURFACE)).toBeGreaterThanOrEqual(3);
  });

  it('gives the three teams three different colours and "any" a neutral one', () => {
    const mystic = teamEdge(1).light;
    const valor = teamEdge(2).light;
    const instinct = teamEdge(3).light;
    expect(new Set([mystic, valor, instinct, teamEdge(TEAM_ANY).light]).size).toBe(4);
    // Harmony (0) is an uncontested gym, not a team, so it reads like "any".
    expect(teamEdge(0)).toEqual(teamEdge(TEAM_ANY));
  });

  it('keeps a hue where it already reads, and only deepens the ones that do not', () => {
    // Valor's red clears 3:1 on both cards as it is.
    expect(teamEdge(2)).toEqual({ dark: '#e53935', light: '#e53935' });
    // Instinct's yellow is 1.4:1 on the light card, so the light theme gets a deeper gold.
    expect(teamEdge(3).dark).toBe('#fdd835');
    expect(teamEdge(3).light).not.toBe('#fdd835');
  });

  it('colours a species by its primary type, and nothing when the types are unknown', () => {
    expect(speciesEdge(['Fire', 'Flying'])).toEqual(speciesEdge(['Fire']));
    expect(speciesEdge([])).toBeNull();
    expect(speciesEdge(['NotAType'])).toBeNull();
  });

  it('gives a weather-boosted type the same colour its species card carries (the dashboard reads it here)', () => {
    for (const type of Object.values(POKEMON_TYPE_NAMES_BY_ID)) {
      expect(typeEdge(type)).toEqual(speciesEdge([type]));
    }
    expect(typeEdge('Shadow')).toBeNull();
  });

  it('groups tiers the way the game draws its eggs', () => {
    expect(tierEdge(1)).toEqual(tierEdge(2));
    expect(tierEdge(3)).toEqual(tierEdge(4));
    expect(tierEdge(5)).not.toEqual(tierEdge(3));
    expect(tierEdge(11)).toEqual(tierEdge(15));
    // Every tier, and a level this build does not know, are neutral rather than a guess.
    expect(tierEdge(9000)).toEqual(teamEdge(TEAM_ANY));
    expect(tierEdge(42)).toEqual(teamEdge(TEAM_ANY));
  });
});
