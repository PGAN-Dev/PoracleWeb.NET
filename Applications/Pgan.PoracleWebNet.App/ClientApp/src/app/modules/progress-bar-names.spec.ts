import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * axe rates a `mat-progress-bar` with no accessible name as serious. Nine templates drew one bare, so a
 * screen reader met a progressbar with nothing to say about what was in progress. Read from the
 * templates: a new bar added without a name fails here instead of in the next audit.
 */
describe('mat-progress-bar accessible names', () => {
  const root = join(__dirname, '..');

  const templates = (dir: string): string[] =>
    readdirSync(dir).flatMap(entry => {
      const full = join(dir, entry);
      if (statSync(full).isDirectory()) return templates(full);
      return full.endsWith('.html') ? [full] : [];
    });

  const bars = templates(root).flatMap(file =>
    (readFileSync(file, 'utf8').match(/<mat-progress-bar\b[^>]*>/g) ?? []).map(tag => ({ file: file.slice(root.length + 1), tag })),
  );

  it('finds the bars it is meant to police', () => {
    expect(bars.length).toBeGreaterThanOrEqual(8);
  });

  it('names every bar', () => {
    const unnamed = bars.filter(b => !/(\[attr\.aria-label\]|aria-label|aria-labelledby)=/.test(b.tag)).map(b => b.file);

    expect(unnamed).toEqual([]);
  });
});
