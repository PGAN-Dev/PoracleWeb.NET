import { INITIAL_VIEW_MAX_ZOOM, INITIAL_VIEW_PRIORITY, LOCATION_ONLY_ZOOM, planInitialView, InitialViewState } from './initial-view';

const state = (overrides: Partial<InitialViewState> = {}): InitialViewState => ({
  fittedPriority: 0,
  hasAllBounds: false,
  hasCustomBounds: false,
  hasSelectionBounds: false,
  hasUserLocation: false,
  viewLockedByUser: false,
  ...overrides,
});

describe('planInitialView', () => {
  describe('the ladder', () => {
    it('opens on the user own geofences above everything else', () => {
      // My Geofences is the only page that binds these, and the shapes you drew are what you came
      // for there. A pin must not displace them.
      const plan = planInitialView(state({ hasAllBounds: true, hasCustomBounds: true, hasSelectionBounds: true, hasUserLocation: true }));

      expect(plan?.source).toBe('custom');
    });

    it('opens on the pin rather than the selected areas', () => {
      // Areas binds the selection. A multi-area selection frames a whole region and opens too far
      // out to act on, where the pin says where the person is.
      const plan = planInitialView(state({ hasAllBounds: true, hasSelectionBounds: true, hasUserLocation: true }));

      expect(plan?.source).toBe('location');
    });

    it('opens on the selected areas when there is no pin', () => {
      const plan = planInitialView(state({ hasAllBounds: true, hasSelectionBounds: true }));

      expect(plan?.source).toBe('selection');
    });

    it('opens on the pinned location when there are no shapes of the user own', () => {
      const plan = planInitialView(state({ hasAllBounds: true, hasUserLocation: true }));

      expect(plan?.source).toBe('location');
    });

    it('falls back to every area only when there is no other signal', () => {
      const plan = planInitialView(state({ hasAllBounds: true }));

      expect(plan?.source).toBe('all');
    });

    it('leaves the map alone when there is nothing to fit at all', () => {
      expect(planInitialView(state())).toBeNull();
    });
  });

  describe('upgrading a view that was fitted before the data arrived', () => {
    // The Areas page loads the feed and the selection from independent requests, so the map is
    // routinely on screen before the selection lands. This is the case that makes the whole
    // difference between the fix working and working most of the time.
    it('re-fits when the selection arrives after the map already fitted everything', () => {
      const plan = planInitialView(state({ fittedPriority: INITIAL_VIEW_PRIORITY.all, hasAllBounds: true, hasSelectionBounds: true }));

      expect(plan).toEqual({ priority: INITIAL_VIEW_PRIORITY.selection, source: 'selection' });
    });

    it('re-fits when the location arrives after the map already fitted everything', () => {
      const plan = planInitialView(state({ fittedPriority: INITIAL_VIEW_PRIORITY.all, hasAllBounds: true, hasUserLocation: true }));

      expect(plan?.source).toBe('location');
    });
  });

  describe('not moving a map the user is working with', () => {
    // Toggling an area re-emits selectedAreas. If that re-fitted, the map would jump on every
    // click -- worse than the bug this replaced.
    it('does not move the map when the selection changes at the same rank', () => {
      const plan = planInitialView(
        state({ fittedPriority: INITIAL_VIEW_PRIORITY.selection, hasAllBounds: true, hasSelectionBounds: true }),
      );

      expect(plan).toBeNull();
    });

    it('does not zoom back out when the user deselects everything', () => {
      // With a pin present the map is already fitted to it, so losing the selection leaves nothing
      // better to move to. Rule 2: equal rank is not an upgrade.
      const plan = planInitialView(state({ fittedPriority: INITIAL_VIEW_PRIORITY.location, hasAllBounds: true, hasUserLocation: true }));

      expect(plan).toBeNull();
    });

    it('does not fall back to the whole feed when a selection is cleared and there is no pin', () => {
      const plan = planInitialView(state({ fittedPriority: INITIAL_VIEW_PRIORITY.selection, hasAllBounds: true }));

      expect(plan).toBeNull();
    });

    it('never overrides a view the user chose, however much better the alternative', () => {
      const plan = planInitialView(state({ hasAllBounds: true, hasSelectionBounds: true, hasUserLocation: true, viewLockedByUser: true }));

      expect(plan).toBeNull();
    });
  });

  describe('zoom limits', () => {
    it('caps an automatic fit below the tightest explicit region jump', () => {
      // The region jump uses maxZoom 14; an inferred view should not be tighter than a stated one.
      expect(INITIAL_VIEW_MAX_ZOOM).toBeLessThan(14);
    });

    it('opens a location-only view wide enough to show neighbouring areas', () => {
      expect(LOCATION_ONLY_ZOOM).toBeGreaterThanOrEqual(10);
      expect(LOCATION_ONLY_ZOOM).toBeLessThanOrEqual(12);
    });
  });
});
