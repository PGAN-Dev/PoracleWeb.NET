/**
 * Decides what the area map should open on.
 *
 * The map used to open on the bounds of every fence in the feed. On a single-city instance that
 * looked like a sensible default; on a multi-region one it is the whole planet, where no polygon is
 * more than a pixel and nothing can be clicked. The feed is not going to get smaller, so the fix is
 * to open on the shapes the user actually came for and fall back only when there is no better
 * signal. See #693.
 *
 * Two rules do the real work here, and both are easy to break by accident:
 *
 *  1. The anchors are ranked, and a fit only ever *upgrades*. The page loads the geofence feed and
 *     the user's selection from independent requests, so the map is often on screen before the
 *     selection arrives. Without an upgrade the map would fit everything, mark itself done, and
 *     never recover -- intermittently, depending on which response won.
 *  2. Because a fit only upgrades, a selection that changes at the same rank cannot move the map.
 *     Toggling an area therefore restyles it and nothing else. Re-fitting on every toggle would
 *     yank the map out from under the cursor, which is worse than the bug being fixed.
 */

/** Where the opening view is taken from, worst to best. */
export type InitialViewSource = 'all' | 'custom' | 'location' | 'selection';

/**
 * Higher wins.
 *
 * The pin now outranks the selection. #693 ranked the selection highest, on the reasoning that you
 * open on the shapes you came for, but a multi-area selection frames a whole region and the map
 * opens too far out to act on. A pin says where the person actually is, so it wins when one exists.
 *
 * It does NOT outrank `custom`. On My Geofences the shapes you drew are the thing you came for, and
 * opening on your pin instead would be the same mistake in the other direction. The two never
 * coexist in practice -- Areas binds `selectedAreas`, My Geofences binds `customGeofences` -- but
 * the ordering has to be total, and this is the direction that is right on both pages.
 */
export const INITIAL_VIEW_PRIORITY: Record<InitialViewSource, number> = {
  all: 1,
  custom: 4,
  location: 3,
  selection: 2,
};

/**
 * Ceiling for an automatic fit. Admin areas rarely reach it -- a single Richmond area fits at 12-13
 * on its own -- but a user-drawn geofence a few hundred metres across would otherwise land at street
 * level with no surrounding context to add neighbours from. An explicit region jump is allowed to go
 * one step tighter, because it states an intent this has to infer.
 */
export const INITIAL_VIEW_MAX_ZOOM = 13;

/** Roughly 30 km across, so several adjacent areas are visible and clickable around the pin. */
export const LOCATION_ONLY_ZOOM = 11;

export interface InitialViewPlan {
  priority: number;
  source: InitialViewSource;
}

export interface InitialViewState {
  /** Priority of the anchor already fitted. 0 when the map has not been positioned yet. */
  fittedPriority: number;
  hasAllBounds: boolean;
  hasCustomBounds: boolean;
  hasSelectionBounds: boolean;
  hasUserLocation: boolean;
  /** A region jump, a "fit all", a drag or a zoom is a stated intent. Never override it. */
  viewLockedByUser: boolean;
}

/**
 * Returns the view to apply, or null to leave the map where it is.
 */
export function planInitialView(state: InitialViewState): InitialViewPlan | null {
  if (state.viewLockedByUser) return null;

  const available: InitialViewSource[] = [];
  if (state.hasSelectionBounds) available.push('selection');
  if (state.hasCustomBounds) available.push('custom');
  if (state.hasUserLocation) available.push('location');
  if (state.hasAllBounds) available.push('all');

  if (available.length === 0) return null;

  const best = available.reduce((a, b) => (INITIAL_VIEW_PRIORITY[b] > INITIAL_VIEW_PRIORITY[a] ? b : a));
  const priority = INITIAL_VIEW_PRIORITY[best];

  // Equal rank is not an upgrade: this is what stops a selection change from moving the map.
  return priority > state.fittedPriority ? { priority, source: best } : null;
}
