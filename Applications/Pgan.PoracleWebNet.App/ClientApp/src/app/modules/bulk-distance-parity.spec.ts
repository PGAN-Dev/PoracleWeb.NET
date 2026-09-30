import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every list's bulk radius action reports what the server skipped.
 *
 * The distance endpoints leave alone the alarms a radius cannot apply to -- limited to areas, or
 * measured from a place when the radius is zero -- and name them in the response. A list that ignores
 * that says "updated distance for 5 alarms" when two of them did not change. Ten lists and no shared
 * base class: this is the shape where one gets missed, so it is pinned from the source.
 *
 * The lists also carried an `updateAllDistance()` with no control calling it since the header's
 * "Update All Distance" menu was replaced by select mode, whose Select All + Update Distance does the
 * same job. It was removed rather than kept in step with the bulk path; `PUT /distance` stays on the API.
 */
describe('bulk distance reports skipped alarms on every list', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  const LISTS = [
    'pokemon/pokemon-list.component.ts',
    'raids/raid-list.component.ts',
    'quests/quest-list.component.ts',
    'invasions/invasion-list.component.ts',
    'lures/lure-list.component.ts',
    'nests/nest-list.component.ts',
    'gyms/gym-list.component.ts',
    'max-battles/max-battle-list.component.ts',
    'fort-changes/fort-change-list.component.ts',
    'pokestop-events/pokestop-event-list.component.ts',
  ];

  it.each(LISTS)('%s reports skips from bulkUpdateDistance', file => {
    const source = read(file);

    const bulk = source.slice(
      source.indexOf('async bulkUpdateDistance('),
      source.indexOf('\n  }\n', source.indexOf('async bulkUpdateDistance(')),
    );

    expect(bulk).toContain('distanceUpdateMessage(');
  });

  it.each(LISTS)('%s has no updateAllDistance without a control to call it', file => {
    expect(read(file)).not.toContain('updateAllDistance(');
  });
});
