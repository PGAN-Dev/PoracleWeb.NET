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

  /**
   * Sending a sample used to be a paper-plane button beside Edit, and members pressed it by accident and went
   * looking for a spawn that was never there (#1097). It now lives behind one shared "More actions" menu, placed
   * after Delete, in the eight types that can send a sample (the raid list holds two: raid and egg cards). The
   * three that cannot (Pokéstop events, max battles, fort changes) must not grow a menu with nothing in it.
   */
  const SAMPLE_MENUS: [string, string[]][] = [
    ['pokemon/pokemon-list.component.html', ['pokemon']],
    ['raids/raid-list.component.html', ['raid', 'egg']],
    ['quests/quest-list.component.html', ['quest']],
    ['invasions/invasion-list.component.html', ['invasion']],
    ['lures/lure-list.component.html', ['lure']],
    ['nests/nest-list.component.html', ['nest']],
    ['gyms/gym-list.component.html', ['gym']],
  ];
  const NO_SAMPLE = [
    'pokestop-events/pokestop-event-list.component.html',
    'max-battles/max-battle-list.component.html',
    'fort-changes/fort-change-list.component.html',
  ];

  it.each(SAMPLE_MENUS)('%s keeps Edit and Delete on the card and puts the sample behind the shared menu', (template, types) => {
    const html = read(template);
    const menus = [...html.matchAll(/<app-alarm-more-menu alarmType="(\w+)" \[uid\]="\w+\.uid" \/>/g)].map(m => m[1]);

    expect(menus).toEqual(types);
    // Each menu is the last control in its action row: Edit, Delete, then More actions.
    for (const block of html.split('<ng-container ruleActions>').slice(1)) {
      const row = block.slice(0, block.indexOf('</ng-container>'));
      expect(row.indexOf('COMMON.EDIT')).toBeGreaterThan(-1);
      expect(row.indexOf('COMMON.DELETE')).toBeGreaterThan(row.indexOf('COMMON.EDIT'));
      expect(row.indexOf('<app-alarm-more-menu')).toBeGreaterThan(row.indexOf('COMMON.DELETE'));
    }
    // No paper plane, no copy of the old button or of the service call behind it.
    expect(html).not.toContain('test-alert-btn');
    expect(html).not.toContain('sendTestAlert');
    expect(html).not.toContain('testAlertService');
    expect(html).not.toMatch(/<mat-icon[^>]*>send<\/mat-icon>/);
  });

  it.each(SAMPLE_MENUS)('%s no longer carries the sample plumbing in its class', template => {
    const ts = read(template.replace('.html', '.ts'));

    expect(ts).toContain('AlarmMoreMenuComponent');
    expect(ts).not.toContain('TestAlertService');
    expect(ts).not.toContain('sendTestAlert');
  });

  it.each(NO_SAMPLE)('%s has no sample menu, because there is no sample event to send', template => {
    const html = read(template);

    expect(html).not.toContain('app-alarm-more-menu');
    expect(html).not.toMatch(/<mat-icon[^>]*>send<\/mat-icon>/);
  });

  it.each(LISTS)('%s has the shared toolbar with search and sort', template => {
    const html = read(template);

    expect(html).toContain('<app-alarm-list-toolbar');
    expect(html).toContain('[(search)]="search"');
    expect(html).toContain('[(sort)]="sortBy"');
    expect(html).toContain('(selectModeToggle)="toggleSelectMode()"');
  });

  /** A linked person filters by app, sees the app on each card, and moves a selection from the bulk bar. */
  it.each(LISTS)('%s wires the delivery filter, the card glyph and the bulk move', (template, grids) => {
    const html = read(template);
    const ts = read(template.replace('.html', '.ts'));

    expect(html).toContain('[(destinationFilter)]="destinationFilter"');
    expect(html).toContain('[canFilterDestination]="canChooseDestination()"');
    expect(html.split('[destination]=').length - 1).toBe(grids.length);
    expect(html).toContain('[canMove]="canChooseDestination()"');
    expect(html).toContain('(moveSelected)="bulkMove($event)"');
    expect(ts).toContain('byDestination(');
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

  /**
   * The search can hide selected cards. Bulk Delete and Change radius act on the selected cards still on
   * screen (`actionIds`), and the bulk bar counts those, so nothing is deleted or resized unseen.
   */
  it.each(LISTS)('%s acts in bulk on the visible selection only', template => {
    const html = read(template);
    const ts = read(template.replace('.html', '.ts'));

    expect(html).toContain('<app-bulk-action-bar');
    expect(html).toContain('[count]="actionIds().length"');
    expect(ts).toMatch(/readonly actionIds = computed\(\s*\(\) =>\s*visibleSelection\(\s*this\.selectedIds\(\)/);
    expect(ts).not.toContain('[...this.selectedIds()]');
    expect(ts).not.toContain('this.selectedIds().size');
  });
});
