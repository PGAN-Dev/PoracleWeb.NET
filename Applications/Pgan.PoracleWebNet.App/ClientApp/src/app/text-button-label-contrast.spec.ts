import { readdirSync, readFileSync } from 'fs';
import { join, relative } from 'path';

import { contrastRatio } from './shared/utils/contrast';

/**
 * A text button filled with an alarm type's colour carries its label in that fill, so the label has to
 * reach WCAG AA's 4.5:1 like any other text. The empty-state "Add" buttons were written as
 * `style="background: #8bc34a; color: #fff"`, and white on most Material 500 shades misses: the nest
 * button read 2.09:1, pokemon 2.78:1, quick picks 2.8:1. They only render on an empty list, so a page
 * sweep run with a seeded user never sees them.
 *
 * This reads the templates and stylesheets rather than rendering them, so every list's empty state is
 * covered. A FAB holds only an icon and is exempt.
 */
const APP_DIR = __dirname;
const MIN = 4.5;
const TEXT_BUTTON = /\b(mat-flat-button|mat-raised-button|mat-button|matButton)\b/;

function files(dir: string, ext: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return files(path, ext);
    return entry.name.endsWith(ext) ? [path] : [];
  });
}

function lineOf(source: string, index: number): number {
  return source.slice(0, index).split('\n').length;
}

/** `background` and label `color` declared together, as hex, in one declaration list. */
function pair(declarations: string): null | { background: string; color: string } {
  const background = /(?:^|;|\s)background(?:-color)?\s*:\s*(#[0-9a-f]{3,6})\b/i.exec(declarations)?.[1];
  const raw = /(?:^|;|\s)color\s*:\s*(#[0-9a-f]{3,6}|white)\b/i.exec(declarations)?.[1];
  if (!background || !raw) return null;
  return { background, color: raw.toLowerCase() === 'white' ? '#ffffff' : raw };
}

describe('text buttons filled with a fixed colour', () => {
  it('keep their label at 4.5:1 or better in every template', () => {
    const failures: string[] = [];
    for (const file of files(APP_DIR, '.html')) {
      const source = readFileSync(file, 'utf8');
      for (const match of source.matchAll(/<button\b[^>]*>/g)) {
        const tag = match[0];
        if (!TEXT_BUTTON.test(tag) || /\bmat-(mini-)?fab\b/.test(tag)) continue;
        const style = /\bstyle="([^"]*)"/.exec(tag)?.[1];
        const colours = style ? pair(style) : null;
        if (!colours) continue;
        const ratio = contrastRatio(colours.background, colours.color);
        if (ratio < MIN) {
          failures.push(
            `${relative(APP_DIR, file)}:${lineOf(source, match.index ?? 0)} ${colours.color} on ${colours.background} = ${ratio.toFixed(2)}:1`,
          );
        }
      }
    }
    expect(failures).toEqual([]);
  });

  it('keep their label at 4.5:1 or better in every call-to-action class', () => {
    const failures: string[] = [];
    for (const file of files(APP_DIR, '.scss')) {
      const source = readFileSync(file, 'utf8');
      for (const match of source.matchAll(/([^{};]*\bcta[\w-]*[^{};]*)\{([^{}]*)\}/g)) {
        const colours = pair(match[2]);
        if (!colours) continue;
        const ratio = contrastRatio(colours.background, colours.color);
        if (ratio < MIN) {
          failures.push(`${relative(APP_DIR, file)}:${lineOf(source, match.index ?? 0)} ${match[1].trim()} ${ratio.toFixed(2)}:1`);
        }
      }
    }
    expect(failures).toEqual([]);
  });

  it('still recognises a passing button, so the scan is not vacuous', () => {
    expect(pair('background: #9c27b0; color: #fff')).toEqual({ background: '#9c27b0', color: '#fff' });
    expect(contrastRatio('#9c27b0', '#ffffff')).toBeGreaterThanOrEqual(MIN);
  });
});
