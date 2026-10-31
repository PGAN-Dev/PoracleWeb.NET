/**
 * Ray-casting point-in-polygon check.
 * @param point [lat, lng]
 * @param polygon array of [lat, lng] pairs forming a closed polygon
 */
export function pointInPolygon(point: [number, number], polygon: [number, number][]): boolean {
  const [y, x] = point;
  let inside = false;

  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const [yi, xi] = polygon[i];
    const [yj, xj] = polygon[j];

    const intersect = yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi;
    if (intersect) inside = !inside;
  }

  return inside;
}

/**
 * Compute centroid of a polygon (simple average of all points).
 * @param polygon array of [lat, lng] pairs
 * @returns [lat, lng] centroid
 */
export function polygonCentroid(polygon: [number, number][]): [number, number] {
  if (polygon.length === 0) return [0, 0];

  let latSum = 0;
  let lngSum = 0;

  for (const [lat, lng] of polygon) {
    latSum += lat;
    lngSum += lng;
  }

  return [latSum / polygon.length, lngSum / polygon.length];
}

/**
 * Calculate polygon area in square kilometers using the spherical excess formula.
 * @param polygon array of [lat, lng] pairs
 * @returns area in km²
 */
export function polygonAreaSqKm(polygon: [number, number][]): number {
  if (polygon.length < 3) return 0;

  const toRad = (deg: number) => (deg * Math.PI) / 180;
  const R = 6371; // Earth radius in km

  // Spherical excess (Girard's theorem) for each triangle with vertex at origin
  let total = 0;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const [lat1, lng1] = polygon[j];
    const [lat2, lng2] = polygon[i];
    total += (toRad(lng2) - toRad(lng1)) * (2 + Math.sin(toRad(lat1)) + Math.sin(toRad(lat2)));
  }

  return Math.abs((total * R * R) / 2);
}

/**
 * A fence as PoracleNG serves it: one `path`, or for a multi-part area one [lat, lng] ring per part in
 * `multipath` and no `path` at all.
 */
export interface FenceShape {
  multipath?: [number, number][][] | null;
  path?: [number, number][] | null;
}

/**
 * Every ring of a fence with at least three points: the path, then each part. PoracleNG matches a fence on all
 * of them, so drawing and containment read the same list. See #1117.
 */
export function fenceRings(fence: FenceShape): [number, number][][] {
  return [fence.path ?? [], ...(fence.multipath ?? [])].filter(ring => ring.length >= 3);
}

/** Whether a point falls inside any part of a fence. */
export function pointInFence(point: [number, number], fence: FenceShape): boolean {
  return fenceRings(fence).some(ring => pointInPolygon(point, ring));
}

/**
 * The rings in the shape `L.polygon` wants. Leaflet reads `[ring, ring]` as an outline and a hole in it, so
 * each part of a multi-part area goes in its own array; one ring stays flat.
 */
export function fenceLatLngs(rings: [number, number][][]): [number, number][] | [number, number][][][] {
  return rings.length === 1 ? rings[0] : rings.map(ring => [ring]);
}

/** The part of a fence with the most area, where one point has to stand for it, or null with none. */
export function largestRing(fence: FenceShape): [number, number][] | null {
  let largest: [number, number][] | null = null;
  let largestArea = -1;
  for (const ring of fenceRings(fence)) {
    const area = polygonAreaSqKm(ring);
    if (area > largestArea) {
      largest = ring;
      largestArea = area;
    }
  }
  return largest;
}

/**
 * Auto-detect which region contains a polygon's centroid.
 * @param polygon the drawn polygon coordinates
 * @param regions array of region geofences with their path, or their parts for a multi-part region
 * @returns the matching region or null
 */
export function detectRegion(
  polygon: [number, number][],
  regions: (FenceShape & { displayName: string; id: number; name: string })[],
): { id: number; name: string; displayName: string } | null {
  const centroid = polygonCentroid(polygon);
  for (const region of regions) {
    if (pointInFence(centroid, region)) {
      return { id: region.id, name: region.name, displayName: region.displayName };
    }
  }
  return null;
}
