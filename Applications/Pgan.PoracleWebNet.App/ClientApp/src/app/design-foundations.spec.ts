/**
 * @jest-environment node
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

import { compileString } from 'sass';

/**
 * The design direction, held app-wide: body text in Atkinson Hyperlegible, figures that line up, no
 * decoration (pokéball watermarks, tinted header washes, ALL-CAPS eyebrow labels), no entrance motion
 * nobody asked for, and one page header whose primary action is labelled and sits in the same place on
 * every page. Read from the sources, like the parity specs, so a new page or an eleventh list cannot
 * quietly bring any of it back.
 */
const app = __dirname;
const src = join(app, '..');
const clientApp = join(src, '..');

const read = (path: string) => readFileSync(path, 'utf8');

function filesUnder(dir: string, extension: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return filesUnder(path, extension);
    return path.endsWith(extension) && !path.endsWith(`.spec${extension}`) ? [path] : [];
  });
}

function compileGlobalStyles(): string {
  const source = read(join(src, 'styles.scss')).replace(/^@import 'leaflet.*$/gm, '');
  return compileString(source, { loadPaths: [join(clientApp, 'node_modules')], style: 'expanded' }).css;
}

describe('type', () => {
  let css: string;

  beforeAll(() => {
    css = compileGlobalStyles();
  }, 60000);

  it('loads Atkinson Hyperlegible next to Plus Jakarta Sans and Roboto', () => {
    const html = read(join(src, 'index.html'));
    expect(html).toContain('family=Atkinson+Hyperlegible+Next');
    expect(html).toContain('family=Plus+Jakarta+Sans');
    expect(html).toContain('family=Roboto');
  });

  it('sets body text in Atkinson Hyperlegible with Roboto behind it, and headings in Plus Jakarta Sans', () => {
    expect(css).toMatch(/--mat-sys-body-medium-font:\s*"?Atkinson Hyperlegible Next"?,\s*Roboto/);
    expect(css).toMatch(/--mat-sys-headline-small-font:\s*"?Plus Jakarta Sans"?/);
  });

  it('draws figures in tabular widths everywhere, so a column of IV, CP and level lines up', () => {
    const body = [...css.matchAll(/(?:^|\n)body\s*\{([^}]*)\}/g)].map(m => m[1]).join('\n');
    expect(body).toContain('font-variant-numeric: tabular-nums');
  });
});

describe('decoration', () => {
  const stylesheets = [join(src, 'styles.scss'), ...filesUnder(app, '.scss')];

  it('has no pokéball watermark or background drawing in any stylesheet', () => {
    const offenders = stylesheets.filter(path => /url\("data:image\/svg\+xml[^"]*%3Ccircle/.test(read(path)));
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });

  it('labels blocks in sentence case: no ALL-CAPS eyebrow anywhere', () => {
    const offenders = stylesheets.filter(path => /text-transform:\s*uppercase/.test(read(path)));
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });

  it('has no staggered entrance: nothing sets a delay per item', () => {
    const offenders = [...stylesheets, ...filesUnder(app, '.html')].filter(path =>
      /animation-delay(?!:\s*0s !important)|\[style\.animation-delay/.test(read(path)),
    );
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });

  it('has no entrance animation on the alarm grid and no hover lift on cards', () => {
    const css = compileGlobalStyles();
    expect(css).not.toMatch(/\.alarm-grid\s*>\s*\*[^{]*\{[^}]*animation/);
    expect(css).not.toMatch(/\.mat-mdc-card:hover\s*\{[^}]*translateY/);
  }, 60000);
});

describe('page header', () => {
  it('is styled once, globally, and no page redefines it', () => {
    expect(read(join(src, 'styles.scss'))).toMatch(/\n\.page-header \{/);
    const offenders = filesUnder(app, '.scss').filter(path => /(^|\n)\.page-header \{/.test(read(path)));
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });

  const LISTS = [
    'pokemon/pokemon-list',
    'raids/raid-list',
    'quests/quest-list',
    'invasions/invasion-list',
    'pokestop-events/pokestop-event-list',
    'lures/lure-list',
    'nests/nest-list',
    'gyms/gym-list',
    'max-battles/max-battle-list',
    'fort-changes/fort-change-list',
  ];

  it.each(LISTS)('%s puts a labelled "New alarm" in the header instead of an unlabelled FAB', list => {
    const html = read(join(app, 'modules', `${list}.component.html`));
    const header = html.slice(html.indexOf('<div class="page-header">'), html.indexOf('</div>\n</div>') + 1);

    expect(html).not.toMatch(/\bmat-(mini-)?fab\b/);
    expect(header).toMatch(/<button\s+mat-flat-button\s+class="page-primary-action"\s+\(click\)="openAddDialog\(\)">/);
    expect(header).toContain("{{ 'ALARM.NEW_ALARM' | translate }}");
  });

  it.each([
    ['profiles-overview/profile-overview', 'openAddProfileDialog()', 'PROFILES.NEW_PROFILE'],
    ['quick-picks/quick-pick-list', 'onAddNew()', 'QUICK_PICKS.NEW_QUICK_PICK'],
  ])('%s labels its primary action the same way', (page, click, key) => {
    const html = read(join(app, 'modules', `${page}.component.html`));

    expect(html).not.toMatch(/\bmat-(mini-)?fab\b/);
    expect(html).toContain(`class="page-primary-action"`);
    expect(html).toContain(`(click)="${click}"`);
    expect(html).toContain(`{{ '${key}' | translate }}`);
  });

  it('uses the shared structure wherever a page draws a header: title and purpose inside .page-header-text, at the top level', () => {
    const templates = filesUnder(join(app, 'modules'), '.html').filter(path => read(path).includes('class="page-header"'));
    expect(templates.length).toBeGreaterThan(20);
    const offenders = templates.filter(path => {
      const html = read(path);
      const header = html.slice(html.indexOf('<div class="page-header">'));
      const opensWithText = /^<div class="page-header">\s*<div class="page-header-text">\s*<h1>/.test(header);
      // The header sits at the top of the page, not inside a padded wrapper that indents it a second time.
      const atTopLevel = /(^|\n)<div class="page-header">/.test(html);
      return !opensWithText || !atTopLevel;
    });
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });

  it('has no FAB left on any page', () => {
    const offenders = filesUnder(join(app, 'modules'), '.html').filter(path => /\bmat-(mini-)?fab\b/.test(read(path)));
    expect(offenders.map(path => relative(src, path))).toEqual([]);
  });
});

describe('English casing', () => {
  const en = JSON.parse(read(join(src, 'assets', 'i18n', 'en.json'))) as Record<string, Record<string, unknown>>;
  /** Names that keep their capital wherever they sit: the game's own words and the site's. */
  const PROPER = new Set(['Pokémon', 'Pokéstop', 'Mega', 'Max', 'Giovanni', 'Rocket', 'GO', 'PoracleWeb.NET', 'PoracleNG']);

  const titleCased = (value: string) =>
    value
      .split(/\s+/)
      .slice(1)
      .some(word => /^[A-Z][a-z]/.test(word) && !PROPER.has(word.replace(/[^\p{L}.]/gu, '')));

  it('keeps nav labels, dashboard counts and page titles in sentence case', () => {
    const labels: [string, unknown][] = [
      ...Object.entries(en['NAV']).map(([key, value]): [string, unknown] => [`NAV.${key}`, value]),
      ...Object.entries(en['DASHBOARD'])
        .filter(([key]) => key.startsWith('CARD_'))
        .map(([key, value]): [string, unknown] => [`DASHBOARD.${key}`, value]),
      ...Object.entries(en)
        .filter(([, section]) => typeof section === 'object' && typeof section['PAGE_TITLE'] === 'string')
        .map(([key, section]): [string, unknown] => [`${key}.PAGE_TITLE`, section['PAGE_TITLE']]),
    ];
    const offenders = labels
      .filter(([, value]) => typeof value === 'string' && titleCased(value))
      .map(([key, value]) => `${key}: ${value}`);
    expect(offenders).toEqual([]);
  });
});
