import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { contrastRatio } from '../../shared/utils/contrast';

/**
 * axe, run inside the Quick Pick apply dialog, flagged the title icon: #4caf50 on the light dialog
 * surface is 2.65:1, under the 3:1 a 24px glyph needs. It was an inline style, so the theme could not
 * touch it. The exclusion summary beside it printed #ff9800 text, 2.2:1, and only escaped the sweep
 * because it appears once something is excluded.
 */
describe('Quick pick apply dialog colours', () => {
  const html = readFileSync(join(__dirname, 'quick-pick-apply-dialog.component.html'), 'utf8');
  const scss = readFileSync(join(__dirname, 'quick-pick-apply-dialog.component.scss'), 'utf8');

  const colourOf = (selector: string): string => {
    const start = scss.indexOf(`${selector} {`);
    if (start < 0) throw new Error(`no rule for ${selector}`);
    const match = /color:\s*(#[0-9a-fA-F]{6})/.exec(scss.slice(start, scss.indexOf('}', start)));
    if (!match) throw new Error(`no hex colour in ${selector}`);
    return match[1];
  };

  const LIGHT_DIALOG = '#faf9fd';
  const DARK_DIALOG = '#2b2930';

  it('does not hard-code the title icon colour', () => {
    expect(html).not.toMatch(/<mat-icon[^>]*style\.color/);
    expect(html).toContain('class="title-icon"');
  });

  it('draws the title icon at 3:1 or better in both themes', () => {
    expect(contrastRatio(colourOf('.title-icon'), LIGHT_DIALOG)).toBeGreaterThanOrEqual(3);
    expect(contrastRatio(colourOf(':host-context(.dark-theme) .title-icon'), DARK_DIALOG)).toBeGreaterThanOrEqual(3);
  });

  it('prints the exclusion summary at 4.5:1 or better in both themes', () => {
    expect(contrastRatio(colourOf('.exclusion-summary'), LIGHT_DIALOG)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(colourOf(':host-context(.dark-theme) .exclusion-summary'), DARK_DIALOG)).toBeGreaterThanOrEqual(4.5);
  });
});
