/**
 * @jest-environment node
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { compileString } from 'sass';

import { contrastRatio } from './shared/utils/contrast';

/**
 * The global theme, compiled the way the build compiles it, so these assert what reaches the browser
 * rather than what the source looks like: `color="warn"` drawing in the error role (it drew nothing
 * under `mat.theme`), and nothing else picking up a colour variant by accident; the keyboard focus ring;
 * and reduced motion zeroing the stagger delays as well as the durations.
 */
const clientApp = join(__dirname, '..', '..');

function compileGlobalStyles(): string {
  // Leaflet's stylesheets are plain CSS imports that Sass leaves alone; drop them so the compile does
  // not depend on how they resolve.
  const source = readFileSync(join(clientApp, 'src', 'styles.scss'), 'utf8').replace(/^@import 'leaflet.*$/gm, '');
  return compileString(source, { loadPaths: [join(clientApp, 'node_modules')], style: 'expanded' }).css;
}

/** Every declaration block whose selector list mentions `fragment`, joined. */
function blocksFor(css: string, fragment: string): string {
  return [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .filter(([, selector]) => selector.includes(fragment))
    .map(([, , body]) => body)
    .join('\n');
}

describe('global theme', () => {
  let css: string;

  beforeAll(() => {
    css = compileGlobalStyles();
  }, 60000);

  it('draws a warn button, icon button and chip in the error role', () => {
    const button = blocksFor(css, '.mat-warn.mat-mdc-button-base');
    expect(button).toContain('--mat-button-filled-container-color: var(--mat-sys-error)');
    expect(button).toContain('--mat-button-filled-label-text-color: var(--mat-sys-on-error)');
    expect(button).toContain('--mat-button-text-label-text-color: var(--mat-sys-error)');
    expect(button).toContain('--mat-button-outlined-label-text-color: var(--mat-sys-error)');
    expect(button).toContain('--mat-button-protected-label-text-color: var(--mat-sys-error)');
    expect(button).toContain('--mat-icon-button-icon-color: var(--mat-sys-error)');
    expect(blocksFor(css, '.mat-warn.mat-mdc-standard-chip')).toContain(
      '--mat-chip-elevated-selected-container-color: var(--mat-sys-error-container)',
    );
    expect(blocksFor(css, '.mat-warn.mat-mdc-slide-toggle')).toContain('var(--mat-sys-error)');
    expect(blocksFor(css, '.mat-warn.mat-mdc-checkbox')).toContain('var(--mat-sys-error)');
  });

  it('follows the theme in dark mode: the warn variant emits system variables, not light hex', () => {
    expect(blocksFor(css, '.mat-warn')).not.toMatch(/#[0-9a-f]{6}\b/i);
  });

  it('leaves primary and accent alone, which checkboxes, radios, list options and FABs carry by default', () => {
    // color-variants-backwards-compatibility would map .mat-accent to tertiary and turn all of them cyan.
    expect(css).not.toMatch(/\.mat-accent[.\s,{]/);
    expect(css).not.toMatch(/\.mat-primary[.\s,{]/);
  });

  it('turns on Material strong focus indicators in the primary colour', () => {
    const html = blocksFor(css, 'html');
    expect(html).toContain('--mat-focus-indicator-display: block');
    expect(html).toContain('--mat-focus-indicator-border-color: var(--mat-sys-primary)');
  });

  it('draws the focus ring white on the toolbar gradient and near-black on a dark-ink toolbar', () => {
    expect(blocksFor(css, '.app-toolbar')).toContain('--mat-focus-indicator-border-color: #fff');
    expect(blocksFor(css, '.app-toolbar.toolbar-dark-ink')).toContain('--mat-focus-indicator-border-color: #212121');
  });

  it('zeroes animation and transition delays under prefers-reduced-motion', () => {
    const start = css.indexOf('@media (prefers-reduced-motion: reduce)');
    expect(start).toBeGreaterThanOrEqual(0);
    const block = css.slice(start, css.indexOf('}', start));
    expect(block).toContain('animation-delay: 0s !important');
    expect(block).toContain('transition-delay: 0s !important');
    expect(block).toContain('animation-duration: 0.01ms !important');
  });
});

describe('impersonation banner', () => {
  const scss = readFileSync(join(__dirname, 'app.scss'), 'utf8');
  const rule = scss.slice(scss.indexOf('.impersonation-banner {'), scss.indexOf('mat-icon {', scss.indexOf('.impersonation-banner {')));

  it('carries its text at 4.5:1 or better', () => {
    const background = /background-color:\s*(#[0-9a-fA-F]{6})/.exec(rule)?.[1] ?? '';
    const colour = /(?:^|\s)color:\s*(#[0-9a-fA-F]{6})/.exec(rule)?.[1] ?? '';
    expect(contrastRatio(colour, background)).toBeGreaterThanOrEqual(4.5);
  });

  it('says whose alerts a change lands on, in every locale', () => {
    for (const locale of ['en', 'da', 'de', 'es', 'fr', 'it', 'nl', 'pl', 'pt', 'pt-BR', 'sv']) {
      const json = JSON.parse(readFileSync(join(clientApp, 'src', 'assets', 'i18n', `${locale}.json`), 'utf8'));
      const text: string = json.BANNER.VIEWING_AS;
      expect(text.match(/\{\{name\}\}/g)?.length).toBe(2);
    }
    const en = JSON.parse(readFileSync(join(clientApp, 'src', 'assets', 'i18n', 'en.json'), 'utf8'));
    expect(en.BANNER.VIEWING_AS).toBe("Viewing as {{name}}. Changes you make are saved to {{name}}'s alerts.");
  });

  it('passes the name to the banner text', () => {
    const html = readFileSync(join(__dirname, 'app.html'), 'utf8');
    expect(html).toContain("'BANNER.VIEWING_AS' | translate: { name: auth.user()?.username }");
  });
});
