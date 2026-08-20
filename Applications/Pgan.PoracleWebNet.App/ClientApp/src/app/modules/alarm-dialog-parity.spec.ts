import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A field you can set when creating an alarm, you can change when editing it.
 *
 * Twice in one day a control shipped in an add dialog and not its edit twin: the PVP mega picker, which
 * was nested inside a block that only renders on servers advertising level caps, and the quest minimum
 * amount, which was simply never added. Both produced the same user-visible state — a card describing a
 * setting with no way back to it — and neither was caught by a test, because each dialog's own specs
 * pass perfectly well while the pair disagrees.
 *
 * Read from the templates rather than the DOM: Material mounts only the active tab body, so a rendering
 * test would have to drive tab switches in nine dialogs to assert something that is really about
 * structure. What matters is whether the control is written down, and in which block.
 */
describe('add and edit dialogs offer the same fields', () => {
  const ALARM_TYPES = [
    ['pokemon', 'pokemon/pokemon'],
    ['raid', 'raids/raid'],
    ['quest', 'quests/quest'],
    ['invasion', 'invasions/invasion'],
    ['lure', 'lures/lure'],
    ['nest', 'nests/nest'],
    ['gym', 'gyms/gym'],
    ['fort change', 'fort-changes/fort-change'],
    ['max battle', 'max-battles/max-battle'],
  ] as const;

  /** Controls an add dialog may have that its edit twin does not, and why. */
  const ADD_ONLY: Record<string, string> = {
    'max battle.gmaxOnly': 'The edit dialog exposes the same flag under the name PoracleNG uses for it, `gmax`.',
    'pokemon.forms': 'Multi-select that fans out into one alarm per form. The edit dialog changes the single form of one alarm.',
    'quest.reward':
      'Which item or pokemon the quest must give — the identity of the alarm, not a threshold. Stardust is the ' +
      'exception and is editable as `stardust`, because for that reward type PoracleNG reads `reward` as the floor.',
  };

  /** Controls an edit dialog may have that its add twin does not, and why. */
  const EDIT_ONLY: Record<string, string> = {
    'max battle.gmax': 'Named `gmaxOnly` in the add dialog.',
    'max battle.level': 'The add dialog picks levels with checkboxes and fans out one alarm per level, so it has no single-level control.',
    'quest.stardust': 'The stardust floor, which the add dialog collects on its own tab as `reward`.',
  };

  const CONTROL = /formControl\]="[\w.]*controls\.(\w+)"|formControlName="(\w+)"/g;

  function controlsIn(stem: string, kind: 'add' | 'edit'): Set<string> {
    const file = join(__dirname, `${stem}-${kind}-dialog.component.html`);
    const template = readFileSync(file, 'utf8');
    return new Set([...template.matchAll(CONTROL)].map(m => m[1] ?? m[2]));
  }

  it.each(ALARM_TYPES)('%s: everything the add dialog sets, the edit dialog can change', (name, stem) => {
    const add = controlsIn(stem, 'add');
    const edit = controlsIn(stem, 'edit');

    const unexplained = [...add].filter(c => !edit.has(c) && !(`${name}.${c}` in ADD_ONLY));

    expect(unexplained).toEqual([]);
  });

  it.each(ALARM_TYPES)('%s: everything the edit dialog changes, the add dialog can set', (name, stem) => {
    const add = controlsIn(stem, 'add');
    const edit = controlsIn(stem, 'edit');

    const unexplained = [...edit].filter(c => !add.has(c) && !(`${name}.${c}` in EDIT_ONLY));

    expect(unexplained).toEqual([]);
  });

  it('has no stale exceptions', () => {
    // An exception left behind after the difference is resolved reads as a decision that was made, so
    // the next person keeps it. These must describe a difference that is really there.
    const stale: string[] = [];

    for (const [name, stem] of ALARM_TYPES) {
      const add = controlsIn(stem, 'add');
      const edit = controlsIn(stem, 'edit');

      for (const key of Object.keys(ADD_ONLY)) {
        const [type, control] = [key.slice(0, key.lastIndexOf('.')), key.slice(key.lastIndexOf('.') + 1)];
        if (type === name && (!add.has(control) || edit.has(control))) stale.push(key);
      }
      for (const key of Object.keys(EDIT_ONLY)) {
        const [type, control] = [key.slice(0, key.lastIndexOf('.')), key.slice(key.lastIndexOf('.') + 1)];
        if (type === name && (!edit.has(control) || add.has(control))) stale.push(key);
      }
    }

    expect(stale).toEqual([]);
  });
});
