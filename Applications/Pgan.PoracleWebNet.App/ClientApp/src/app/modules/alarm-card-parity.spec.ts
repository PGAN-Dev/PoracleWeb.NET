import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The ten alarm types share one card and one toolbar. Each list used to invent its own card, and the
 * other nine had no search: this pins all of them to the shared parts, so an eleventh type, or an edit
 * to one of the ten, cannot quietly drift back. Read from the templates, like quiet-surface-parity.
 */
describe('every alarm list uses the shared rule card and toolbar', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  /** [list template, how many card grids it draws, the edge each grid's card carries (null = none)]. */
  const LISTS: [string, (null | string)[]][] = [
    ['pokemon/pokemon-list.component.html', ['edgeFor(monster)']],
    ['raids/raid-list.component.html', ['teamEdge(raid.team)', 'tierEdge(egg.level)']],
    ['quests/quest-list.component.html', [null]],
    ['invasions/invasion-list.component.html', [null]],
    ['pokestop-events/pokestop-event-list.component.html', [null]],
    ['lures/lure-list.component.html', [null]],
    ['nests/nest-list.component.html', [null]],
    ['gyms/gym-list.component.html', ['teamEdge(gym.team)']],
    ['max-battles/max-battle-list.component.html', [null]],
    ['fort-changes/fort-change-list.component.html', [null]],
  ];

  it.each(LISTS)('%s draws its rules with app-alarm-card and no hand-built card', (template, grids) => {
    const html = read(template);

    expect(html.split('<app-alarm-card').length - 1).toBe(grids.length);
    expect(html).not.toContain('<mat-card');
    expect(html).not.toContain('app-rule-summary');
    expect(html).not.toContain('stat-label');
  });

  it.each(LISTS)('%s has the shared toolbar with search and sort', template => {
    const html = read(template);

    expect(html).toContain('<app-alarm-list-toolbar');
    expect(html).toContain('[(search)]="search"');
    expect(html).toContain('[(sort)]="sortBy"');
    expect(html).toContain('(selectModeToggle)="toggleSelectMode()"');
  });

  /**
   * Colour is data or nothing: team on gym and raid cards, the tier on egg cards, the primary type on
   * a single-species Pokémon card, and no edge anywhere else.
   */
  it.each(LISTS)('%s colours a card only where the colour is data', (template, grids) => {
    const html = read(template);
    const edges = [...html.matchAll(/\[edge\]="([^"]+)"/g)].map(m => m[1]);

    expect(edges).toEqual(grids.filter((edge): edge is string => edge !== null));
    expect(html).not.toMatch(/border-(left|top)-color/);
  });

  it.each(LISTS)("%s passes PoracleNG's rule summary to the card, behind Details", template => {
    const html = read(template);
    const cards = html.split('<app-alarm-card').length - 1;

    // Pokéstop events carry no description, and a fort change's is a raw JSON array rather than a
    // sentence; every other type hands its own to the card.
    if (template.startsWith('pokestop-events') || template.startsWith('fort-changes')) {
      expect(html).not.toContain('[summary]');
    } else {
      expect(html.split('[summary]=').length - 1).toBe(cards);
    }
  });
});
