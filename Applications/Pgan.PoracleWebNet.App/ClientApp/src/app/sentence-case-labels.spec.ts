import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Sentence case in English (UI audit N9 and the screenshot notes): the labels below were the Title Case
 * left behind. Proper nouns and game names keep their capitals (Pokémon, Discord, Telegram, Great League).
 */
describe('English labels are sentence case', () => {
  const en = JSON.parse(readFileSync(join(__dirname, '../assets/i18n/en.json'), 'utf8'));
  const get = (key: string): string => key.split('.').reduce((o, k) => o?.[k], en);

  it.each([
    ['AREAS.SELECT_ALL_VISIBLE', 'Select all'],
    ['AREAS.DESELECT_ALL_VISIBLE', 'Deselect all'],
    ['GEOJSON_IMPORT.TITLE_UPLOAD', 'Import geofences'],
    ['GEOJSON_IMPORT.TITLE_PREVIEW', 'Select & name geofences'],
    ['GEOJSON_IMPORT.IMPORT_BTN_ONE', 'Import {{count}} geofence'],
    ['GEOJSON_IMPORT.TITLE_RESULTS', 'Import results'],
    ['GEOJSON_EXPORT.TITLE', 'Export geofences'],
    ['DIALOG.LOCATION_TITLE', 'Set location'],
    ['DIALOG.LOCATION_USE_MY', 'Use my location'],
    ['LURES.LURE_TYPES', 'Lure types'],
    ['RAIDS.BY_LEVEL', 'By level'],
    ['RAIDS.BY_BOSS', 'By boss'],
    ['RAIDS.TEAM_ANY', 'Any team'],
    ['ADMIN.ADD_DESTINATION', 'Add destination'],
    ['ADMIN.GEOFENCES_TITLE', 'User geofences'],
    ['ADMIN.APPROVAL_DIALOG_TITLE', 'Review geofence'],
    ['ADMIN_SETTINGS.DISABLE_FORT_CHANGES_LABEL', 'Fort changes'],
    ['GYMS.TRACK_SLOT_CHANGES', 'Track slot changes'],
    ['NESTS.MIN_SPAWNS_HR', 'Min spawns/hour'],
  ])('%s', (key, expected) => {
    expect(get(key)).toBe(expected);
  });

  it('names the Areas page the way the sidebar does', () => {
    expect(JSON.stringify(en)).not.toContain('Areas & Places');
  });
});
