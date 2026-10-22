import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A nest rule for any Pokémon (id 0) drew a broken sprite beside "All Pokemon", where the Pokémon list
 * draws the select_all icon beside "All Pokémon". Both lists now draw the same placeholder.
 */
describe('nest card for any Pokémon', () => {
  const template = (file: string): string => readFileSync(join(__dirname, file), 'utf8').replace(/\s+/g, ' ');

  it('draws the same icon placeholder as the Pokémon list', () => {
    const placeholder = '<div ruleMedia class="rule-icon"><mat-icon>select_all</mat-icon></div>';
    expect(template('../pokemon/pokemon-list.component.html')).toContain(placeholder);
    expect(template('nest-list.component.html')).toContain('@if (nest.pokemonId === 0) { ' + placeholder);
  });
});
