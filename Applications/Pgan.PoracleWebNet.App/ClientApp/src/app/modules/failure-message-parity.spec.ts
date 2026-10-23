import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A failed alarm write says why when the API said why, and otherwise one specific "Couldn't ... Try
 * again" line shared by all ten types. "Failed to delete alarm" in ten spellings told nobody anything,
 * and reading `err.error.error` by hand showed a 503's English body to a German reader.
 */
describe('alarm failure messages', () => {
  const TYPES = [
    'pokemon/pokemon',
    'raids/raid',
    'quests/quest',
    'invasions/invasion',
    'lures/lure',
    'nests/nest',
    'gyms/gym',
    'fort-changes/fort-change',
    'pokestop-events/pokestop-event',
    'max-battles/max-battle',
  ];

  const files = TYPES.flatMap(t =>
    ['list.component.ts', 'add-dialog.component.ts', 'edit-dialog.component.ts'].map(suffix => {
      const file = `${t}-${suffix}`;
      return [file, readFileSync(join(__dirname, file), 'utf8')] as const;
    }),
  );

  it.each(files)('%s uses the shared failure keys, not one per type', (_file, source) => {
    expect(source).not.toMatch(/'[A-Z_]+\.(SNACK_FAILED_[A-Z_]+|CREATE_FAILED|UPDATE_FAILED)'/);
  });

  it.each(files)('%s reads the reason through serverReason, never err.error.error by hand', (_file, source) => {
    expect(source).not.toMatch(/\?\.error\?\.error/);
  });

  it.each(files)("%s puts the server's reason ahead of every failure snack", (_file, source) => {
    const uses = [
      ...source.matchAll(/this\.i18n\.instant\('(ALARM\.FAILED_(?:UPDATE|DELETE|DELETE_ALL|DISTANCE)|WHERE\.SCOPE_SAVE_ERROR)'\)/g),
    ];
    for (const use of uses) {
      expect(source.slice(Math.max(0, use.index - 21), use.index)).toBe('serverReason(err) ?? ');
    }
  });

  /**
   * An edit model binding refuses (a field out of its range) answers `{ errors: { Field: [...] } }` with no
   * `error`. The add dialogs name that field through `createFailureText`; the edit dialogs said "Try again
   * in a moment", which can't help.
   */
  it.each(files.filter(([file]) => file.endsWith('edit-dialog.component.ts')))(
    '%s names the refused field the way the add dialog does',
    (_file, source) => {
      expect(source).toContain("createFailureText(err, this.i18n, 'ALARM.FAILED_UPDATE')");
      expect(source).not.toContain("serverReason(err) ?? this.i18n.instant('ALARM.FAILED_UPDATE')");
    },
  );
});

/**
 * The rest of the app's write failures: the server's reason first when it gave one (a 4xx), and otherwise
 * a line that says what did not happen and what to do next. "Failed to update location" said neither.
 */
describe('other failure messages', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  it.each([
    ['dashboard/dashboard.component.ts', 'DASHBOARD.SWITCH_PROFILE_FAILED'],
    ['areas/area-list.component.ts', 'AREAS.SNACK_LOCATION_FAILED'],
    ['../shared/components/location-dialog/location-dialog.component.ts', 'DIALOG.LOCATION_SAVE_ERROR'],
    ['admin/my-webhooks.component.ts', 'ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'],
    ['admin/admin-destinations.component.ts', 'ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'],
    ['admin/admin-destinations.component.ts', 'ADMIN.SNACK_FAILED_ADD_DESTINATION'],
    ['admin/geofence-submissions/geofence-submissions.component.ts', 'ADMIN.SNACK_FAILED_DELETE_GEOFENCE'],
    ['admin/geofence-submissions/geofence-submissions.component.ts', 'ADMIN.SNACK_FAILED_APPROVE'],
    ['admin/geofence-submissions/geofence-submissions.component.ts', 'ADMIN.SNACK_FAILED_REJECT'],
  ])("%s puts the server's reason ahead of %s", (file, key) => {
    const source = read(file);

    // A call site may opt one 5xx in (`serverReason(err, { allow5xx: [502] })`) where that answer carries a next step.
    const call = String.raw`serverReason\(err(, [^)]+)?\) \?\? this\.i18n\.instant\('` + key.replace('.', String.raw`\.`) + String.raw`'\)`;
    expect(source).toMatch(new RegExp(call));
    expect(source).not.toMatch(/\?\.error\?\.error/);
  });

  it('has no English failure line that starts "Failed to"', () => {
    const en = JSON.parse(read('../../assets/i18n/en.json')) as Record<string, unknown>;
    const failed: string[] = [];
    const walk = (node: unknown, path: string) => {
      if (typeof node === 'string') {
        if (/^Failed to\b/.test(node)) failed.push(path);
      } else if (node && typeof node === 'object') {
        for (const [key, value] of Object.entries(node)) walk(value, path ? `${path}.${key}` : key);
      }
    };
    walk(en, '');

    expect(failed).toEqual([]);
  });
});
