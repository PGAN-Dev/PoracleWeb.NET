import * as fs from 'fs';
import * as path from 'path';

import { pluralCategory, pluralKey, PluralTranslator, translatePlural } from './plural';

/** A translator over one real locale file, interpolating `{{name}}` the way ngx-translate does. */
function translatorFor(locale: string): PluralTranslator {
  const file = path.join(__dirname, '../../../assets/i18n', `${locale}.json`);
  const flat: Record<string, string> = {};
  const walk = (node: Record<string, unknown>, prefix: string) => {
    for (const [key, value] of Object.entries(node)) {
      const dotted = prefix ? `${prefix}.${key}` : key;
      if (value !== null && typeof value === 'object') walk(value as Record<string, unknown>, dotted);
      else flat[dotted] = String(value);
    }
  };
  walk(JSON.parse(fs.readFileSync(file, 'utf8')), '');
  return {
    instant: (key, params) => (flat[key] ?? key).replace(/\{\{\s*(\w+)\s*\}\}/g, (_, name: string) => String(params?.[name] ?? '')),
  };
}

describe('plural helper', () => {
  describe('pluralCategory', () => {
    it('gives English one and other', () => {
      expect([0, 1, 2, 5].map(n => pluralCategory(n, 'en'))).toEqual(['other', 'one', 'other', 'other']);
    });

    it('gives Polish one, few and many', () => {
      expect([1, 2, 4, 5, 12, 22, 25].map(n => pluralCategory(n, 'pl'))).toEqual(['one', 'few', 'few', 'many', 'many', 'few', 'many']);
    });

    it('treats zero as singular in French', () => {
      expect(pluralCategory(0, 'fr')).toBe('one');
      expect(pluralCategory(2, 'fr')).toBe('other');
    });

    it('reads the app’s bare pt as European Portuguese, where zero is plural', () => {
      expect(pluralCategory(0, 'pt')).toBe('other');
      expect(pluralCategory(0, 'pt-BR')).toBe('one');
    });

    it('falls back to English rules for a tag Intl rejects', () => {
      expect(pluralCategory(1, 'not a tag!')).toBe('one');
    });
  });

  describe('pluralKey', () => {
    it('falls back to _OTHER when the category has no key', () => {
      expect(pluralKey('X', 3, 'pl', () => false)).toBe('X_OTHER');
      expect(pluralKey('X', 3, 'pl', k => k === 'X_FEW')).toBe('X_FEW');
    });
  });

  describe('translatePlural against the shipped locale files', () => {
    const count = (locale: string, n: number) => translatePlural(translatorFor(locale), locale, 'ALARM.ALARMS_COUNT', n);

    it('en', () => {
      expect(count('en', 1)).toBe('1 alarm');
      expect(count('en', 2)).toBe('2 alarms');
      expect(count('en', 0)).toBe('0 alarms');
    });

    it('de', () => {
      expect(count('de', 1)).toBe('1 Alarm');
      expect(count('de', 3)).toBe('3 Alarme');
    });

    it('pl', () => {
      expect(count('pl', 1)).toBe('1 alarm');
      expect(count('pl', 3)).toBe('3 alarmy');
      expect(count('pl', 5)).toBe('5 alarmów');
      expect(count('pl', 22)).toBe('22 alarmy');
    });

    it('fr', () => {
      expect(count('fr', 0)).toBe('0 alarme');
      expect(count('fr', 1)).toBe('1 alarme');
      expect(count('fr', 2)).toBe('2 alarmes');
      // French "many" (a million) has no key of its own and reads the _OTHER form.
      expect(count('fr', 1_000_000)).toBe('1000000 alarmes');
    });

    it('passes extra params and lets them rename the count', () => {
      const t = translatorFor('en');
      expect(translatePlural(t, 'en', 'POKEMON.RESULT_COUNT', 1, { filtered: 1, total: 1 })).toBe('1 of 1 alarm');
      expect(translatePlural(t, 'en', 'GEOJSON_IMPORT.SLOTS_WARNING', 2, { max: 10, remaining: 2 })).toContain('2 slots remaining');
    });
  });
});
