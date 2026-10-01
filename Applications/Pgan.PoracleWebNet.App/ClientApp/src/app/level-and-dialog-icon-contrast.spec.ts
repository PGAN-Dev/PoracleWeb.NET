import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { contrastRatio } from './shared/utils/contrast';

/**
 * The siblings of the dialog stars #922 fixed: the level stars on the raid and max-battle list cards,
 * the Gigantamax mark beside them, and the coloured icons and amber warning text in the two admin
 * dialogs (geofence approval, GeoJSON import). Each was a Material 500 shade on a near-white surface,
 * 1.6:1 to 3.2:1, where a glyph or line of text this size needs 4.5:1. Light takes a deeper shade of
 * the same hue; dark keeps the bright original, which already reads there.
 */
const LIGHT_CARD = '#f4f3f6';
const DARK_CARD = '#211f26';
const LIGHT_DIALOG = '#faf9fd';
const DARK_DIALOG = '#2b2930';
const AMBER_BOX = '#fff8e1';
/** `color-mix(in srgb, #2196f3 6%, transparent)` over the light dialog. */
const LIGHT_INFO_ROW = '#edf3fc';

function scss(path: string): string {
  return readFileSync(join(__dirname, path), 'utf8');
}

/** The first hex `color:` declared in the rule whose selector list starts exactly with `selector`. */
function colourOf(source: string, selector: string, nth = 0): string {
  let from = 0;
  for (let i = 0; i <= nth; i++) {
    const at = source.indexOf(`${selector} {`, from);
    if (at < 0) throw new Error(`no rule ${selector}`);
    from = at + 1;
  }
  const start = from - 1;
  const match = /(?:^|\s)color:\s*(#[0-9a-fA-F]{6})/.exec(source.slice(start, source.indexOf('}', start)));
  if (!match) throw new Error(`no hex colour in ${selector}`);
  return match[1];
}

/** The colour a `:host-context(.dark-theme)` rule listing `selector` gives it. */
function darkColourOf(source: string, selector: string): string {
  for (const match of source.matchAll(/([^{}]*:host-context\(\.dark-theme\)[^{}]*)\{([^{}]*)\}/g)) {
    if (match[1].split(',').some(part => part.trim() === `:host-context(.dark-theme) ${selector}`)) {
      const colour = /color:\s*(#[0-9a-fA-F]{6})/.exec(match[2]);
      if (colour) return colour[1];
    }
  }
  throw new Error(`no dark rule for ${selector}`);
}

describe('level stars and admin dialog icons', () => {
  const raid = scss('modules/raids/raid-list.component.scss');
  const maxBattle = scss('modules/max-battles/max-battle-list.component.scss');
  const approval = scss('shared/components/geofence-approval-dialog/geofence-approval-dialog.component.scss');
  const geojson = scss('shared/components/geojson-import-dialog/geojson-import-dialog.component.scss');

  it.each([
    ['raid star', raid, '.star-icon'],
    ['max battle star', maxBattle, '.star-icon'],
    ['gigantamax icon', maxBattle, '.gmax-icon'],
    ['level label', maxBattle, '.level-label-text'],
  ])('%s reads at 4.5:1 on the light card and on the dark card', (_name, source, selector) => {
    expect(contrastRatio(colourOf(source, selector), LIGHT_CARD)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(darkColourOf(source, selector), DARK_CARD)).toBeGreaterThanOrEqual(4.5);
  });

  it('the approval dialog title icon reads in both themes', () => {
    expect(contrastRatio(colourOf(approval, '.title-icon'), LIGHT_DIALOG)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(darkColourOf(approval, '.title-icon'), DARK_DIALOG)).toBeGreaterThanOrEqual(4.5);
  });

  it('the GeoJSON import icons read in both themes', () => {
    expect(contrastRatio(colourOf(geojson, '.drop-zone-icon'), LIGHT_DIALOG)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(darkColourOf(geojson, '.drop-zone-icon'), DARK_DIALOG)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(colourOf(geojson, '> mat-icon'), LIGHT_INFO_ROW)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(darkColourOf(geojson, '.file-info > mat-icon'), DARK_DIALOG)).toBeGreaterThanOrEqual(4.5);
  });

  it('the GeoJSON warning text reads on its amber box, which is light in both themes', () => {
    expect(contrastRatio(colourOf(geojson, '.warning-banner'), AMBER_BOX)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(colourOf(geojson, '.warning-count'), AMBER_BOX)).toBeGreaterThanOrEqual(4.5);
  });
});
