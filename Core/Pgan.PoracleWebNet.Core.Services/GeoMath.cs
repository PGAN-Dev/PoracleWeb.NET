namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Server-side twin of the frontend's <c>shared/utils/geo.utils.ts</c>. Kept in sync by hand -- the two
/// implementations must agree, or the area a user sees while drawing will differ from the one a reviewer
/// sees in Discord.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusKm = 6371;

    /// <summary>Arithmetic mean of the vertices. Good enough to place a marker and pick a containing region.</summary>
    public static (double Lat, double Lon) Centroid(IReadOnlyList<double[]> polygon)
    {
        if (polygon.Count == 0)
        {
            return (0, 0);
        }

        double latSum = 0;
        double lonSum = 0;

        foreach (var point in polygon)
        {
            if (point.Length < 2)
            {
                continue;
            }

            latSum += point[0];
            lonSum += point[1];
        }

        return (latSum / polygon.Count, lonSum / polygon.Count);
    }

    /// <summary>Area in square kilometres via the spherical excess (Girard's theorem) formula.</summary>
    public static double AreaSqKm(IReadOnlyList<double[]> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0;
        }

        double total = 0;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (polygon[i].Length < 2 || polygon[j].Length < 2)
            {
                continue;
            }

            var lat1 = ToRadians(polygon[j][0]);
            var lon1 = ToRadians(polygon[j][1]);
            var lat2 = ToRadians(polygon[i][0]);
            var lon2 = ToRadians(polygon[i][1]);

            total += (lon2 - lon1) * (2 + Math.Sin(lat1) + Math.Sin(lat2));
        }

        return Math.Abs(total * EarthRadiusKm * EarthRadiusKm / 2);
    }

    /// <summary>Ray-casting containment test. <paramref name="polygon"/> points are [lat, lon].</summary>
    public static bool Contains(IReadOnlyList<double[]> polygon, double lat, double lon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (polygon[i].Length < 2 || polygon[j].Length < 2)
            {
                continue;
            }

            double latI = polygon[i][0], lonI = polygon[i][1];
            double latJ = polygon[j][0], lonJ = polygon[j][1];

            if (latI > lat != latJ > lat &&
                lon < ((lonJ - lonI) * (lat - latI) / (latJ - latI)) + lonI)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Plain-language band for an area, so a reviewer does not have to hold a sense of scale in their head.
    /// Advisory only: the exact figure is always shown next to it.
    /// </summary>
    public static string DescribeArea(double areaSqKm) => areaSqKm switch
    {
        < 1 => "a block or two",
        < 10 => "neighbourhood",
        < 50 => "district",
        < 200 => "city-sized",
        _ => "very large",
    };

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
