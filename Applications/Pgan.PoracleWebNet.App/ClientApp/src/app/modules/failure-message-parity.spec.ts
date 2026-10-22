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
});
