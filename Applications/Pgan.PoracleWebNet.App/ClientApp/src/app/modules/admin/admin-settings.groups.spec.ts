import * as fs from 'fs';
import * as path from 'path';

import { PROJECTED_KEYS, SETTING_GROUPS } from './admin-settings.component';
import { ICON_SOURCE_KEYS } from '../../core/services/icon.service';

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

  it("covers poracle_alert_languages, which is Poracle's list and not this page's to edit", () => {
    expect(PROJECTED_KEYS).toContain('poracle_alert_languages');
  });

  it('declares nothing that is also a real, editable setting', () => {
    const editable = new Set(SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key)));
    const overlap = PROJECTED_KEYS.filter(k => editable.has(k));

    expect(overlap).toEqual([]);
  });
});

/**
 * A group or a setting whose label key is absent from en.json renders the raw key -- "ADMIN_SETTINGS.
 * GROUP_MAPS" sitting where a heading should be. Nothing else catches it: locale parity compares the
 * locales against each other, so a key missing from all twelve files is consistent and passes.
 */
describe('SETTING_GROUPS translation keys', () => {
  const english = JSON.parse(fs.readFileSync(path.join(__dirname, '../../../assets/i18n/en.json'), 'utf8')) as {
    ADMIN_SETTINGS: Record<string, string>;
  };

  const resolves = (key: string) => key.startsWith('ADMIN_SETTINGS.') && key.slice('ADMIN_SETTINGS.'.length) in english.ADMIN_SETTINGS;

  it('resolves every group label', () => {
    expect(SETTING_GROUPS.map(g => g.labelKey).filter(k => !resolves(k))).toEqual([]);
  });

  it('resolves every setting label and description', () => {
    const keys = SETTING_GROUPS.flatMap(g => g.settings.flatMap(s => [s.labelKey, s.descriptionKey]));
    expect(keys.filter(k => !resolves(k))).toEqual([]);
  });

  it('resolves every translated dropdown option', () => {
    // Options are a mix: provider names are brand names and ship as literal labels, while words like
    // "Automatic" carry a key. An unresolved key here puts the raw string inside the dropdown.
    const keys = SETTING_GROUPS.flatMap(g => g.settings.flatMap(s => (s.options ?? []).map(o => o.labelKey))).filter(
      (k): k is string => !!k,
    );

    expect(keys).not.toEqual([]);
    expect(keys.filter(k => !resolves(k))).toEqual([]);
  });

  it('gives every dropdown option exactly one of a label and a label key', () => {
    const malformed = SETTING_GROUPS.flatMap(g =>
      g.settings.flatMap(s => (s.options ?? []).filter(o => !!o.label === !!o.labelKey).map(o => `${s.key}:${o.value}`)),
    );

    expect(malformed).toEqual([]);
  });
});

/**
 * Which Maps fields apply depends entirely on the provider, and until they were hidden the page
 * showed all five at once with nothing to say that four of them were inert for the choice made. The
 * conditions are asserted here rather than through the rendered page because that is where they live
 * -- and because the question a reader has ("I picked CARTO, what do I fill in?") is answered by
 * exactly this table.
 */
describe('Maps field visibility', () => {
  const maps = SETTING_GROUPS.find(g => g.labelKey === 'ADMIN_SETTINGS.GROUP_MAPS')!;

  /** The Maps keys on offer for a given stored configuration. */
  const shown = (values: Record<string, string>) =>
    maps.settings.filter(meta => !meta.showIf || meta.showIf(key => values[key] ?? '')).map(meta => meta.key);

  it('asks a keyless built-in for nothing but the choice', () => {
    expect(shown({ basemap_provider: 'osm' })).toEqual(['basemap_provider']);
  });

  it('asks a keyed built-in for the key and nothing else', () => {
    expect(shown({ basemap_provider: 'carto-positron' })).toEqual(['basemap_provider', 'basemap_key']);
  });

  it('asks a custom basemap for its name, URLs and attribution, but not a key it does not use', () => {
    const values = { basemap_provider: 'custom', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' };
    expect(shown(values)).toEqual(['basemap_provider', 'basemap_name', 'basemap_url', 'basemap_url_dark', 'basemap_attribution']);
  });

  it('asks a custom basemap for a key once its URL says it wants one', () => {
    const values = { basemap_provider: 'custom', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png?k={key}' };
    expect(shown(values)).toContain('basemap_key');
  });

  it('asks an unconfigured install for nothing at all', () => {
    // Nothing chosen resolves to OpenStreetMap, which wants no key and no URL. Resolving it to CARTO
    // instead put a key field, and a warning about a missing key, on every fresh install.
    expect(shown({})).toEqual(['basemap_provider']);
  });

  it('asks for the key on an install that set one and never picked a provider', () => {
    // That combination is all the pre-#863 settings could express, and it still means CARTO.
    expect(shown({ basemap_key: 'abc123' })).toEqual(['basemap_provider', 'basemap_key']);
  });

  it('treats an install that set only a tile URL as custom, which is what it meant', () => {
    expect(shown({ basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' })).toContain('basemap_url');
  });
});

/**
 * The icon repository picker writes a set of settings; IconService reads a set of settings. Nothing
 * connected the two, and they drifted: the picker wrote four keys and left `uicons_type` alone, so an
 * instance could look configured while its type icons still pointed at the hardcoded default -- which
 * by then was a repository that had been deleted. The Pokemon filter chips rendered nothing and no test
 * had an opinion about it. See #877.
 */
describe('icon source keys', () => {
  /** Mirrors the map inside AdminSettingsComponent.selectRepo. */
  const written = (base: string): Record<string, string> => ({
    uicons_raid: `${base}/raid`,
    uicons_gym: `${base}/gym`,
    uicons_pkmn: `${base}/pokemon`,
    uicons_reward: `${base}/reward`,
    uicons_type: `${base}/type`,
  });

  it('picking a repository points every category IconService reads at it', () => {
    expect(Object.keys(written('https://example.test/UICONS')).sort()).toEqual([...ICON_SOURCE_KEYS].sort());
  });

  it('no icon source key is also declared as an editable group row', () => {
    // They are driven by the repository picker, not by a text box. One appearing in both places would
    // let an operator set a base by hand and have the picker silently overwrite it.
    const groupKeys = new Set(SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key)));

    expect(ICON_SOURCE_KEYS.filter(k => groupKeys.has(k))).toEqual([]);
  });
});
