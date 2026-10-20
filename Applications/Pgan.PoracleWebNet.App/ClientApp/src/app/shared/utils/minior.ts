/**
 * Minior's IVs, CP and level are rolled again for each player when it's caught (it breaks out of its
 * Meteor form into its Core form), so the stats a scanner reads from the wild encounter don't describe
 * what anyone catches. A scanner that knows this reports Minior with no stats, and an IV, CP or level
 * filter then never matches it. Pokemon alert dialogs say so when Minior is picked.
 */
export const MINIOR_POKEMON_ID = 774;

export function includesMinior(ids: Iterable<number>): boolean {
  for (const id of ids) {
    if (id === MINIOR_POKEMON_ID) return true;
  }
  return false;
}
