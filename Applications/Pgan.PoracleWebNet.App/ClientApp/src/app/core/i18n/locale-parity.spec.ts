import * as fs from 'fs';
import * as path from 'path';

/**
 * Every locale had drifted to exactly 12 keys behind English, which the English fallback hid — the
 * affected strings simply rendered in English inside otherwise-translated pages, so nothing looked
 * broken. This fails the build the next time that happens. See #425.
 */
describe('locale parity', () => {
  const dir = path.join(__dirname, '../../../assets/i18n');

  const flatten = (value: unknown, prefix = ''): Record<string, string> => {
    const out: Record<string, string> = {};
    for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
      const dotted = prefix ? `${prefix}.${key}` : key;
      if (child !== null && typeof child === 'object') {
        Object.assign(out, flatten(child, dotted));
      } else {
        out[dotted] = String(child);
      }
    }
    return out;
  };

  const load = (locale: string) => flatten(JSON.parse(fs.readFileSync(path.join(dir, `${locale}.json`), 'utf8')));

  const english = load('en');
  const locales = fs
    .readdirSync(dir)
    .filter(f => f.endsWith('.json'))
    .map(f => f.replace('.json', ''))
    .filter(l => l !== 'en');

  it('ships more than one locale', () => {
    expect(locales.length).toBeGreaterThan(0);
  });

  /**
   * Counted messages are split by CLDR plural category (see core/i18n/plural.ts). English needs only
   * `_ONE` and `_OTHER`; Polish also needs `_FEW` and `_MANY`. Those extra categories are the one way a
   * locale may differ from English, and only for a base English already counts.
   */
  const pluralBases = new Set(
    Object.keys(english)
      .filter(k => k.endsWith('_OTHER') && `${k.slice(0, -'_OTHER'.length)}_ONE` in english)
      .map(k => k.slice(0, -'_OTHER'.length)),
  );
  const pluralLocale = (locale: string) => (locale === 'pt' ? 'pt-PT' : locale);
  /** The categories a language actually uses for whole numbers, so French's million-only "many" is not required. */
  const categoriesFor = (locale: string) => {
    const rules = new Intl.PluralRules(pluralLocale(locale));
    return new Set(Array.from({ length: 201 }, (_, n) => rules.select(n).toUpperCase()));
  };
  const isPluralExtra = (key: string, locale: string) => {
    const match = /^(.*)_(ZERO|TWO|FEW|MANY)$/.exec(key);
    return match !== null && pluralBases.has(match[1]) && categoriesFor(locale).has(match[2]);
  };

  it.each(locales)('%s has exactly the same keys as en', locale => {
    const keys = load(locale);
    expect(Object.keys(keys).filter(k => !(k in english) && !isPluralExtra(k, locale))).toEqual([]);
    expect(Object.keys(english).filter(k => !(k in keys))).toEqual([]);
  });

  it.each(locales)('%s has every plural form its language uses', locale => {
    const keys = load(locale);
    const missing = [...pluralBases].flatMap(base =>
      [...categoriesFor(locale)].map(category => `${base}_${category}`).filter(k => !(k in keys)),
    );
    expect(missing).toEqual([]);
  });

  it('pl carries few and many forms, so the rule above is not vacuous', () => {
    expect(categoriesFor('pl')).toEqual(new Set(['ONE', 'FEW', 'MANY']));
    expect(load('pl')['ALARM.ALARMS_COUNT_FEW']).toBeDefined();
  });

  it.each(locales)('%s translates the error toasts the interceptor uses', locale => {
    // These are the strings a user sees when something fails, so English here is the most visible
    // kind of gap. HTTP_ERROR.* is the single namespace both the interceptor and ToastService use.
    const keys = load(locale);
    const untranslated = Object.keys(english)
      .filter(k => k.startsWith('HTTP_ERROR.') || k === 'ERROR.FEATURE_DISABLED')
      .filter(k => keys[k] === english[k]);

    expect(untranslated).toEqual([]);
  });

  /**
   * A key can be present, translated, and still broken: drop `{{provider}}` from a sentence and it
   * renders with a hole in it, or — worse — reads as a confident statement of whatever the English
   * used to hard-code. That is not hypothetical. The basemap warnings were rewritten from naming
   * OpenStreetMap to taking `{{provider}}`, by regex, across all eleven files at once; a locale whose
   * phrasing did not match would have been left asserting a provider the code no longer chooses.
   */
  it.each(locales)('%s preserves every interpolation placeholder English uses', locale => {
    const placeholders = (value: string) => [...value.matchAll(/\{\{\s*(\w+)\s*\}\}/g)].map(m => m[1]).sort();
    const keys = load(locale);

    // A plural extra (pl `_FEW`) is held to its English `_OTHER`.
    const englishFor = (k: string) => english[k] ?? english[k.replace(/_(ZERO|TWO|FEW|MANY)$/, '_OTHER')];
    const mismatched = Object.keys(keys)
      .filter(k => englishFor(k) !== undefined)
      .map(k => ({ key: k, mine: placeholders(keys[k]), theirs: placeholders(englishFor(k)) }))
      .filter(({ mine, theirs }) => mine.join() !== theirs.join());

    expect(mismatched).toEqual([]);
  });

  it.each(locales)('%s translates the paginator', locale => {
    const keys = load(locale);
    // RANGE strings are mostly interpolation tokens, so only the prose label is checked.
    expect(keys['PAGINATOR.ITEMS_PER_PAGE']).not.toBe(english['PAGINATOR.ITEMS_PER_PAGE']);
  });
});
