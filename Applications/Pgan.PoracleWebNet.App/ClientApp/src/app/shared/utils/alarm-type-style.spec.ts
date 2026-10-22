import * as fs from 'fs';
import * as path from 'path';

import { ALARM_TYPE_STYLE } from './alarm-type-style';

/**
 * The settings page borrows each alarm type's nav icon and colour. A copy can drift from the original, so
 * this reads the nav items out of app.ts and checks every entry against the item with the same disableKey.
 */
describe('ALARM_TYPE_STYLE', () => {
  const appSource = fs.readFileSync(path.join(__dirname, '../../app.ts'), 'utf8');

  function navItem(disableKey: string): { color: string; icon: string } | null {
    const at = appSource.indexOf(`disableKey: '${disableKey}'`);
    if (at < 0) return null;
    const open = appSource.lastIndexOf('{', at);
    const close = appSource.indexOf('}', at);
    const body = appSource.slice(open, close);
    const icon = /icon: '([^']+)'/.exec(body)?.[1];
    const color = /iconColor: '([^']+)'/.exec(body)?.[1];
    return icon && color ? { color, icon } : null;
  }

  it.each(Object.entries(ALARM_TYPE_STYLE))('%s matches its nav item', (key, style) => {
    expect(navItem(key)).toEqual(style);
  });

  it('covers every alarm-type switch on the settings page', () => {
    for (const key of ['disable_mons', 'disable_raids', 'disable_quests', 'disable_invasions', 'disable_lures']) {
      expect(ALARM_TYPE_STYLE[key]).toBeDefined();
    }
  });
});
