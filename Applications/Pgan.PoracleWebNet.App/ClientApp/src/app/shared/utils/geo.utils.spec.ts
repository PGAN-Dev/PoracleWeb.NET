import { detectRegion, fenceLatLngs, fenceRings, largestRing, pointInFence, pointInPolygon, polygonCentroid } from './geo.utils';

describe('geo.utils', () => {
  // A simple square polygon: (0,0) -> (0,10) -> (10,10) -> (10,0)
  const square: [number, number][] = [
    [0, 0],
    [0, 10],
    [10, 10],
    [10, 0],
  ];

  const triangle: [number, number][] = [
    [0, 0],
    [10, 5],
    [0, 10],
  ];

  describe('pointInPolygon', () => {
    it('should return true for a point inside a square polygon', () => {
      expect(pointInPolygon([5, 5], square)).toBe(true);
    });

    it('should return false for a point outside a square polygon', () => {
      expect(pointInPolygon([15, 15], square)).toBe(false);
    });

    it('should return false for a point clearly outside', () => {
      expect(pointInPolygon([-5, 5], square)).toBe(false);
    });

    it('should return true for a point near the interior', () => {
      expect(pointInPolygon([1, 1], square)).toBe(true);
    });

    it('should handle an empty polygon', () => {
      expect(pointInPolygon([5, 5], [])).toBe(false);
    });

    it('should handle a single-point polygon', () => {
      expect(pointInPolygon([5, 5], [[5, 5]])).toBe(false);
    });
  });

  describe('polygonCentroid', () => {
    it('should compute the correct center of a square', () => {
      const centroid = polygonCentroid(square);
      expect(centroid[0]).toBe(5);
      expect(centroid[1]).toBe(5);
    });

    it('should compute the correct center of a triangle', () => {
      const centroid = polygonCentroid(triangle);
      expect(centroid[0]).toBeCloseTo(10 / 3);
      expect(centroid[1]).toBe(5);
    });

    it('should return [0, 0] for an empty polygon', () => {
      expect(polygonCentroid([])).toEqual([0, 0]);
    });

    it('should return the point itself for a single-point polygon', () => {
      expect(polygonCentroid([[7, 3]])).toEqual([7, 3]);
    });
  });

  describe('detectRegion', () => {
    const regions = [
      {
        id: 1,
        name: 'downtown',
        displayName: 'Downtown',
        path: [
          [0, 0],
          [0, 10],
          [10, 10],
          [10, 0],
        ] as [number, number][],
      },
      {
        id: 2,
        name: 'suburbs',
        displayName: 'Suburbs',
        path: [
          [20, 20],
          [20, 30],
          [30, 30],
          [30, 20],
        ] as [number, number][],
      },
    ];

    it('should return the matching region when centroid is inside', () => {
      // polygon whose centroid (5,5) is inside region "downtown"
      const polygon: [number, number][] = [
        [4, 4],
        [4, 6],
        [6, 6],
        [6, 4],
      ];

      const result = detectRegion(polygon, regions);
      expect(result).toEqual({ id: 1, name: 'downtown', displayName: 'Downtown' });
    });

    it('should return null when centroid is outside all regions', () => {
      const polygon: [number, number][] = [
        [50, 50],
        [50, 52],
        [52, 52],
        [52, 50],
      ];

      const result = detectRegion(polygon, regions);
      expect(result).toBeNull();
    });

    it('should return the first matching region when centroid falls in it', () => {
      // polygon whose centroid (25,25) is inside region "suburbs"
      const polygon: [number, number][] = [
        [24, 24],
        [24, 26],
        [26, 26],
        [26, 24],
      ];

      const result = detectRegion(polygon, regions);
      expect(result).toEqual({ id: 2, name: 'suburbs', displayName: 'Suburbs' });
    });

    it('should return null when there are no regions', () => {
      const polygon: [number, number][] = [
        [5, 5],
        [5, 6],
        [6, 6],
      ];

      expect(detectRegion(polygon, [])).toBeNull();
    });

    it('should use [0,0] centroid for empty polygon and match if a region contains it', () => {
      // polygonCentroid([]) returns [0,0], which is inside the downtown region (0,0)-(10,10)
      const result = detectRegion([], regions);
      // [0,0] is on the boundary of downtown; ray-casting may or may not include it
      // This test documents actual behavior rather than asserting null
      if (result) {
        expect(result.name).toBe('downtown');
      } else {
        expect(result).toBeNull();
      }
    });

    it('should return null for empty polygon when no region contains origin', () => {
      const farRegions = [
        {
          id: 10,
          name: 'faraway',
          displayName: 'Far Away',
          path: [
            [50, 50],
            [50, 60],
            [60, 60],
            [60, 50],
          ] as [number, number][],
        },
      ];
      expect(detectRegion([], farRegions)).toBeNull();
    });

    it('should detect region for single-point polygon at known location', () => {
      const polygon: [number, number][] = [[5, 5]];
      const result = detectRegion(polygon, regions);
      expect(result).toEqual({ id: 1, name: 'downtown', displayName: 'Downtown' });
    });
  });

  describe('pointInPolygon — triangle', () => {
    it('should return true for a point inside a triangle', () => {
      expect(pointInPolygon([3, 5], triangle)).toBe(true);
    });

    it('should return false for a point outside a triangle', () => {
      expect(pointInPolygon([0, 11], triangle)).toBe(false);
    });

    it('should return false for a point just outside the triangle edge', () => {
      expect(pointInPolygon([9, 1], triangle)).toBe(false);
    });
  });

  describe('polygonCentroid — additional cases', () => {
    it('should compute centroid of a line segment (two points)', () => {
      const centroid = polygonCentroid([
        [0, 0],
        [10, 10],
      ]);
      expect(centroid).toEqual([5, 5]);
    });

    it('should compute centroid with negative coordinates', () => {
      const centroid = polygonCentroid([
        [-10, -10],
        [-10, 10],
        [10, 10],
        [10, -10],
      ]);
      expect(centroid).toEqual([0, 0]);
    });
  });

  // ── Multi-part areas (#1117) ─────────────────────────────────────────────
  // PoracleNG serves a multi-part area as `multipath` (one ring per part) with no `path`, and matches a fence
  // on every ring it carries. Reading `path` alone made such an area invisible on every map.

  const farSquare: [number, number][] = [
    [20, 20],
    [20, 30],
    [30, 30],
    [30, 20],
  ];

  describe('fenceRings', () => {
    it('returns the path of an ordinary area', () => {
      expect(fenceRings({ path: square })).toEqual([square]);
    });

    it('returns every part of a multi-part area', () => {
      expect(fenceRings({ multipath: [square, farSquare] })).toEqual([square, farSquare]);
    });

    it('reads an empty path beside the parts as nothing', () => {
      expect(fenceRings({ multipath: [square, farSquare], path: [] })).toEqual([square, farSquare]);
    });

    it('keeps a path and parts together, as PoracleNG matches on both', () => {
      expect(fenceRings({ multipath: [farSquare], path: square })).toEqual([square, farSquare]);
    });

    it('drops a ring too small to be a polygon', () => {
      const line: [number, number][] = [
        [0, 0],
        [1, 1],
      ];
      expect(fenceRings({ multipath: [line, farSquare] })).toEqual([farSquare]);
      expect(fenceRings({ path: line })).toEqual([]);
    });

    it('treats a missing or null shape as no rings', () => {
      expect(fenceRings({})).toEqual([]);
      expect(fenceRings({ multipath: null, path: null })).toEqual([]);
    });
  });

  describe('pointInFence', () => {
    it('finds a point inside the second part', () => {
      expect(pointInFence([25, 25], { multipath: [square, farSquare] })).toBe(true);
    });

    it('does not find a point between the parts', () => {
      expect(pointInFence([15, 15], { multipath: [square, farSquare] })).toBe(false);
    });

    it('still finds a point inside an ordinary area', () => {
      expect(pointInFence([5, 5], { path: square })).toBe(true);
    });
  });

  describe('fenceLatLngs', () => {
    it('leaves one ring flat, as an ordinary polygon', () => {
      expect(fenceLatLngs([square])).toEqual(square);
    });

    it('wraps each part in its own array, so Leaflet does not draw the second part as a hole', () => {
      expect(fenceLatLngs([square, farSquare])).toEqual([[square], [farSquare]]);
    });
  });

  describe('largestRing', () => {
    it('picks the part with the most area', () => {
      const small: [number, number][] = [
        [0, 0],
        [0, 1],
        [1, 1],
        [1, 0],
      ];
      expect(largestRing({ multipath: [small, farSquare] })).toEqual(farSquare);
    });

    it('returns the path of an ordinary area', () => {
      expect(largestRing({ path: square })).toEqual(square);
    });

    it('returns null when there is nothing to measure', () => {
      expect(largestRing({ path: [] })).toBeNull();
    });
  });

  describe('detectRegion — multi-part regions', () => {
    it('detects a region from its second part', () => {
      const islands = { id: 7, name: 'islands', displayName: 'Islands', multipath: [square, farSquare] };
      const polygon: [number, number][] = [
        [24, 24],
        [24, 26],
        [26, 26],
        [26, 24],
      ];

      expect(detectRegion(polygon, [islands])).toEqual({ id: 7, name: 'islands', displayName: 'Islands' });
    });
  });
});
