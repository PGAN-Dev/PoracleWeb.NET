import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { contrastRatio } from './shared/utils/contrast';

/**
 * Amber warning text on an amber tint. #e65100 measured 3.39:1 on Admin > Server settings' warning hint
 * in the light theme (on #faf1e9) and 4.34:1 in the dark one (on #251e14), at 13px, where WCAG AA asks
 * 4.5:1. The same colour sat on the Help page's "English only" chip and the Versions card's amber alert.
 *
 * Asserted against the stylesheets: jsdom resolves neither the tint nor the theme, and the backgrounds
 * below are the composited colours axe measured (the hint, and the `code` inside it a shade darker).
 */
describe('amber warning text reads at 4.5:1', () => {
  const read = (file: string): string => readFileSync(join(__dirname, file), 'utf8');

  /** The `color` declared in the first block opened by `opener` in `source`. */
  const colorIn = (source: string, opener: string): string => {
    const start = source.indexOf(opener);
    if (start < 0) throw new Error(`no "${opener}"`);
    const block = source.slice(start, source.indexOf('}', start));
    const match = /\n\s*color:\s*(#[0-9a-fA-F]{6})\s*;/.exec(block);
    if (!match) throw new Error(`no hex color under "${opener}"`);
    return match[1];
  };

  const settings = read('modules/admin/admin-settings.component.scss');
  const help = read('modules/help/help.component.scss');
  const serverCard = read('shared/components/server-profile-card/server-profile-card.component.scss');

  it.each(['#faf1e9', '#ebe3db'])('Server settings warning hint, light, on %s', background => {
    expect(contrastRatio(colorIn(settings, '&.warning {'), background)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['#251e14', '#231c13'])('Server settings warning hint, dark, on %s', background => {
    expect(contrastRatio(colorIn(settings, ':host-context(.dark-theme) .config-hint.warning {'), background)).toBeGreaterThanOrEqual(4.5);
  });

  it('Help "English only" chip, light', () => {
    expect(contrastRatio(colorIn(help, '\n.lang-fallback-chip {'), '#fff0e0')).toBeGreaterThanOrEqual(4.5);
  });

  it('Versions card amber alert, light', () => {
    expect(contrastRatio(colorIn(serverCard, '\n.server-alert-warn {'), '#fff8e1')).toBeGreaterThanOrEqual(4.5);
  });
});
