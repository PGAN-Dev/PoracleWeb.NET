import { LevelOption, resolveLevel } from '../../core/models/raid-level.models';

/**
 * The words an alarm is called by, in one place. The Raids, Lures, Gyms, Max battles, Fort changes and Quests
 * pages and the Profiles overview all print a title for the same rule; each kept its own copy and the copies
 * drifted ("All Legendary raids" on one page, "All level 5 raids" on the other). Every one of them calls
 * these now, so a rule has one name wherever it is listed.
 */
export type Translate = (key: string, params?: Record<string, unknown>) => string;

/** What a title needs to look up besides translations. */
export interface NameLookups {
  itemName(id: number): string;
  pokemonName(id: number): string;
}

/** The fields of a quest rule a title is built from, in the list model's camelCase. */
export interface QuestTitleFields {
  amount: number;
  pokemonId: number;
  reward: number;
  rewardType: number;
}

/** PoracleNG's "every Pokémon / every tier" sentinel. */
const ANY = 9000;

export function eggTitle(level: number, t: Translate, live?: ReadonlyMap<number, LevelOption>): string {
  return `${raidLevelName(level, t, live)} ${t('RAIDS.EGG_SUFFIX')}`;
}

export function fortTypeName(type: null | string | undefined, t: Translate): string {
  switch (type) {
    case 'gym':
      return t('FORT_CHANGES.FORT_GYM');
    case 'pokestop':
      return t('FORT_CHANGES.FORT_POKESTOP');
    default:
      return t('FORT_CHANGES.FORT_EVERYTHING');
  }
}

export function gymTeamName(team: number, t: Translate): string {
  switch (team) {
    case 0:
      return t('GYMS.TEAM_NEUTRAL');
    case 1:
      return t('GYMS.TEAM_MYSTIC');
    case 2:
      return t('GYMS.TEAM_VALOR');
    case 3:
      return t('GYMS.TEAM_INSTINCT');
    case 4:
      // "Any team": the same words raids and eggs use for this value.
      return t('RAIDS.TEAM_ANY');
    default:
      return t('GYMS.TEAM_UNKNOWN', { id: team });
  }
}

export function lureName(id: number, t: Translate): string {
  switch (id) {
    case 501:
      return t('LURES.TYPE_NORMAL');
    case 502:
      return t('LURES.TYPE_GLACIAL');
    case 503:
      return t('LURES.TYPE_MOSSY');
    case 504:
      return t('LURES.TYPE_MAGNETIC');
    case 505:
      return t('LURES.TYPE_RAINY');
    case 506:
      return t('LURES.TYPE_GOLDEN');
    default:
      return t('LURES.TYPE_UNKNOWN', { id });
  }
}

/** "Golden Lure": the lure's name and the noun, as the Lures page prints it. */
export function lureTitle(id: number, t: Translate): string {
  return `${lureName(id, t)} ${t('LURES.LURE_SUFFIX')}`;
}

export function maxBattleTitle(pokemonId: null | number | undefined, t: Translate, lookups: NameLookups): string {
  return pokemonId && pokemonId !== ANY ? lookups.pokemonName(pokemonId) : t('MAX_BATTLES.ANY_POKEMON');
}

export function questTitle(quest: QuestTitleFields, t: Translate, lookups: NameLookups): string {
  const reward = describeQuestReward(quest, t, lookups);
  // The minimum only applies to the rewards that come in quantities, and saying "1x" for a rule that
  // asks for one of something is noise.
  return quest.amount > 1 ? t('QUESTS.AMOUNT_PREFIX', { count: quest.amount, reward }) : reward;
}

/** The tier's own name ("Legendary", "Mega", "3 star"), from the live list when the API has extended it. */
export function raidLevelName(level: number, t: Translate, live?: ReadonlyMap<number, LevelOption>): string {
  const opt = live?.get(level) ?? resolveLevel(level);
  return opt.category === 'custom' ? `${t(opt.labelKey)} ${opt.value}` : t(opt.labelKey);
}

export function raidTitle(
  raid: { level: number; pokemonId?: null | number },
  t: Translate,
  lookups: NameLookups,
  live?: ReadonlyMap<number, LevelOption>,
): string {
  if (raid.pokemonId && raid.pokemonId !== ANY) return lookups.pokemonName(raid.pokemonId);
  return raid.level === ANY ? t('RAIDS.ALL_RAIDS') : t('RAIDS.ALL_LEVEL_RAIDS', { level: raidLevelName(raid.level, t, live) });
}

function describeQuestReward(quest: QuestTitleFields, t: Translate, lookups: NameLookups): string {
  // Pokemon encounter: the id may be in pokemonId or the reward field.
  const pokemonId = quest.pokemonId > 0 ? quest.pokemonId : quest.reward;
  if (quest.rewardType === 7 && pokemonId > 0) return lookups.pokemonName(pokemonId);
  if (quest.rewardType === 7 && pokemonId === 0) return t('QUESTS.ANY_POKEMON_ENCOUNTER');
  if (quest.rewardType === 12 && pokemonId > 0) return t('QUESTS.MEGA_ENERGY_SUFFIX', { name: lookups.pokemonName(pokemonId) });
  if (quest.rewardType === 4 && pokemonId > 0) return t('QUESTS.CANDY_SUFFIX', { name: lookups.pokemonName(pokemonId) });
  if (quest.rewardType === 2) {
    // reward 0 is the dialog default, meaning any item. getItemName has no id 0, so this used to
    // render as "Item #0" -- the Pokemon branch above already has the equivalent "any" case.
    return quest.reward > 0 ? lookups.itemName(quest.reward) : t('QUESTS.ANY_ITEM');
  }
  if (quest.rewardType === 3) {
    return quest.reward > 0 ? t('QUESTS.STARDUST_AMOUNT', { amount: quest.reward }) : t('QUESTS.STARDUST');
  }
  // Ungated on purpose: a pokecoin rule set with the bot, or left behind by a PoracleNG downgrade,
  // still has to be readable and deletable here. Only creating one asks whether the server can.
  if (quest.rewardType === 8) {
    return quest.reward > 0 ? t('QUESTS.POKECOINS_AMOUNT', { amount: quest.reward }) : t('QUESTS.POKECOINS');
  }
  return questRewardTypeLabel(quest.rewardType, t);
}

/** The reward family's name, which a title falls back to and the Quests page also filters and sorts by. */
export function questRewardTypeLabel(rewardType: number, t: Translate): string {
  switch (rewardType) {
    case 12:
      return t('QUESTS.REWARD_MEGA_ENERGY');
    case 2:
      return t('QUESTS.REWARD_ITEM');
    case 3:
      return t('QUESTS.STARDUST');
    case 4:
      return t('QUESTS.REWARD_CANDY');
    case 7:
      return t('QUESTS.REWARD_POKEMON');
    case 8:
      return t('QUESTS.POKECOINS');
    default:
      return t('QUESTS.REWARD_TYPE_PREFIX', { type: rewardType });
  }
}
