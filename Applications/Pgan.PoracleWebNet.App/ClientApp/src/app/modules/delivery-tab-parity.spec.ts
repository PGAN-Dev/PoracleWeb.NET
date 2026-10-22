import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The Delivery tab is built the same way in every alarm dialog: the scope choice (with its map), the
 * "Message" heading, then what it labels: the template, and the toggles side by side in one
 * `.message-row`, each with a line saying what it does. The Pokéstop event dialogs had the clean toggle
 * above the heading, leaving the heading orphaned at the bottom, and no description line (alarms D6);
 * quests and invasions called the same block "Common settings". Read from the templates for the reason
 * alarm-dialog-parity gives: Material mounts only the active tab.
 */
describe('alarm dialog Delivery tabs', () => {
  const DIALOGS = [
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

  const templates = DIALOGS.flatMap(d =>
    (['add', 'edit'] as const).map(kind => {
      const file = `${d}-${kind}-dialog.component.html`;
      return [file, readFileSync(join(__dirname, file), 'utf8')] as const;
    }),
  );

  it.each(templates)('%s puts the clean toggle under the Message settings heading', (_file, html) => {
    const toggle = html.indexOf("'ALARM.CLEAN_MODE'");
    if (toggle < 0) return; // fort changes have no clean bit
    const heading = html.indexOf("'ALARM.MESSAGE_SETTINGS'");

    expect(heading).toBeGreaterThanOrEqual(0);
    expect(heading).toBeLessThan(toggle);
  });

  it.each(templates)('%s says what clean mode does', (_file, html) => {
    const toggle = html.indexOf("'ALARM.CLEAN_MODE'");
    if (toggle < 0) return;

    // The hint must be this toggle's: before the next toggle, not any later hint in the file.
    const next = html.indexOf('<mat-slide-toggle', toggle);
    const hint = html.indexOf('class="clean-hint"', toggle);
    expect(hint).toBeGreaterThan(toggle);
    if (next >= 0) expect(hint).toBeLessThan(next);
  });

  it.each(templates)('%s keeps every Delivery toggle inside the one message row', (_file, html) => {
    const delivery = html.slice(html.indexOf("'ALARM.TAB_DELIVERY'"));
    const row = delivery.indexOf('class="message-row"');
    const toggles = [...delivery.matchAll(/<mat-slide-toggle/g)].map(m => m.index);
    if (toggles.length === 0) return; // fort changes: template only

    expect(row).toBeGreaterThanOrEqual(0);
    for (const at of toggles) expect(at).toBeGreaterThan(row);
  });

  it.each(templates)('%s names its Delivery tab with the one shared key', (_file, html) => {
    expect(html).toContain("'ALARM.TAB_DELIVERY'");
    expect(html).not.toMatch(/'(POKEMON|RAIDS|QUICK_PICKS)\.TAB_DELIVERY'/);
  });

  /** Add and edit name their first tab after the type, the way the navigation does, rather than "Settings". */
  it.each(DIALOGS.filter(d => d !== 'pokemon/pokemon'))('%s add and edit dialogs share their first tab name', dialog => {
    const firstTab = (kind: string) =>
      /class="tab-label">\{\{ '([A-Z_.]+)'/.exec(readFileSync(join(__dirname, `${dialog}-${kind}-dialog.component.html`), 'utf8'))?.[1];

    expect(firstTab('add')).toMatch(/^NAV\./);
    expect(firstTab('edit')).toBe(firstTab('add'));
  });
});
