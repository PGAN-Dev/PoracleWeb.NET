import { TranslateService } from '@ngx-translate/core';

import {
  describeScope,
  formatDistance,
  formatKm,
  formatAreaList,
  kmToMetres,
  metresToKm,
  MAX_RADIUS_METRES,
  bulkRadiusAcceptable,
  radiusAcceptable,
  scopeOf,
  scopeToFields,
  titleCaseArea,
} from './alarm-scope';

/** Echoes the key and interpolation so assertions read against the shape, not a translation. */
const translate = {
  instant: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
} as unknown as TranslateService;

describe('alarm-scope', () => {
  describe('scopeOf', () => {
    it('reads areas as the areas mode', () => {
      expect(scopeOf(null, ['terrigal'], 0)).toEqual({ areas: ['terrigal'], mode: 'areas' });
    });

    it('reads a label and a radius as the place mode, in km', () => {
      expect(scopeOf('home', null, 2500)).toEqual({ distanceKm: 2.5, mode: 'place', placeLabel: 'home' });
    });

    it('reads a radius with no place as measured from the pin, not as inherited areas', () => {
      // This is the pre-existing "within N km of me" alarm. Collapsing it into the areas reading would
      // put the opposite words on the card.
      expect(scopeOf(null, null, 500)).toEqual({ distanceKm: 0.5, mode: 'profile' });
    });

    it('reads no overrides and no radius as inherited', () => {
      expect(scopeOf(null, null, 0)).toEqual({ mode: 'profile' });
    });

    it('treats an empty area list as inherited rather than as an empty restriction', () => {
      // An alarm restricted to no areas would match nothing. PoracleNG stores the cleared state as an
      // empty column, so this has to read back as "no override".
      expect(scopeOf(null, [], 0)).toEqual({ mode: 'profile' });
    });

    it('prefers areas when a row somehow carries both', () => {
      // PoracleNG refuses to store both, but a row written by an older client might. Areas win because
      // they are the more restrictive of the two, so the alarm cannot silently widen.
      expect(scopeOf('home', ['terrigal'], 500).mode).toBe('areas');
    });
  });

  describe('scopeToFields', () => {
    it('clears with empty values rather than null, so an override can be taken off', () => {
      // null means "not stated, keep what is stored" on the write path. Sending null here would make
      // the override impossible to remove.
      expect(scopeToFields({ mode: 'profile' })).toEqual({ overrideAreas: [], overrideLocationLabel: '', distance: 0 });
    });

    it('zeroes the radius when areas are chosen', () => {
      // Areas and a radius are mutually exclusive upstream; leaving a stale radius on the form would
      // be refused with a message about a field the user did not touch.
      expect(scopeToFields({ areas: ['terrigal'], mode: 'areas' }).distance).toBe(0);
    });

    it('sends the place radius in metres', () => {
      expect(scopeToFields({ distanceKm: 2.5, mode: 'place', placeLabel: 'home' })).toEqual({
        overrideAreas: [],
        overrideLocationLabel: 'home',
        distance: 2500,
      });
    });
  });

  describe('describeScope', () => {
    it('names the place and the radius', () => {
      expect(describeScope({ distanceKm: 2, mode: 'place', placeLabel: 'Home' }, [], translate)).toBe(
        'WHERE.NEAR_PLACE:{"distance":"2","place":"Home"}',
      );
    });

    it('distinguishes an inherited scope with areas from one without', () => {
      expect(describeScope({ mode: 'profile' }, ['terrigal'], translate)).toBe('WHERE.PROFILE_AREAS');
      expect(describeScope({ mode: 'profile' }, [], translate)).toBe('WHERE.PROFILE_ANYWHERE');
    });

    it('says the pin, not the areas, when the inherited scope carries a radius', () => {
      expect(describeScope({ distanceKm: 2, mode: 'profile' }, ['terrigal'], translate)).toBe('WHERE.NEAR_PIN:{"distance":"2"}');
    });
  });

  describe('formatAreaList', () => {
    it('title-cases the stored lowercase names', () => {
      // Geofence names are lowercase because Poracle matches case-sensitively. People are not.
      expect(formatAreaList(['avoca beach'], translate)).toBe('Avoca Beach');
    });

    it('counts the tail past three', () => {
      expect(formatAreaList(['a', 'b', 'c', 'd', 'e'], translate)).toBe('WHERE.AREA_LIST_MORE:{"areas":"A, B, C","count":2}');
    });
  });

  /**
   * Rounding to a tenth of a km rewrote every stored radius on the next save of any field: 250 m became
   * 300 m, and a pin radius under 50 m became 0, which reads as "anywhere in my areas".
   */
  it.each([40, 30, 250, 1234, 2450, 500000])('carries %i m to km and back exactly', metres => {
    expect(kmToMetres(metresToKm(metres))).toBe(metres);
  });

  it('quotes a bound in the display language', () => {
    expect(formatKm(0.1, 'de')).toBe('0,1');
    expect(formatKm(0.1, 'en')).toBe('0.1');
    expect(formatKm(20037.509, 'en')).toBe('20,037.509');
  });

  it('keeps a pin radius under 50 m a radius', () => {
    expect(scopeOf(null, null, 40)).toEqual({ distanceKm: 0.04, mode: 'profile' });
  });

  it('converts a typed radius to whole metres without snapping to 100 m', () => {
    expect(kmToMetres(1.234)).toBe(1234);
    expect(kmToMetres(0.25)).toBe(250);
  });

  it('shows a radius with as many decimals as it holds, up to metres', () => {
    expect(formatDistance(0.04)).toBe('0.04');
    expect(formatDistance(1.234)).toBe('1.234');
    expect(formatDistance(2)).toBe('2');
    expect(formatDistance(2.5)).toBe('2.5');
  });

  it('leaves an already-capitalised name alone', () => {
    expect(titleCaseArea('Avoca Beach')).toBe('Avoca Beach');
  });

  /**
   * The dialogs offer 0.1 to 100 km. The server's own cap is half the Earth's circumference, and
   * production holds rules far past 100 km, so an edit that leaves such a radius alone must still save.
   */
  describe('radiusAcceptable', () => {
    it('accepts the ends of the range and the values between', () => {
      expect(radiusAcceptable(0.1)).toBe(true);
      expect(radiusAcceptable(1)).toBe(true);
      expect(radiusAcceptable(100)).toBe(true);
    });

    it('refuses zero, a negative, past 100 km and an empty field', () => {
      expect(radiusAcceptable(0)).toBe(false);
      expect(radiusAcceptable(-5)).toBe(false);
      expect(radiusAcceptable(100.1)).toBe(false);
      expect(radiusAcceptable(500)).toBe(false);
      expect(radiusAcceptable(null)).toBe(false);
      expect(radiusAcceptable(Number.NaN)).toBe(false);
    });

    it('accepts a stored radius outside the range when it is left as it is', () => {
      expect(radiusAcceptable(500, 500)).toBe(true);
      expect(radiusAcceptable(10000, 10000)).toBe(true);
    });

    it('refuses a new value outside the range even when the stored one was outside it too', () => {
      expect(radiusAcceptable(600, 500)).toBe(false);
    });

    it('never treats a stored zero as a radius to keep', () => {
      expect(radiusAcceptable(0, 0)).toBe(false);
    });
  });

  /** The bulk dialog edits existing rules, so it takes what the server takes: above 0, up to AlarmDistance.MaxMetres. */
  describe('bulkRadiusAcceptable', () => {
    it('accepts a radius far past 100 km, up to half the Earth', () => {
      expect(bulkRadiusAcceptable(10000)).toBe(true);
      expect(bulkRadiusAcceptable(MAX_RADIUS_METRES / 1000)).toBe(true);
      expect(bulkRadiusAcceptable(0.001)).toBe(true);
    });

    it('refuses zero, what rounds to zero metres, past the server cap, and an empty field', () => {
      expect(bulkRadiusAcceptable(0)).toBe(false);
      expect(bulkRadiusAcceptable(0.0004)).toBe(false);
      expect(bulkRadiusAcceptable(MAX_RADIUS_METRES / 1000 + 0.001)).toBe(false);
      expect(bulkRadiusAcceptable(null)).toBe(false);
    });
  });
});
