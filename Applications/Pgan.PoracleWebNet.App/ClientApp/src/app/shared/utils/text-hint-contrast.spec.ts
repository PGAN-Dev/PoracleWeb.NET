import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { contrastRatio } from './contrast';

/**
 * `--text-hint` is set on text all over the app -- the Pokemon add dialog's selection hint, empty
 * states, card captions -- and at 38% alpha it read at 2.67:1 on the light dialog surface and 3.58:1 on
 * the dark one, where WCAG AA asks 4.5:1 of small text. #919 lifted `--text-muted` and left this one.
 *
 * Checked against the surfaces the token actually lands on in each theme: the page, the cards, and the
 * dialog containers, which in Material 3 are a step darker (light) or lighter (dark) than a card.
 */
describe('--text-hint reads at 4.5:1 in both themes', () => {
  const styles = readFileSync(join(__dirname, '../../../styles.scss'), 'utf8');

  const tokenIn = (selector: string): [number, number, number, number] => {
    const block = styles.slice(styles.indexOf(`${selector} {`));
    const match = /--text-hint:\s*rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)/.exec(block.slice(0, block.indexOf('}')));
    if (!match) throw new Error(`no --text-hint in ${selector}`);
    return [Number(match[1]), Number(match[2]), Number(match[3]), Number(match[4])];
  };

  const over = ([r, g, b, a]: [number, number, number, number], background: string): string => {
    const bg = [1, 3, 5].map(i => parseInt(background.slice(i, i + 2), 16));
    const mix = [r, g, b].map((v, i) => Math.round(v * a + bg[i] * (1 - a)));
    return `#${mix.map(v => v.toString(16).padStart(2, '0')).join('')}`;
  };

  const LIGHT_SURFACES = ['#ffffff', '#f4f3f6', '#ece6f0', '#e9e7ec'];
  const DARK_SURFACES = ['#121212', '#1e1e1e', '#2b2930', '#36343b'];

  it.each(LIGHT_SURFACES)('light theme on %s', surface => {
    const hint = over(tokenIn('body:not(.dark-theme)'), surface);
    expect(contrastRatio(hint, surface)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(DARK_SURFACES)('dark theme on %s', surface => {
    const hint = over(tokenIn('body.dark-theme'), surface);
    expect(contrastRatio(hint, surface)).toBeGreaterThanOrEqual(4.5);
  });
});
