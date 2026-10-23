import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Touch screens get 44px targets on the map pages (WCAG 2.5.5, UI audit cross D10). Measured at 390px
 * with a coarse pointer before this: the Areas Map/List toggle 34px tall, Leaflet's zoom buttons and the
 * basemap button 30x30, the place labels 20px, the attribution links 14px. A mouse keeps the compact size,
 * so every rule here lives under `@media (pointer: coarse)`.
 *
 * Asserted against the stylesheets because jsdom has no layout and no pointer media.
 */
describe('map controls are 44px on touch', () => {
  const read = (file: string): string => readFileSync(join(__dirname, file), 'utf8');

  /** The text of every `@media (pointer: coarse)` block in a stylesheet, joined. */
  const coarse = (source: string): string => {
    const blocks: string[] = [];
    let at = source.indexOf('@media (pointer: coarse)');
    while (at >= 0) {
      let depth = 0;
      let i = source.indexOf('{', at);
      const start = i;
      for (; i < source.length; i++) {
        if (source[i] === '{') depth++;
        if (source[i] === '}' && --depth === 0) break;
      }
      blocks.push(source.slice(start, i + 1));
      at = source.indexOf('@media (pointer: coarse)', i);
    }
    return blocks.join('\n');
  };

  /** The declarations under `selector` inside `css`. */
  const rule = (css: string, selector: string): string => {
    const start = css.indexOf(`${selector} {`);
    if (start < 0) throw new Error(`no "${selector}" under pointer: coarse`);
    return css.slice(start, css.indexOf('}', start));
  };

  const global = coarse(read('../styles.scss'));
  const areas = coarse(read('modules/areas/area-list.component.scss'));
  const areaMap = coarse(read('shared/components/area-map/area-map.component.scss'));

  // The zoom control only: leaflet-draw's toolbar is also a .leaflet-bar, drawn from a 30px sprite sheet.
  it('Leaflet zoom buttons', () => {
    const body = rule(global, '.leaflet-touch .leaflet-control-zoom a');
    expect(body).toContain('width: 44px');
    expect(body).toContain('height: 44px');
  });

  it('the basemap button and its menu entries', () => {
    const toggle = rule(global, '.basemap-control__toggle');
    expect(toggle).toContain('width: 44px');
    expect(toggle).toContain('height: 44px');
    expect(rule(global, '.basemap-control__option')).toContain('min-height: 44px');
  });

  it('attribution links stand apart, at least 24px tall', () => {
    expect(rule(global, '.leaflet-control-attribution a')).toMatch(/min-height: (2[4-9]|[3-9]\d)px/);
  });

  it('the Areas Map/List toggle', () => {
    expect(rule(areas, '.mode-btn')).toContain('min-height: 44px');
  });

  it('a place marker, without moving the dot off its point', () => {
    // The hit area grows through a pseudo-element, so the marker's own box (and its anchor) is unchanged.
    expect(rule(areaMap, '.place-marker::before')).toMatch(/inset: -12px/);
  });
});
