import { QuickPickDefinition } from '../../core/models';

/**
 * The built-in quick picks QuickPickService seeds, by id, with the English text it stores.
 *
 * The seed is stored in English, so every locale showed English names and descriptions. A built-in whose
 * stored text still matches the seed is shown from QUICK_PICKS.BUILT_IN in the display language; once an
 * admin edits it, or for any pick an admin or user wrote, the stored text is shown as typed.
 * quick-pick-labels.spec.ts checks this table against the C# seed, so the two cannot drift apart.
 */
export const BUILT_IN_QUICK_PICKS: Record<string, { description: string; name: string }> = {
  'raid-1star': { name: 'All 1-Star Raids', description: 'Track all 1-star raid bosses' },
  'raid-3star': { name: 'All 3-Star Raids', description: 'Track all 3-star raid bosses' },
  'raid-5star': { name: 'All 5-Star Raids', description: 'Track all legendary and mythical raid bosses' },
  'raid-ex': { name: 'EX Eligible Raids', description: 'Track raids at EX-eligible gyms' },
  'raid-mega': { name: 'All Mega Raids', description: 'Track all Mega and Primal raid bosses' },
  'raid-shadow': { name: 'All Shadow Raids', description: 'Track all shadow raid bosses' },
  'all-invasions': { name: 'All Invasions', description: 'Track all Team Rocket grunt and leader invasions' },
  'egg-5star': { name: '5-Star Eggs', description: 'Track legendary raid eggs' },
  'egg-mega': { name: 'Mega Eggs', description: 'Track Mega raid eggs' },
  'high-cp': { name: '3000+ CP Pokemon', description: 'Track strong wild spawns for gym defense' },
  'high-iv': { name: '90%+ IV Pokemon', description: 'Track all Pokemon with 90% IV or higher' },
  'high-level': { name: 'Level 30+ Pokemon', description: 'Track all high-level wild spawns (weather boosted)' },
  hundo: { name: '100% IV Pokemon', description: 'Track all perfect IV wild spawns' },
  'invasion-giovanni': { name: 'Giovanni', description: 'Track Giovanni boss encounters' },
  'invasion-leader': { name: 'Rocket Leaders', description: 'Track Sierra, Cliff, and Arlo encounters' },
  'lure-glacial': { name: 'Glacial Lures', description: 'Track Glacial Lure Modules at PokeStops' },
  'lure-golden': { name: 'Golden Lures', description: 'Track Golden Lure Modules at PokeStops' },
  'lure-magnetic': { name: 'Magnetic Lures', description: 'Track Magnetic Lure Modules at PokeStops' },
  'lure-mossy': { name: 'Mossy Lures', description: 'Track Mossy Lure Modules at PokeStops' },
  'lure-rainy': { name: 'Rainy Lures', description: 'Track Rainy Lure Modules at PokeStops' },
  nundo: { name: '0% IV Pokemon', description: 'Track all zero IV wild spawns' },
  'pvp-great-1': { name: 'PvP Great Rank 1', description: 'Track rank 1 Pokemon for Great League (1500 CP)' },
  'pvp-great-10': { name: 'PvP Great Top 10', description: 'Track top 10 ranked Pokemon for Great League' },
  'pvp-little-1': { name: 'PvP Little Rank 1', description: 'Track rank 1 Pokemon for Little Cup (500 CP)' },
  'pvp-ultra-1': { name: 'PvP Ultra Rank 1', description: 'Track rank 1 Pokemon for Ultra League (2500 CP)' },
  'pvp-ultra-10': { name: 'PvP Ultra Top 10', description: 'Track top 10 ranked Pokemon for Ultra League' },
  'quest-pokemon': { name: 'Pokemon Encounter Quests', description: 'Track field research rewarding Pokemon encounters' },
  'quest-rare-candy': { name: 'Rare Candy Quests', description: 'Track field research rewarding rare candy' },
  'quest-stardust': { name: 'Stardust Quests', description: 'Track field research rewarding stardust' },
  xxl: { name: 'XXL Pokemon', description: 'Track all jumbo sized Pokemon for the XXL medal' },
  xxs: { name: 'XXS Pokemon', description: 'Track all tiny Pokemon for the XXS medal' },
};

/** Anything that translates a key: I18nService or TranslateService. */
interface Translator {
  instant(key: string, params?: Record<string, unknown>): string;
}

function builtInKey(id: string): string {
  return `QUICK_PICKS.BUILT_IN.${id.toUpperCase().replace(/-/g, '_')}`;
}

function seeded(definition: QuickPickDefinition): undefined | { description: string; name: string } {
  return definition.scope === 'global' ? BUILT_IN_QUICK_PICKS[definition.id] : undefined;
}

function translated(key: string, fallback: string, translate: Translator): string {
  const value = translate.instant(key);
  return value && value !== key ? value : fallback;
}

export function quickPickName(definition: QuickPickDefinition, translate: Translator): string {
  const seed = seeded(definition);
  if (!seed || seed.name !== definition.name) return definition.name;
  return translated(`${builtInKey(definition.id)}.NAME`, definition.name, translate);
}

export function quickPickDescription(definition: QuickPickDefinition, translate: Translator): string {
  const seed = seeded(definition);
  if (!seed || seed.description !== definition.description) return definition.description;
  return translated(`${builtInKey(definition.id)}.DESCRIPTION`, definition.description, translate);
}

/** The display name of an alarm type as quick picks store it ('monster', 'raid', 'maxbattle'...). */
export function quickPickAlarmTypeLabel(alarmType: string, translate: Translator): string {
  return translated(`QUICK_PICKS.ALARM_TYPES.${alarmType.toUpperCase()}`, alarmType, translate);
}
