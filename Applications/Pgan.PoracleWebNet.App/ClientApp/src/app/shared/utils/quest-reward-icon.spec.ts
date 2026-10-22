import { questRewardIcon } from './quest-reward-icon';

/**
 * UICONS keeps one picture per reward kind for stardust and PokéCoins (`reward/stardust/0.png`,
 * `reward/pokecoin/0.png`). The cards asked for `stardust/{amount}.png` and `quest/8.png`, neither of which
 * exists, so both drew the "?" fallback. Every kind without a per-id picture now asks for its 0.png.
 */
describe('questRewardIcon', () => {
  const icons = {
    getItemUrl: (id: number) => `R/item/${id}.png`,
    getPokemonUrl: (id: number) => `P/${id}.png`,
    getRewardUrl: (type: string, id: number) => `R/${type}/${id}.png`,
  };
  const quest = (rewardType: number, reward = 0, pokemonId = 0) => ({ pokemonId, reward, rewardType });

  it.each([
    ['stardust with a minimum', quest(3, 1500), 'R/stardust/0.png'],
    ['stardust, any amount', quest(3), 'R/stardust/0.png'],
    ['PokéCoins', quest(8, 50), 'R/pokecoin/0.png'],
    ['an item', quest(2, 1301), 'R/item/1301.png'],
    ['any item', quest(2), 'R/item/0.png'],
    ['a Pokémon encounter', quest(7, 0, 25), 'P/25.png'],
    ['an encounter stored in reward', quest(7, 150), 'P/150.png'],
    ['candy for a species', quest(4, 0, 1), 'P/1.png'],
    ['any candy', quest(4), 'R/candy/0.png'],
    ['any mega energy', quest(12), 'R/mega_resource/0.png'],
    ['XL candy', quest(9), 'R/xl_candy/0.png'],
    ['experience', quest(1), 'R/experience/0.png'],
    ['a kind with no picture of its own', quest(99), 'R/quest/0.png'],
  ])('%s', (_label, q, url) => {
    expect(questRewardIcon(q, icons)).toBe(url);
  });

  it('leaves any Pokémon encounter to the template’s own icon', () => {
    expect(questRewardIcon(quest(7), icons)).toBe('');
  });
});
