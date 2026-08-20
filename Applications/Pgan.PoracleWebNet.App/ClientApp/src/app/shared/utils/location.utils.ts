/** The shape every location-ish value in the app shares. */
export interface Coordinates {
  latitude: number;
  longitude: number;
}

/**
 * Poracle stores "no pin" as 0,0 rather than null, so a cleared pin comes back from the API as a
 * real pair of coordinates in the Gulf of Guinea.
 *
 * Clearing the pin looked right until you navigated away and back, because the page set its own
 * state to null while the reload took 0,0 at face value and rendered it as coordinates. The same
 * literal lives in half a dozen components, each rewriting it; this is the one place it belongs.
 */
export function hasPin(location: Coordinates | null | undefined): boolean {
  return !!location && (location.latitude !== 0 || location.longitude !== 0);
}

/** The location, or null when it is the 0,0 that means unset. */
export function pinOrNull<T extends Coordinates>(location: null | T | undefined): null | T {
  return hasPin(location) ? (location as T) : null;
}
