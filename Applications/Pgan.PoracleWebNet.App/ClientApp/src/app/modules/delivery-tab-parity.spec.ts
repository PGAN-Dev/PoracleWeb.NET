import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The Delivery tab is built the same way in every alarm dialog: the "Message settings" heading, then
 * what it labels (template, clean mode), each toggle with a line saying what it does. The Pokéstop event
 * dialogs had the clean toggle above the heading, leaving the heading orphaned at the bottom, and no
 * description line (alarms D6). Read from the templates for the reason alarm-dialog-parity gives:
 * Material mounts only the active tab.
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
    // Multi-select adds (quest, invasion) title the same block "Common settings": it applies to every alarm made.
    const heading = Math.max(html.indexOf("'ALARM.MESSAGE_SETTINGS'"), html.indexOf("'ALARM.COMMON_SETTINGS'"));

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
});
