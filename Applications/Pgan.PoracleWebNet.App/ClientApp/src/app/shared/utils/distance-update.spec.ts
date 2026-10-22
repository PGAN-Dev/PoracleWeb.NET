import { combineDistanceResults, distanceUpdateMessage, skippedAny, Translator } from './distance-update';

describe('distance-update', () => {
  const translate: Translator = {
    instant: (key: string, params?: Record<string, unknown>) => (params ? `${key}(${JSON.stringify(params)})` : key),
  };

  describe('distanceUpdateMessage', () => {
    it('is the page’s own success line when nothing was skipped', () => {
      expect(distanceUpdateMessage({ skippedAreaScoped: [], skippedPlaceScoped: [], updated: 3 }, 'Done', translate)).toBe('Done');
    });

    it('names the area-scoped alarms a radius could not apply to', () => {
      expect(distanceUpdateMessage({ skippedAreaScoped: [4, 5], skippedPlaceScoped: [], updated: 1 }, 'Done', translate)).toBe(
        'Done. WHERE.DISTANCE_SKIPPED_AREAS({"count":2})',
      );
    });

    it('names the place-scoped alarms a zero radius could not apply to', () => {
      expect(distanceUpdateMessage({ skippedAreaScoped: [], skippedPlaceScoped: [9], updated: 0 }, 'Done', translate)).toBe(
        'Done. WHERE.DISTANCE_SKIPPED_PLACE({"count":1})',
      );
    });

    it('does not double a full stop the success line already has', () => {
      expect(distanceUpdateMessage({ skippedAreaScoped: [1], updated: 1 }, 'Done!', translate)).toBe(
        'Done! WHERE.DISTANCE_SKIPPED_AREAS({"count":1})',
      );
    });

    it('names the rules left alone because the new radius would clash with another', () => {
      expect(distanceUpdateMessage({ skippedConflict: [3, 6, 8], updated: 4 }, 'Done', translate)).toBe(
        'Done. WHERE.DISTANCE_SKIPPED_CONFLICT({"count":3})',
      );
    });

    it('lists every kind of skip in order', () => {
      expect(
        distanceUpdateMessage({ skippedAreaScoped: [1], skippedConflict: [2], skippedPlaceScoped: [3], updated: 0 }, 'Done', translate),
      ).toBe(
        'Done. WHERE.DISTANCE_SKIPPED_AREAS({"count":1}) WHERE.DISTANCE_SKIPPED_PLACE({"count":1}) WHERE.DISTANCE_SKIPPED_CONFLICT({"count":1})',
      );
    });

    it('tolerates a server too old to report skips', () => {
      // An older PoracleWeb.NET answers { updated } alone, and void from a mocked service.
      expect(distanceUpdateMessage({ updated: 2 }, 'Done', translate)).toBe('Done');
      expect(distanceUpdateMessage(undefined, 'Done', translate)).toBe('Done');
    });
  });

  describe('combineDistanceResults', () => {
    it('adds the raid and egg halves of one selection together', () => {
      expect(
        combineDistanceResults({ skippedAreaScoped: [1], skippedPlaceScoped: [], updated: 2 }, undefined, {
          skippedAreaScoped: [7],
          skippedPlaceScoped: [8],
          updated: 1,
        }),
      ).toEqual({ skippedAreaScoped: [1, 7], skippedConflict: [], skippedPlaceScoped: [8], updated: 3 });
    });

    it('sums the conflict-skipped rules across types', () => {
      expect(combineDistanceResults({ skippedConflict: [1], updated: 1 }, { skippedConflict: [2, 3], updated: 1 }).skippedConflict).toEqual(
        [1, 2, 3],
      );
    });
  });

  describe('skippedAny', () => {
    it('counts conflict-skipped rules', () => {
      expect(skippedAny({ skippedConflict: [5], updated: 0 })).toBe(true);
      expect(skippedAny({ skippedConflict: [], updated: 0 })).toBe(false);
    });
  });
});
