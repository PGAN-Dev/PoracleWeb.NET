/**
 * Costume filter sentinels, the twin of the backend's `Monster.Costume` / `Raid.Costume` defaults.
 *
 * Costumed spawns arrive as form 598 "Normal", so a form filter can neither target nor exclude them.
 * PoracleNG stores the costume filter as an int with two reserved values, and getting them the wrong
 * way round is silent: sending 0 for "any" narrows every rule to plain spawns only.
 */

/**
 * NOT YET GATED ON SERVER CAPABILITY.
 *
 * The costume columns arrive with PoracleNG migrations 6 and 7, i.e. 5.2.x. `PoracleServerProfile`
 * already reads the applied migration number and `HasSchema()` already answers the question, but the
 * `PoracleCapabilityKeys.MonsterCostume` / `RaidCostume` constants that name it live on
 * feat/poracleng-two-branch-support and are not in this tree. Until they land, a self-hoster on 5.1.0
 * sees a costume control that does nothing: verified against a live 5.1.0 instance, it accepts the
 * extra key, answers 200 and drops it, so nothing else breaks -- but "Halloween 2025" quietly matches
 * every Pikachu. Wrap the four dialog controls in that capability check before release.
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
