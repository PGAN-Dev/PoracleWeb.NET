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

  it.each(locales)('%s has exactly the same keys as en', locale => {
    const keys = load(locale);
    expect(Object.keys(keys).filter(k => !(k in english))).toEqual([]);
    expect(Object.keys(english).filter(k => !(k in keys))).toEqual([]);
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

  it.each(locales)('%s translates the paginator', locale => {
    const keys = load(locale);
    // RANGE strings are mostly interpolation tokens, so only the prose label is checked.
    expect(keys['PAGINATOR.ITEMS_PER_PAGE']).not.toBe(english['PAGINATOR.ITEMS_PER_PAGE']);
  });
});
