/**
 * A stable display order for an alarm list, independent of the rule id.
 *
 * PoracleNG returns tracking rows in id order, and nine of the eleven lists rendered that order straight
 * through. On PoracleNG 5.2.x an edit is a replace: the rule comes back under a new, higher id, so the
 * card the user just saved jumped to the end of the grid and, on a long list, off the screen — with
 * nothing to say it had moved. Since jfberry/PoracleNG #303 a v2 replace keeps the id, but a failed
 * reassignment on the server still hands back a new one, so id order is not a dependable "where the card
 * was" signal on any server.
 *
 * Ordering on the rule's own content instead keeps a card where it was, and is a better order than
 * insertion sequence regardless. The keys are the fields the card is titled by, in their raw form: dex
 * number rather than species name, reward type rather than reward name. Sorting on the resolved name
 * would mean re-ordering the grid once the master data and the gym names arrive, which is a worse flicker
 * than the problem being fixed — and the resolved name changes with the display language, so the order
 * would too.
 *
 * The pokemon list already sorts on its own controls. Invasion does not use this helper: it was left out
 * when its edits still went through PoracleNG's v1 write surface, but every type's edit now goes through
 * v2 where the server supports it, so on 5.2.x an edited invasion card moves like the others did.
 * Pokestop events are here too — that type has only ever had a v2
 * surface, so on 5.2.x its ids rotated on every edit; with #303 they normally stay put, and content order
 * is still the stable choice.
 */
export function orderAlarms<T extends { uid: number }>(
  items: readonly T[],
  key: (item: T) => readonly (number | string | null | undefined)[],
): T[] {
  return [...items].sort((a, b) => {
    const left = key(a);
    const right = key(b);

    for (let i = 0; i < left.length; i++) {
      const difference = compare(left[i], right[i]);
      if (difference !== 0) {
        return difference;
      }
    }

    // Two rules that are alike in everything the card shows. The id is the last tiebreak, so the order is
    // total and a re-render cannot shuffle them against each other.
    return a.uid - b.uid;
  });
}

function compare(a: number | string | null | undefined, b: number | string | null | undefined): number {
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }

  return String(a ?? '').localeCompare(String(b ?? ''));
}
