import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * An alarm with no PVP league must store the rank window's own column defaults, not zero.
 *
 * Both dialogs reset the whole PVP block when no league is chosen, and each reset is a ternary with the
 * default in its else branch. `pvpRankingWorst` got the column default, 4096. `pvpRankingBest` got 0 —
 * and 0 is not a rank. The column defaults to 1, PoracleNG's bot sets 1 explicitly, and both of its API
 * surfaces default to 1 when the field is absent, so every stored 0 came from here, through v1's
 * `flexInt` passing an explicitly sent value straight through. One instance holds 15,383 rules at 0
 * against 7,770 at 1. See jfberry/PoracleNG#227.
 *
 * Asserted against the sources rather than by driving the dialogs, for the same reason as
 * `pvp-controls-parity.spec.ts`: the defect is one half of a sibling pair being written differently
 * from the other, in two places, which is a property of the text. Driving two Material dialogs through
 * a tab switch to observe one number would test the harness more than the fix.
 */
describe('PVP rank window defaults, with no league chosen', () => {
  const read = (name: string) => readFileSync(join(__dirname, name), 'utf8');

  const sources = {
    'add dialog': read('pokemon-add-dialog.component.ts'),
    'edit dialog': read('pokemon-edit-dialog.component.ts'),
  };

  /** The else branch of `pvpRanking<Field>: <league> ? (...) : <here>,`. */
  function noLeagueDefault(source: string, field: string): string {
    const match = new RegExp(String.raw`pvpRanking${field}:[^\n]*\?[^\n]*:\s*([^,\n]+),`).exec(source);
    expect(match).not.toBeNull();
    return match![1].trim();
  }

  it.each(Object.entries(sources))('%s falls back to rank 1, which means no floor', (_name, source) => {
    // 0 is the value that has to stay out. It is not a rank, and PoracleNG refuses it on its v2 write.
    expect(noLeagueDefault(source, 'Best')).toBe('1');
  });

  it.each(Object.entries(sources))('%s falls back to rank 4096 at the other end', (_name, source) => {
    // The sibling that was always right. Pinned so a future edit cannot "fix" the pair by levelling
    // them down to zero instead of up to the column defaults.
    expect(noLeagueDefault(source, 'Worst')).toBe('4096');
  });

  it('agrees between the two dialogs', () => {
    // The original bug was present in both, identically. A fix applied to one of them is not a fix.
    for (const field of ['Best', 'Worst']) {
      expect(noLeagueDefault(sources['add dialog'], field)).toBe(noLeagueDefault(sources['edit dialog'], field));
    }
  });

  it('is what the admin quick-pick editor starts from too', () => {
    // A quick pick's stored filters are overlaid on QuickPickService's defaults, so a 0 here is written
    // to every alarm the pick creates. Same value, third place.
    const editor = readFileSync(join(__dirname, '..', 'quick-picks', 'quick-pick-admin-dialog.component.ts'), 'utf8');

    expect(/pvpRankingBest:\s*\[(\d+)\]/.exec(editor)?.[1]).toBe('1');
    expect(/pvpRankingWorst:\s*\[(\d+)\]/.exec(editor)?.[1]).toBe('4096');
  });
});
