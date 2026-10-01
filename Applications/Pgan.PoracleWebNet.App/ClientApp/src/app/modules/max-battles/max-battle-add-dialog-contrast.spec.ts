import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { contrastRatio } from '../../shared/utils/contrast';

/**
 * axe, run inside the Max Battle add dialog, flagged every level star: #ffd600 on the light dialog
 * surface is 1.34:1 and the Gigantamax star's #e040fb 3.18:1, where a 16-18px glyph needs 4.5:1. Both
 * keep their hue, deeper in light; in dark the yellow already reads at 10:1 and the magenta lightens.
 */
describe('Max battle add dialog star colours', () => {
  const scss = readFileSync(join(__dirname, 'max-battle-add-dialog.component.scss'), 'utf8');

  const colourOf = (selector: string): string => {
    const start = scss.indexOf(`${selector} {`);
    if (start < 0) throw new Error(`no rule for ${selector}`);
    const match = /color:\s*(#[0-9a-fA-F]{6})/.exec(scss.slice(start, scss.indexOf('}', start)));
    if (!match) throw new Error(`no hex colour in ${selector}`);
    return match[1];
  };

  const LIGHT_DIALOG = '#faf9fd';
  const DARK_DIALOG = '#2b2930';

  it.each(['.star-icon', '.gmax-star'])('%s reads at 4.5:1 on the light dialog', selector => {
    expect(contrastRatio(colourOf(selector), LIGHT_DIALOG)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['.star-icon', '.gmax-star'])('%s reads at 4.5:1 on the dark dialog', selector => {
    expect(contrastRatio(colourOf(`:host-context(.dark-theme) ${selector}`), DARK_DIALOG)).toBeGreaterThanOrEqual(4.5);
  });
});
