import { describeUsageRule } from './place-label';

describe('describeUsageRule', () => {
  const instant = (key: string, params?: Record<string, unknown>): string =>
    key === 'WHERE.NEAR_PLACE'
      ? `Within ${params?.['distance']} km of ${params?.['place']}`
      : key === 'QUICK_PICKS.ALARM_TYPES.MONSTER'
        ? 'Pokémon'
        : key;

  // What PoracleNG's include_descriptions sends for a Pokémon rule, verbatim from the 5.4.0 bed.
  const pikachu = '**Pikachu** | distance: 1000m | iv: 0%-100% | cp: 0-9000 | level: 0-55 | stats: 0/0/0 - 15/15/15 | size: XXS-XXL';

  it('names the subject and the radius from the place, without the markdown or the filter dump', () => {
    expect(describeUsageRule({ uid: 1, description: pikachu, type: 'pokemon' }, instant, 'Work')).toBe(
      'Pokémon: Pikachu · Within 1 km of Work',
    );
  });

  it('keeps a radius that is not a whole kilometre', () => {
    const line = describeUsageRule({ uid: 1, description: '**Mewtwo** | distance: 2500m', type: 'pokemon' }, instant, 'Gym');
    expect(line).toBe('Pokémon: Mewtwo · Within 2.5 km of Gym');
  });

  it('shows a description with no radius in it as its first part, cleaned', () => {
    expect(describeUsageRule({ uid: 1, description: '__Level 5__ | team: any', type: 'pokemon' }, instant, 'Work')).toBe(
      'Pokémon: Level 5',
    );
  });

  it('still shows a plain description as it came', () => {
    expect(describeUsageRule({ uid: 1, description: 'Pikachu 90%+', type: 'pokemon' }, instant, 'Work')).toBe('Pokémon: Pikachu 90%+');
  });

  it('falls back to the rule number without a description', () => {
    expect(describeUsageRule({ uid: 9, description: null, type: 'raid' }, instant, 'Work')).toBe('QUICK_PICKS.ALARM_TYPES.RAID #9');
  });
});
