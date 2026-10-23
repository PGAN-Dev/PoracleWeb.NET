/** The IconService methods a reward picture is built from. */
export interface RewardIconSource {
  getItemUrl(id: number): string;
  getPokemonUrl(id: number): string;
  getRewardUrl(type: string, id: number): string;
}

/** UICONS reward folders for the kinds drawn by one picture, whatever the amount (`<folder>/0.png`). */
const ONE_PICTURE: Record<number, string> = {
  1: 'experience',
  3: 'stardust',
  4: 'candy',
  8: 'pokecoin',
  9: 'xl_candy',
  12: 'mega_resource',
};

/** Reward types whose species, when one is set, is drawn as the Pokémon: an encounter, candy, mega energy. */
const SPECIES_TYPES = new Set([4, 7, 12]);

/**
 * The picture for a quest rule's reward, or '' for an encounter with any Pokémon (the template draws its
 * own icon). UICONS has one picture for stardust and PokéCoins (`stardust/0.png`, `pokecoin/0.png`);
 * asking for `stardust/{amount}.png` or `quest/8.png` found nothing and drew the "?" fallback.
 */
export function questRewardIcon(quest: { pokemonId: number; reward: number; rewardType: number }, icons: RewardIconSource): string {
  // An encounter's species may be in pokemonId or in reward.
  const pokemonId = quest.pokemonId > 0 ? quest.pokemonId : quest.reward;
  if (SPECIES_TYPES.has(quest.rewardType) && pokemonId > 0) return icons.getPokemonUrl(pokemonId);
  if (quest.rewardType === 7) return '';
  if (quest.rewardType === 2) return icons.getItemUrl(quest.reward > 0 ? quest.reward : 0);
  const folder = ONE_PICTURE[quest.rewardType];
  return icons.getRewardUrl(folder ?? 'quest', 0);
}
