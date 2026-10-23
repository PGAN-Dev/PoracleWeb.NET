/**
 * Plural forms for counted strings.
 *
 * A counted message is stored as one key per CLDR plural category, upper-cased onto a shared base:
 * `ALARM.ALARMS_COUNT_ONE` and `ALARM.ALARMS_COUNT_OTHER` in English, plus `_FEW` and `_MANY` in Polish,
 * which needs "1 alarm", "2 alarmy" and "5 alarmów". `Intl.PluralRules` decides the category for the
 * display language; a locale only carries the categories its language uses for whole numbers, and any
 * category it does not carry (French "many" for a million, say) falls back to `_OTHER`.
 * `locale-parity.spec.ts` enforces both halves.
 */

/** The smallest translator the helper needs: ngx-translate's `instant`, or I18nService's. */
export interface PluralTranslator {
  instant(key: string, params?: Record<string, unknown>): string;
}

const rules = new Map<string, Intl.PluralRules>();

/**
 * The app's `pt` is European Portuguese, but Intl reads a bare `pt` as Brazilian, whose "one" also
 * covers zero. Everything else is handed to Intl as it is.
 */
function pluralLocale(lang: string): string {
  return lang === 'pt' ? 'pt-PT' : lang || 'en';
}

/** The CLDR plural category of `count` in `lang`: 'one', 'few', 'many', 'other' and so on. */
export function pluralCategory(count: number, lang: string): Intl.LDMLPluralRule {
  const locale = pluralLocale(lang);
  let rule = rules.get(locale);
  if (!rule) {
    try {
      rule = new Intl.PluralRules(locale);
    } catch {
      // A malformed tag throws RangeError; English is the app's fallback language anyway.
      rule = new Intl.PluralRules('en');
    }
    rules.set(locale, rule);
  }
  return rule.select(count);
}

/** The key to read for `count`: `${base}_${CATEGORY}` when the translations have it, `${base}_OTHER` when not. */
export function pluralKey(base: string, count: number, lang: string, has: (key: string) => boolean): string {
  const key = `${base}_${pluralCategory(count, lang).toUpperCase()}`;
  return has(key) ? key : `${base}_OTHER`;
}

/**
 * Translates a counted message. `count` picks the form and is passed to the string as `{{count}}`;
 * `params` adds anything else the string interpolates (and may rename the count, as `{{step}}` does).
 */
export function translatePlural(
  translator: PluralTranslator,
  lang: string,
  base: string,
  count: number,
  params?: Record<string, unknown>,
): string {
  // ngx-translate answers a missing key with the key itself, after trying the fallback language.
  const key = pluralKey(base, count, lang, k => translator.instant(k) !== k);
  return translator.instant(key, { count, ...params });
}
