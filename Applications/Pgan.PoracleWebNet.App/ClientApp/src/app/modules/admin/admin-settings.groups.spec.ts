import { PROJECTED_KEYS, SETTING_GROUPS } from './admin-settings.component';

/**
 * The admin settings page renders one expansion panel per group. A group that declares no settings
 * renders as a header and a chevron over nothing, which is what three of them did: Maps & Assets was
 * left behind when #452 deleted the two toggles it held, and Commands and Debug never had any.
 *
 * This asserts the data rather than the rendering because the recurrence is always a data edit --
 * someone removes the last setting from a group and does not notice the shell.
 */
describe('SETTING_GROUPS', () => {
  it('declares no group without settings', () => {
    const empty = SETTING_GROUPS.filter(g => g.settings.length === 0).map(g => g.labelKey);

    expect(empty).toEqual([]);
  });

  it('gives every group a distinct label key', () => {
    const keys = SETTING_GROUPS.map(g => g.labelKey);

    expect(new Set(keys).size).toBe(keys.length);
  });

  it('gives every setting a distinct key across all groups', () => {
    // A duplicated key would bind two rows to one value, so the second silently shadows the first.
    const keys = SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key));

    expect(new Set(keys).size).toBe(keys.length);
  });
});

/**
 * A projection is not a setting. `poracle_locale` is synthesized by the API from Poracle's config so the
 * SPA can default the display language; undeclared, it fell through to the "Other" catch-all and rendered
 * as an editable text box. Because a real row wins over the synthesized value, one save would have pinned
 * the language default for good. Same mistake as #560, for a key that was never in a group. See #780.
 */
describe('PROJECTED_KEYS', () => {
  it('covers poracle_locale, so it cannot reach the "Other" catch-all', () => {
    expect(PROJECTED_KEYS).toContain('poracle_locale');
  });

  it('declares nothing that is also a real, editable setting', () => {
    const editable = new Set(SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key)));
    const overlap = PROJECTED_KEYS.filter(k => editable.has(k));

    expect(overlap).toEqual([]);
  });
});
