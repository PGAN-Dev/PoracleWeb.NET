import { TranslateService } from '@ngx-translate/core';

import { describeScope, formatAreaList, kmToMetres, metresToKm, scopeOf, scopeToFields, titleCaseArea } from './alarm-scope';

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
      expect(scopeOf(null, null, 0)).toEqual({ distanceKm: 0, mode: 'profile' });
    });

    it('treats an empty area list as inherited rather than as an empty restriction', () => {
      // An alarm restricted to no areas would match nothing. PoracleNG stores the cleared state as an
      // empty column, so this has to read back as "no override".
      expect(scopeOf(null, [], 0)).toEqual({ distanceKm: 0, mode: 'profile' });
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

  it('rounds metres to a tenth of a km and back', () => {
    expect(metresToKm(2450)).toBe(2.5);
    expect(kmToMetres(2.5)).toBe(2500);
  });

  it('leaves an already-capitalised name alone', () => {
    expect(titleCaseArea('Avoca Beach')).toBe('Avoca Beach');
  });
});
