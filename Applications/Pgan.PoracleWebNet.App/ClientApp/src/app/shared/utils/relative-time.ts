/**
 * "3 minutes ago", "2 hours ago", "yesterday", in the display language. Intl carries the wording and the
 * plural forms for every locale, so callers only translate the sentence around it.
 */
export function relativeTime(then: number, now: number, lang: string): string {
  const format = formatter(lang);
  const seconds = Math.max(0, Math.round((now - then) / 1000));
  if (seconds < 60) return format.format(0, 'second');
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return format.format(-minutes, 'minute');
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return format.format(-hours, 'hour');
  return format.format(-Math.floor(hours / 24), 'day');
}

function formatter(lang: string): Intl.RelativeTimeFormat {
  // The app's `pt` is European Portuguese; Intl reads a bare `pt` as Brazilian (see core/i18n/plural.ts).
  const locale = lang === 'pt' ? 'pt-PT' : lang || 'en';
  try {
    return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  } catch {
    return new Intl.RelativeTimeFormat('en', { numeric: 'auto' });
  }
}
