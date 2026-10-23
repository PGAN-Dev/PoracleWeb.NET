import { AlarmScope, SavedPlace } from '../../../core/models';
import { kmToMetres } from '../../utils/alarm-scope';

/** What the Delivery tab's map draws for a scope: a circle, some areas, or both empty. */
export interface ScopeDrawing {
  /** Area names to shade, as stored (lowercase). */
  areas: string[];
  /** The circle's centre and radius, when the scope measures from a point that is known. */
  circle: null | { latitude: number; longitude: number; radiusMetres: number };
}

/**
 * Reads a scope into the shapes that show it.
 *
 * "Near a point" draws a circle around the pin or the saved place, at the radius typed (even one the
 * dialog will refuse: a 500 km circle over a whole state is the clearest way to say why). "Only in
 * specific areas" shades the areas chosen, and "Anywhere in my areas" the profile's. A point that isn't
 * known (no pin, a place since deleted) draws nothing rather than a circle in the Atlantic.
 */
export function scopeDrawing(scope: AlarmScope, pin: null | SavedPlace, named: SavedPlace[], profileAreas: string[]): ScopeDrawing {
  if (scope.mode === 'areas') return { areas: scope.areas ?? [], circle: null };

  if (scope.distanceKm === undefined && scope.mode === 'profile') return { areas: profileAreas, circle: null };

  const centre = scope.mode === 'place' ? (named.find(p => p.label === scope.placeLabel) ?? null) : pin;
  const radiusMetres = kmToMetres(scope.distanceKm ?? 0);
  if (!centre || !(radiusMetres > 0)) return { areas: [], circle: null };

  return { areas: [], circle: { latitude: centre.latitude, longitude: centre.longitude, radiusMetres } };
}
