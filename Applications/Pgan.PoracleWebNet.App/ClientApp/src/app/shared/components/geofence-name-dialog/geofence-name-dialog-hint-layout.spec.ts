import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The name rule under the field wrapped with "( ) &" alone on its last line, and the "8 / 50" counter
 * beside it was squeezed into a one-word column. The rule's character list never breaks, the counter
 * keeps its width, and the field's subscript grows with the hint instead of clipping it.
 */
describe('geofence name dialog hint layout', () => {
  const read = (file: string): string => readFileSync(join(__dirname, file), 'utf8');

  it('lets the subscript grow and keeps the counter on one line', () => {
    expect(read('geofence-name-dialog.component.html')).toContain('subscriptSizing="dynamic"');
    expect(read('geofence-name-dialog.component.scss')).toMatch(/\.mat-mdc-form-field-hint-end\s*\{[^}]*white-space:\s*nowrap/);
  });

  // The allowed punctuation joined by no-break spaces (U+00A0), so it can't wrap mid-list.
  const ALLOWED = ['-', "'", '.', '(', ')', '&'].join(String.fromCharCode(0xa0));

  const i18nDir = join(__dirname, '../../../../assets/i18n');
  const locales = readdirSync(i18nDir).filter(f => f.endsWith('.json'));

  it.each(locales)('%s keeps the allowed-character list on one line', file => {
    const geofences = JSON.parse(readFileSync(join(i18nDir, file), 'utf8')).GEOFENCES;
    for (const key of ['NAME_RULE_HINT', 'NAME_RULE_ERROR']) {
      const value: string = geofences[key];
      expect(value).toContain(ALLOWED);
    }
  });
});
