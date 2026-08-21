/**
 * The 18 Pokemon types, keyed by the type ids the game (and PoracleNG's masterdata) use.
 *
 * These English names are identity, not display text: uicons files are named after the id, alarm
 * filters compare by name, and the type filter chips track by name. PoracleNG returns type names
 * already translated into the requested locale, so the localized string is kept apart from this and
 * used only for rendering — see `MasterDataService.getTypeLabel`.
 */
export const POKEMON_TYPE_NAMES_BY_ID: Record<number, string> = {
  1: 'Normal',
  2: 'Fighting',
  3: 'Flying',
  4: 'Poison',
  5: 'Ground',
  6: 'Rock',
  7: 'Bug',
  8: 'Ghost',
  9: 'Steel',
  10: 'Fire',
  11: 'Water',
  12: 'Grass',
  13: 'Electric',
  14: 'Psychic',
  15: 'Ice',
  16: 'Dragon',
  17: 'Dark',
  18: 'Fairy',
};

/** The inverse of {@link POKEMON_TYPE_NAMES_BY_ID}: English type name to type id. */
export const POKEMON_TYPE_IDS: Record<string, number> = Object.fromEntries(
  Object.entries(POKEMON_TYPE_NAMES_BY_ID).map(([id, name]) => [name, Number(id)]),
);
