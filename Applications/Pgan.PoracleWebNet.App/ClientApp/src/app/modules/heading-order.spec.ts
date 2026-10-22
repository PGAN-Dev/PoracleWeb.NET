import * as fs from 'fs';
import * as path from 'path';

/**
 * Heading levels on the list pages went h1 (the page) straight to h3 (each card), which axe reports as
 * `heading-order` and which leaves a screen reader's heading list with a gap (UI audit cross D9). Cards
 * sit directly under the page heading, so they are h2.
 */
describe('list page heading order', () => {
  const PAGES = [
    'pokemon/pokemon-list',
    'raids/raid-list',
    'quests/quest-list',
    'invasions/invasion-list',
    'pokestop-events/pokestop-event-list',
    'lures/lure-list',
    'nests/nest-list',
    'gyms/gym-list',
    'fort-changes/fort-change-list',
    'max-battles/max-battle-list',
    'areas/area-list',
  ];

  it.each(PAGES)('%s goes from h1 to h2 without skipping', page => {
    const template = fs.readFileSync(path.join(__dirname, `${page}.component.html`), 'utf8');
    const levels = [...template.matchAll(/<h([1-6])[\s>]/g)].map(m => Number(m[1]));

    expect(levels[0]).toBe(1);
    levels.reduce((previous, level) => {
      expect(level - previous).toBeLessThanOrEqual(1);
      return level;
    });
  });
});
