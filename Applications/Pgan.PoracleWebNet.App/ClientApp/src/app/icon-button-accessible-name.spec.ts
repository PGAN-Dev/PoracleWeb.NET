import { readdirSync, readFileSync } from 'fs';
import { join, relative } from 'path';

/**
 * An icon-only button has no text for a screen reader to announce. A `matTooltip` does not fix that:
 * Material wires the tooltip up as a description (aria-describedby), not a name, so axe-core reported
 * 527 unnamed buttons across the 25 routes of the site. Every icon-only Material button therefore
 * carries an aria-label, usually bound to the same translation as its tooltip.
 *
 * This reads the templates rather than rendering them, so it covers every page, including the ones
 * no component spec mounts. An extended FAB has a visible text label and is exempt.
 */
const APP_DIR = __dirname;
const ICON_BUTTON = /\b(mat-icon-button|mat-fab|mat-mini-fab|matIconButton|matFab|matMiniFab)\b/;
const HAS_NAME = /\baria-label(ledby)?\b/;

function templates(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return templates(path);
    return entry.name.endsWith('.html') ? [path] : [];
  });
}

function lineOf(source: string, index: number): number {
  return source.slice(0, index).split('\n').length;
}

function unnamedIconButtons(source: string): number[] {
  const lines: number[] = [];
  for (const match of source.matchAll(/<(button|a)\b[^>]*>/g)) {
    const tag = match[0];
    if (!ICON_BUTTON.test(tag) || /\bextended\b/.test(tag) || HAS_NAME.test(tag)) continue;
    lines.push(lineOf(source, match.index ?? 0));
  }
  return lines;
}

/** A button toggle whose only content is an icon (the geofence submissions view switcher). */
function unnamedIconToggles(source: string): number[] {
  const lines: number[] = [];
  for (const match of source.matchAll(/<mat-button-toggle(?=[\s>])([^>]*)>([\s\S]*?)<\/mat-button-toggle>/g)) {
    const [, attributes, body] = match;
    const text = body.replace(/<mat-icon\b[\s\S]*?<\/mat-icon>/g, '').replace(/<[^>]+>/g, '');
    if (text.trim() || HAS_NAME.test(attributes)) continue;
    lines.push(lineOf(source, match.index ?? 0));
  }
  return lines;
}

describe('icon-only buttons have an accessible name', () => {
  const files = templates(APP_DIR);

  it('finds the templates it is meant to police', () => {
    // Guards against the walk silently finding nothing, which would pass everything.
    expect(files.length).toBeGreaterThan(50);
    expect(files.some(f => f.endsWith('pokemon-list.component.html'))).toBe(true);
  });

  it('flags a tooltip-only icon button, and passes a labelled or extended one', () => {
    expect(
      unnamedIconButtons(`<button mat-icon-button [matTooltip]="'COMMON.EDIT' | translate"><mat-icon>edit</mat-icon></button>`),
    ).toEqual([1]);
    expect(unnamedIconButtons(`<button mat-fab (click)="add()"><mat-icon>add</mat-icon></button>`)).toEqual([1]);
    expect(unnamedIconButtons(`<button mat-icon-button [attr.aria-label]="'COMMON.EDIT' | translate">`)).toEqual([]);
    expect(unnamedIconButtons(`<button mat-fab extended (click)="draw()"><mat-icon>draw</mat-icon> Draw</button>`)).toEqual([]);
    expect(unnamedIconButtons(`<button mat-raised-button (click)="save()">Save</button>`)).toEqual([]);
  });

  it('flags an icon-only button toggle, and passes one with text', () => {
    expect(unnamedIconToggles(`<mat-button-toggle value="card"><mat-icon>grid_view</mat-icon></mat-button-toggle>`)).toEqual([1]);
    expect(
      unnamedIconToggles(`<mat-button-toggle value="card" [aria-label]="x"><mat-icon>grid_view</mat-icon></mat-button-toggle>`),
    ).toEqual([]);
    expect(unnamedIconToggles(`<mat-button-toggle value="all">{{ 'ADMIN.STATUS_ALL' | translate }}</mat-button-toggle>`)).toEqual([]);
  });

  it('holds for every template in the app', () => {
    const offenders = files.flatMap(file => {
      const source = readFileSync(file, 'utf8');
      return [...unnamedIconButtons(source), ...unnamedIconToggles(source)].map(line => `${relative(APP_DIR, file)}:${line}`);
    });

    expect(offenders).toEqual([]);
  });
});
