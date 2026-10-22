import { GEOFENCE_NAME_PATTERN } from './geofence.utils';

/**
 * One rule for a geofence name, shared by create, rename, import and the admin's promoted name, and the same
 * as `UserGeofenceService.MyRegex` on the server. Four copies of it used to sit in four files.
 */
describe('GEOFENCE_NAME_PATTERN', () => {
  it.each(['Bon Air - Robious', "St. John's (North) & Co", 'Zone 51', 'ACLT'])('accepts %s', name => {
    expect(GEOFENCE_NAME_PATTERN.test(name)).toBe(true);
  });

  it.each(['Café', 'Müllerpark', 'Söder', '<script>', 'a/b', ''])('refuses %s', name => {
    expect(GEOFENCE_NAME_PATTERN.test(name)).toBe(false);
  });
});
