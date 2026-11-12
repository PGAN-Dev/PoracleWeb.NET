import { readFileSync } from 'fs';
import { join } from 'path';

/**
 * jsdom does no layout, so this pins the CSS contract instead. `app-area-map` renders the region-search
 * toolbar and the map wrapper as siblings. With the wrapper at `height: 100%` the pair overran the host by
 * the toolbar's height and `overflow: hidden` cut the map's bottom edge, attribution included. The OSM
 * credit is a licence condition, so the host must be a flex column and the wrapper must take the remainder.
 */
describe('Areas page map fits its slot', () => {
  const scss = readFileSync(join(__dirname, 'area-list.component.scss'), 'utf-8');
  const block = (selector: string): string => {
    const start = scss.indexOf(selector);
    expect(start).toBeGreaterThan(-1);
    return scss.slice(start, scss.indexOf('}', start));
  };

  it('makes the map host a flex column', () => {
    const host = block('app-area-map {');
    expect(host).toMatch(/display:\s*flex/);
    expect(host).toMatch(/flex-direction:\s*column/);
  });

  it('lets the map wrapper take the remainder instead of a second 100%', () => {
    const wrapper = block('.map-wrapper {');
    expect(wrapper).toMatch(/flex:\s*1/);
    expect(wrapper).toMatch(/min-height:\s*0/);
    expect(wrapper).not.toMatch(/height:\s*100%/);
  });
});
