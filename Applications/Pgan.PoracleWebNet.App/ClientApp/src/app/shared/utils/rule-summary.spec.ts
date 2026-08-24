import { cleanRuleSummary, ruleSummaryNeedsExpanding } from './rule-summary';

describe('cleanRuleSummary', () => {
  // Every input below is a string a live PoracleNG returned, not one invented to suit the function.
  it('strips the bold around the species, which the card heading already says', () => {
    expect(cleanRuleSummary('**Bulbasaur**  | distance: 5000m | iv: 90%-100% | cp: 1200-4000 ')).toBe(
      'Bulbasaur | distance: 5000m | iv: 90%-100% | cp: 1200-4000',
    );
  });

  it('handles the quest shape, where the bold is mid-sentence', () => {
    expect(cleanRuleSummary('Reward: **Pikachu** | distance: 500m ')).toBe('Reward: Pikachu | distance: 500m');
  });

  it('keeps the trailing clean flag, which is part of what the rule does', () => {
    expect(cleanRuleSummary('**Mystic gyms** | distance: 150m  clean')).toBe('Mystic gyms | distance: 150m clean');
  });

  it('collapses the doubled space a missing segment leaves behind', () => {
    expect(cleanRuleSummary('**Level 5 raids**  without rsvp updates')).toBe('Level 5 raids without rsvp updates');
  });

  it('leaves the fort description intact rather than trying to prettify its JSON array', () => {
    // Not rendered on a card today, but the utility must not mangle it if it ever is.
    expect(cleanRuleSummary('Fort updates: **pokestop** | distance: 5000m ["name"] ')).toBe(
      'Fort updates: pokestop | distance: 5000m ["name"]',
    );
  });

  it('normalises the spacing around pipes so the separators line up', () => {
    expect(cleanRuleSummary('Grunt type: **Gold-stop**|distance: 1500m   |gender: any  clean')).toBe(
      'Grunt type: Gold-stop | distance: 1500m | gender: any clean',
    );
  });

  it('leaves an underscore that is part of a name alone', () => {
    // Poracle interpolates user-chosen strings -- template names, and the areas and saved-place labels
    // a scope override renders -- straight into this sentence without escaping them. A place called
    // work_gym is not italics, and losing the underscore renames it on the card.
    expect(cleanRuleSummary('**Bulbasaur** | distance: 5000m | template: my_template ')).toBe(
      'Bulbasaur | distance: 5000m | template: my_template',
    );
    expect(cleanRuleSummary('**Pikachu** | areas: north_side, east_side ')).toBe('Pikachu | areas: north_side, east_side');
  });

  it('strips the other Discord emphasis, but only where it is paired', () => {
    expect(cleanRuleSummary('_Bulbasaur_ | distance: 5000m')).toBe('Bulbasaur | distance: 5000m');
    expect(cleanRuleSummary('__Bulbasaur__ | distance: 5000m')).toBe('Bulbasaur | distance: 5000m');
    expect(cleanRuleSummary('*Bulbasaur* | `iv: 90%-100%`')).toBe('Bulbasaur | iv: 90%-100%');
  });

  it('is empty for the absent, null and blank cases, so the card renders nothing', () => {
    expect(cleanRuleSummary(undefined)).toBe('');
    expect(cleanRuleSummary(null)).toBe('');
    expect(cleanRuleSummary('   ')).toBe('');
    expect(cleanRuleSummary('  |  ')).toBe('');
  });
});

describe('ruleSummaryNeedsExpanding', () => {
  it('offers the control on a real Pokemon description, which overflows two lines', () => {
    const live = cleanRuleSummary(
      '**Bulbasaur**  | distance: 5000m | iv: 90%-100% | cp: 1200-4000 | level: 20-35 | stats: 0/0/0 - 15/15/15 | pvp ranking: greatpvp top100 (@0+) | size: XXS-XXL ',
    );

    expect(ruleSummaryNeedsExpanding(live)).toBe(true);
  });

  it('does not offer it on a short one, where there is nothing to expand', () => {
    expect(ruleSummaryNeedsExpanding(cleanRuleSummary('Reward: **Pikachu** | distance: 500m '))).toBe(false);
    expect(ruleSummaryNeedsExpanding(cleanRuleSummary('**Level 5 raids**  without rsvp updates'))).toBe(false);
  });
});
