import { relativeTime } from './relative-time';

describe('relativeTime', () => {
  const at = Date.parse('2026-10-22T12:00:00Z');
  const ago = (ms: number, lang = 'en') => relativeTime(at - ms, at, lang);

  it.each([
    [10_000, 'now'],
    [3 * 60_000, '3 minutes ago'],
    [60 * 60_000, '1 hour ago'],
    [5 * 60 * 60_000, '5 hours ago'],
    [26 * 60 * 60_000, 'yesterday'],
    [3 * 24 * 60 * 60_000, '3 days ago'],
  ])('reads %i ms back as "%s"', (ms, text) => {
    expect(ago(ms)).toBe(text);
  });

  it('never reads a clock that lags the server as the future', () => {
    expect(relativeTime(at + 5000, at, 'en')).toBe('now');
  });

  it('speaks the display language', () => {
    expect(ago(3 * 60_000, 'de')).toBe('vor 3 Minuten');
  });

  it('falls back to English for a tag Intl refuses', () => {
    expect(ago(3 * 60_000, 'not a tag!')).toBe('3 minutes ago');
  });
});
