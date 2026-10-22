import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A dialog opened with Material's default focus lands on its first tabbable element, which in every
 * alarm add and edit dialog (and the quick-pick apply dialog) is the first tab. The tab then drew its
 * grey focus fill and the 3px ring on a mouse or touch open: a selected-and-hovered look nobody chose
 * (UI audit N7). Those dialogs open focused on their title instead, which a screen reader announces and
 * from which Tab reaches the tabs first.
 */
describe('tabbed dialogs open focused on their title', () => {
  const root = __dirname;

  const files = (dir: string): string[] =>
    readdirSync(dir).flatMap(name => {
      const path = join(dir, name);
      return statSync(path).isDirectory() ? files(path) : [path];
    });

  const all = files(join(root, 'modules')).concat(files(join(root, 'shared')));

  /** Component class names whose template has a tab group. */
  const tabbed = all
    .filter(f => f.endsWith('.component.html') && readFileSync(f, 'utf8').includes('<mat-tab-group'))
    .map(f => readFileSync(f.replace(/\.html$/, '.ts'), 'utf8'))
    .map(source => /export class (\w+DialogComponent)/.exec(source)?.[1])
    .filter((name): name is string => !!name);

  /** Every `.open(Component, { ... })` in the non-spec sources, with its options text. */
  const opens = all
    .filter(f => f.endsWith('.ts') && !f.endsWith('.spec.ts'))
    .flatMap(f => {
      const source = readFileSync(f, 'utf8');
      const found: { component: string; file: string; options: string }[] = [];
      // `m.` covers a lazily imported dialog (`this.dialog.open(m.QuickPickApplyDialogComponent, ...)`).
      const pattern = /\.open\(\s*(?:\w+\.)?(\w+DialogComponent)\s*,\s*\{/g;
      let match: null | RegExpExecArray;
      while ((match = pattern.exec(source))) {
        let depth = 0;
        let i = match.index + match[0].length - 1;
        const start = i;
        for (; i < source.length; i++) {
          if (source[i] === '{') depth++;
          if (source[i] === '}' && --depth === 0) break;
        }
        found.push({ component: match[1], file: f.slice(root.length + 1), options: source.slice(start, i + 1) });
      }
      return found;
    });

  it('finds the tabbed dialogs and where they are opened', () => {
    expect(tabbed.length).toBeGreaterThanOrEqual(21);
    expect(opens.filter(o => tabbed.includes(o.component)).length).toBeGreaterThanOrEqual(22);
  });

  it('opens every one of them with autoFocus on the first heading', () => {
    const missing = opens
      .filter(o => tabbed.includes(o.component))
      .filter(o => !/autoFocus:\s*'first-heading'/.test(o.options))
      .map(o => `${o.file}: ${o.component}`);

    expect(missing).toEqual([]);
  });
});
