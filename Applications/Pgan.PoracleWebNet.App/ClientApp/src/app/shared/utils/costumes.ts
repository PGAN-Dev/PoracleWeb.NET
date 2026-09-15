/**
 * Costume filter sentinels, the twin of the backend's `Monster.Costume` / `Raid.Costume` defaults.
 *
 * Costumed spawns arrive as form 598 "Normal", so a form filter can neither target nor exclude them.
 * PoracleNG stores the costume filter as an int with two reserved values, and getting them the wrong
 * way round is silent: sending 0 for "any" narrows every rule to plain spawns only.
 */

/**
 * Gated on the server's applied migration, not on its version string.
 *
 * The costume columns arrive with PoracleNG migrations 6 (`monsters.costume`) and 7 (`raid.costume`),
 * and a server without them accepts the extra key, answers 200 and drops it -- verified against a live
 * 5.1.0 instance. Nothing breaks; "Halloween 2025" just quietly matches every Pikachu. The four dialog
 * controls therefore ask `SettingsService.supportsCostume()` before rendering, and the alarm services
 * refuse a narrower costume than the wildcard on a server that cannot store it. Both fail closed.
 */

/** Matches every spawn, costumed or not. What PoracleNG stores when the key is absent. */
export const ANY_COSTUME = 9000;

/** Matches only spawns wearing no costume. A real filter, not an "unset" marker. */
export const NO_COSTUME = 0;

/**
 * The hint shown under the costume select. The three states are mutually exclusive and none of them
 * is obvious from the option text alone, so the hint says what the rule will actually do.
 *
 * A missing name list wins over the selection: the person needs to know why the named costumes are
 * gone before they need to know what their current choice means.
 */
export function costumeHintKey(costume: number, namesAvailable: boolean): string {
  if (!namesAvailable) return 'POKEMON.COSTUME_HINT_UNAVAILABLE';
  if (costume === ANY_COSTUME) return 'POKEMON.COSTUME_HINT_ANY';
  if (costume === NO_COSTUME) return 'POKEMON.COSTUME_HINT_NONE';
  return 'POKEMON.COSTUME_HINT_SPECIFIC';
}
