import { readdirSync, readFileSync } from 'node:fs';
import { join, sep } from 'node:path';

/**
 * Every dialog that hosts the scope picker keeps Save off while the picker holds a radius it won't accept,
 * and every edit dialog tells the picker the stored radius so a rule saved past 100 km stays editable.
 *
 * The picker alone can't stop a save; each host has to bind it. Wiring some of the twenty-one hosts and
 * not the rest is the regression shape this codebase keeps hitting, so the sources are read rather than
 * twenty-one dialogs mounted, and a new host fails here until it is wired.
 */
describe('the scope picker radius check is wired into every host', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  const HOSTS = readdirSync(__dirname, { recursive: true })
    .map(String)
    .filter(file => file.endsWith('.component.html'))
    .map(file => file.split(sep).join('/').slice(0, -'.html'.length))
    .filter(file => read(`${file}.html`).includes('<app-scope-picker'))
    .sort();

  it('finds the ten add dialogs, the ten edit dialogs and the quick-pick apply dialog', () => {
    expect(HOSTS).toHaveLength(21);
  });

  it.each(HOSTS)('%s keeps Save off while the radius is out of range', host => {
    const html = read(`${host}.html`);

    expect(html).toContain('[(radiusValid)]="radiusValid"');
    expect(html).toMatch(/\(click\)="(save|apply)\(\)" \[disabled\]="[^"]*\|\| !radiusValid\(\)"/);
    expect(read(`${host}.ts`)).toContain('readonly radiusValid = signal(true);');
  });

  it.each(HOSTS.filter(host => host.includes('-edit-dialog')))('%s lets a stored radius stand', host => {
    expect(read(`${host}.html`)).toContain('[storedRadiusKm]="storedRadiusKm"');
    expect(read(`${host}.ts`)).toMatch(/readonly storedRadiusKm = metresToKm\(this\.data(\.item)?\.distance\);/);
  });
});

/** The bulk radius dialog names the selection it acts on (the selected cards still on screen), so every list says how many. */
describe('every list tells the bulk radius dialog how many alarms are selected', () => {
  const read = (relative: string) => readFileSync(join(__dirname, relative), 'utf8');

  const LISTS = readdirSync(__dirname, { recursive: true })
    .map(String)
    .filter(file => file.endsWith('-list.component.ts'))
    .filter(file => read(file).includes('DistanceDialogComponent'));

  it('finds all ten alarm lists', () => {
    expect(LISTS).toHaveLength(10);
  });

  it.each(LISTS)('%s passes the selection count', list => {
    expect(read(list)).toMatch(/this\.dialog\.open\(DistanceDialogComponent, \{[^}]*data: \{ count: this\.actionIds\(\)\.length \}/);
  });
});
