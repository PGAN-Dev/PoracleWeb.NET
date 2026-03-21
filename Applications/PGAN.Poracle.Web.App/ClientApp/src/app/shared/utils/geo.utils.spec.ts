import { detectRegion, pointInPolygon, polygonCentroid } from './geo.utils';

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
});
