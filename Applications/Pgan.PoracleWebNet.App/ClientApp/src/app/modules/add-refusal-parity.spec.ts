import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every add dialog keeps a refused single Add open with the reason inline, and every list that opens one
 * can describe the clashing rule and open it for editing.
 *
 * Wiring one dialog of ten and leaving the rest closing on a toast is the regression shape this codebase
 * keeps hitting, so the sources are read rather than ten dialogs mounted.
 */
describe('refused Adds stay in the dialog on every alarm type', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  /** [add dialog, its list]. Raids and eggs share one dialog and one list. */
  const PAIRS: [string, string][] = [
    ['pokemon/pokemon-add-dialog.component', 'pokemon/pokemon-list.component'],
    ['raids/raid-add-dialog.component', 'raids/raid-list.component'],
    ['quests/quest-add-dialog.component', 'quests/quest-list.component'],
    ['invasions/invasion-add-dialog.component', 'invasions/invasion-list.component'],
    ['lures/lure-add-dialog.component', 'lures/lure-list.component'],
    ['nests/nest-add-dialog.component', 'nests/nest-list.component'],
    ['gyms/gym-add-dialog.component', 'gyms/gym-list.component'],
    ['max-battles/max-battle-add-dialog.component', 'max-battles/max-battle-list.component'],
    ['fort-changes/fort-change-add-dialog.component', 'fort-changes/fort-change-list.component'],
    ['pokestop-events/pokestop-event-add-dialog.component', 'pokestop-events/pokestop-event-list.component'],
  ];

  it.each(PAIRS)('%s renders the refusal above its actions', dialog => {
    const html = read(`${dialog}.html`);

    expect(html).toContain('<app-add-refusal [refusal]="refusal()" (editExisting)="editExisting($event)"');
    expect(html.indexOf('<app-add-refusal')).toBeLessThan(html.indexOf('<mat-dialog-actions'));
  });

  it.each(PAIRS)('%s is opened by a list that handles "Edit that one"', (_dialog, list) => {
    const ts = read(`${list}.ts`);

    expect(ts).toContain('isEditExisting(');
    expect(ts).toContain('private openExisting(');
  });

  it('the quick-pick apply dialog shows its refusal inline too', () => {
    expect(read('quick-picks/quick-pick-apply-dialog.component.html')).toContain('<app-add-refusal [refusal]="refusal()"');
  });
});
