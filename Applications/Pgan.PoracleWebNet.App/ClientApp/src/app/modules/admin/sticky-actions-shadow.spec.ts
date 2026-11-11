import { readFileSync } from 'fs';
import { join } from 'path';

/**
 * The pinned actions column on Admin > Users and Admin > Destinations is opaque at every width, because
 * columns scroll under it wherever the table overflows. Its left-edge shadow is the cue that columns
 * continue underneath, so it belongs only where the table scrolls (the phone block): drawn at every
 * width it read as a dashed line down every row on a wide screen where nothing scrolls.
 */
describe('sticky actions column stylesheet contract', () => {
  const files = ['admin-users.component.scss', 'admin-destinations.component.scss'];

  /** Splits a stylesheet into the text outside any media block and the text inside `max-width` blocks. */
  function split(scss: string): { outside: string; phone: string } {
    let outside = '';
    let phone = '';
    const re = /@media \(max-width: 599px\) \{([\s\S]*?)\n\}/g;
    let last = 0;
    for (let m = re.exec(scss); m; m = re.exec(scss)) {
      outside += scss.slice(last, m.index);
      phone += m[1];
      last = m.index + m[0].length;
    }
    outside += scss.slice(last);
    return { outside, phone };
  }

  const actionsRule = /\.mat-column-actions[^{]*\{([^}]*)\}/g;

  function actionsDeclarations(block: string): string {
    return Array.from(block.matchAll(actionsRule), m => m[1]).join('\n');
  }

  it.each(files)('%s keeps the pinned column opaque at every width and shadows it only where it scrolls', file => {
    const { outside, phone } = split(readFileSync(join(__dirname, file), 'utf8'));

    expect(actionsDeclarations(outside)).toMatch(/background-color:/);
    expect(actionsDeclarations(outside)).not.toMatch(/box-shadow:/);
    expect(actionsDeclarations(phone)).toMatch(/box-shadow:/);
    expect(phone).toMatch(/overflow-x: auto/);
  });
});
