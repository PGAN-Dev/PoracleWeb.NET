namespace Pgan.PoracleWebNet.Core.Services;

public static class GeometryHelpers
{
    public readonly record struct BoundingBox(double MinLat, double MinLon, double MaxLat, double MaxLon)
    {
        public bool Contains(double lat, double lon) =>
            lat >= this.MinLat && lat <= this.MaxLat && lon >= this.MinLon && lon <= this.MaxLon;

        public static BoundingBox FromPolygon(double[][] polygon)
        {
            if (polygon.Length == 0)
            {
                return default;
            }

            var minLat = double.MaxValue;
            var minLon = double.MaxValue;
            var maxLat = double.MinValue;
            var maxLon = double.MinValue;

            foreach (var pt in polygon)
            {
                if (pt.Length < 2)
                {
                    continue;
                }

                if (pt[0] < minLat)
                {
                    minLat = pt[0];
                }
                if (pt[0] > maxLat)
                {
                    maxLat = pt[0];
                }
                if (pt[1] < minLon)
                {
                    minLon = pt[1];
                }
                if (pt[1] > maxLon)
                {
                    maxLon = pt[1];
                }
            }

            return new BoundingBox(minLat, minLon, maxLat, maxLon);
        }
    }

    public static bool PointInPolygon(double lat, double lon, double[][] polygon)
    {
        var n = polygon.Length;
        if (n < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var yi = polygon[i][0];
            var xi = polygon[i][1];
            var yj = polygon[j][0];
            var xj = polygon[j][1];
            if (((yi > lat) != (yj > lat)) &&
                (lon < ((xj - xi) * (lat - yi) / (yj - yi)) + xi))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
