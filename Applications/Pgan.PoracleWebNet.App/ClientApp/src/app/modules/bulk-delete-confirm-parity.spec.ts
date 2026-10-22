import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The bulk bar's button says "Delete 3". The confirm that follows used to say it five ways ("Delete All" on
 * Pokémon, "Delete Selected Alarms", "Delete Selected", a bare "Delete" under the page title). One wording
 * on all ten: the title names the count, the button repeats it.
 */
describe('bulk delete confirm', () => {
  const LISTS = [
    'pokemon/pokemon',
    'raids/raid',
    'quests/quest',
    'invasions/invasion',
    'lures/lure',
    'nests/nest',
    'gyms/gym',
    'fort-changes/fort-change',
    'pokestop-events/pokestop-event',
    'max-battles/max-battle',
  ];

  const confirmOf = (list: string): string => {
    const source = readFileSync(join(__dirname, `${list}-list.component.ts`), 'utf8');
    const start = source.indexOf('bulkDelete(): Promise<void>');
    const open = source.indexOf('this.dialog.open(ConfirmDialogComponent', start);
    return source.slice(open, source.indexOf('as ConfirmDialogData', open));
  };

  it.each(LISTS)('%s names the count in the title and on the button', list => {
    const confirm = confirmOf(list);

    expect(confirm).toContain("title: this.i18n.plural('ALARM.CONFIRM_BULK_DELETE_TITLE', this.actionIds().length)");
    expect(confirm).toContain("confirmText: this.i18n.instant('ALARM.DELETE_SELECTED', { count: this.actionIds().length })");
    expect(confirm).toContain("message: this.i18n.instant('ALARM.CONFIRM_BULK_DELETE_BODY')");
  });
});
