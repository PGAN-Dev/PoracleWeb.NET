import * as fs from 'fs';
import * as path from 'path';

/**
 * The dashboard's "Add" quick actions, its first-alarm tip and the onboarding alarm step send `?add=1`
 * to whichever alarm list comes first on the instance, which can be any of the ten. A list that does not
 * honour it lands the user on the page with no dialog, which is the defect the parameter exists to fix
 * (UI audit U1), so every list must read it.
 */
describe('add-on-arrival parity', () => {
  const LISTS = [
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
  ];

  it.each(LISTS)('%s opens its add dialog when asked to on arrival', list => {
    const source = fs.readFileSync(path.join(__dirname, `${list}.component.ts`), 'utf8');
    const ngOnInit = source.slice(source.indexOf('ngOnInit(): void {'));
    const body = ngOnInit.slice(0, ngOnInit.indexOf('\n  }\n'));

    expect(source).toContain('private readonly takeAddRequest = addOnArrival();');
    // Pokéstop events hands the dialog the loaded list, so it opens after the first load instead.
    const deferred = 'this.loadItems(this.takeAddRequest() ? () => this.openAddDialog() : undefined);';
    expect(body.includes('if (this.takeAddRequest()) this.openAddDialog();') || body.includes(deferred)).toBe(true);
  });

  it('covers every alarm route the dashboard and onboarding can send it to', () => {
    const routes = (file: string) =>
      [...fs.readFileSync(path.join(__dirname, file), 'utf8').matchAll(/route: '\/([a-z-]+)'/g)].map(m => m[1]);
    const listRoutes = LISTS.map(l => l.split('/')[0]);
    const alarmRoutes = [
      ...routes('dashboard/dashboard.component.ts'),
      ...routes('../shared/components/onboarding/onboarding.component.ts'),
    ].filter(r => !['areas', 'cleaning', 'profiles'].includes(r));

    expect(alarmRoutes.length).toBeGreaterThan(0);
    for (const route of alarmRoutes) expect(listRoutes).toContain(route);
  });
});
