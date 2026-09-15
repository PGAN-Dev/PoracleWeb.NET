import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The PVP band on a Pokemon alarm card keeps a fixed light background (`#f5f5f5`) in both themes,
 * so every colour painted on it must be fixed too.
 *
 * `.pvp-rank` used `var(--text-muted)`, which `styles.scss` sets to `rgba(255, 255, 255, 0.5)` under
 * `body.dark-theme`. That put the rank range at roughly 1.1:1 on the band: rendered, present in the
 * DOM, and invisible. Users read it as the ranks they had set having been dropped. See #800.
 *
 * Asserted against the stylesheet because that is where the defect lives — jsdom resolves neither
 * custom properties nor contrast, so a rendered-component test could not tell the two colours apart.
 */
describe('PVP band colours', () => {
  const scss = readFileSync(join(__dirname, 'pokemon-list.component.scss'), 'utf8');

  /** The declarations of a top-level rule, e.g. `.pvp-rank`. */
  function ruleBody(selector: string): string {
    const start = scss.indexOf(`\n${selector} {`);
    expect(start).toBeGreaterThan(-1);
    const end = scss.indexOf('\n}', start);
    return scss.slice(start, end);
  }

  it('paints the band itself with a fixed background', () => {
    // If this ever becomes theme-driven, the rules below can go back to theme variables.
    expect(ruleBody('.pvp-row')).toContain('background: #f5f5f5;');
  });

  it.each(['.pvp-rank', '.pvp-badge'])('gives %s a literal colour, not a theme variable', selector => {
    const body = ruleBody(selector);
    expect(body).toMatch(/color: (#|rgba?\()/);
    expect(body).not.toContain('var(--');
  });
});
