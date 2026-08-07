using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// <see cref="GeoMath"/> is a hand-port of the frontend's <c>geo.utils.ts</c>. If the two drift, the area a
/// user sees while drawing stops matching the one a reviewer sees in Discord, so the expected values here
/// are derived independently rather than from the implementation.
/// </summary>
public class GeoMathTests
{
    /// <summary>
    /// On a sphere of radius 6371 km one degree is 2πR/360 = 111.1949 km in both axes at the equator, so a
    /// 0.1° square there is (11.11949)² ≈ 123.64 km². Derived from the radius rather than read off the
    /// implementation.
    /// </summary>
    private const double DegreeKm = 2 * Math.PI * 6371 / 360;

    [Fact]
    public void AreaMatchesTheSphericalExpectationAtTheEquator()
    {
        double[][] square = [[0, 0], [0, 0.1], [0.1, 0.1], [0.1, 0]];

        var area = GeoMath.AreaSqKm(square);

        var expected = 0.1 * DegreeKm * (0.1 * DegreeKm); // ≈ 123.64 km²
        Assert.InRange(area, expected * 0.999, expected * 1.001);
    }

    /// <summary>The same square at latitude 60 must shrink by cos(latitude) as meridians converge.</summary>
    [Fact]
    public void AreaShrinksWithLatitude()
    {
        double[][] square = [[60, 0], [60, 0.1], [60.1, 0.1], [60.1, 0]];

        var area = GeoMath.AreaSqKm(square);

        var expected = 0.1 * DegreeKm * (0.1 * DegreeKm) * Math.Cos(60.05 * Math.PI / 180);
        Assert.InRange(area, expected * 0.999, expected * 1.001);
    }

    [Fact]
    public void AreaIgnoresWindingDirection()
    {
        double[][] clockwise = [[0, 0], [0, 0.1], [0.1, 0.1], [0.1, 0]];
        double[][] counterClockwise = [[0.1, 0], [0.1, 0.1], [0, 0.1], [0, 0]];

        Assert.Equal(GeoMath.AreaSqKm(clockwise), GeoMath.AreaSqKm(counterClockwise), 6);
    }

    [Fact]
    public void AreaIsZeroForDegeneratePolygons()
    {
        Assert.Equal(0, GeoMath.AreaSqKm([]));
        Assert.Equal(0, GeoMath.AreaSqKm([[1, 2]]));
        Assert.Equal(0, GeoMath.AreaSqKm([[1, 2], [3, 4]]));
    }

    [Fact]
    public void CentroidAveragesTheVertices()
    {
        double[][] square = [[0, 0], [0, 2], [2, 2], [2, 0]];

        var (lat, lon) = GeoMath.Centroid(square);

        Assert.Equal(1, lat, 6);
        Assert.Equal(1, lon, 6);
    }

    [Fact]
    public void CentroidOfAnEmptyPolygonIsOrigin()
    {
        var (lat, lon) = GeoMath.Centroid([]);

        Assert.Equal(0, lat);
        Assert.Equal(0, lon);
    }

    [Theory]
    [InlineData(1, 1, true)]      // interior
    [InlineData(5, 5, false)]     // outside
    [InlineData(-1, 1, false)]    // south of it
    [InlineData(1, -1, false)]    // west of it
    public void ContainsTestsPointsAgainstThePolygon(double lat, double lon, bool expected)
    {
        double[][] square = [[0, 0], [0, 2], [2, 2], [2, 0]];

        Assert.Equal(expected, GeoMath.Contains(square, lat, lon));
    }

    [Fact]
    public void ContainsHandlesAConcavePolygon()
    {
        // A "C" shape open to the east: the notch between the arms is outside the polygon.
        double[][] cShape = [[0, 0], [0, 3], [1, 3], [1, 1], [2, 1], [2, 3], [3, 3], [3, 0]];

        Assert.True(GeoMath.Contains(cShape, 0.5, 1.5));   // inside the lower arm
        Assert.False(GeoMath.Contains(cShape, 1.5, 2));    // inside the notch
        Assert.True(GeoMath.Contains(cShape, 1.5, 0.5));   // inside the spine
    }

    [Fact]
    public void ContainsIsFalseForDegeneratePolygons() =>
        Assert.False(GeoMath.Contains([[0, 0], [1, 1]], 0.5, 0.5));

    [Theory]
    [InlineData(0, "a block or two")]
    [InlineData(0.99, "a block or two")]
    [InlineData(1, "neighbourhood")]
    [InlineData(9.99, "neighbourhood")]
    [InlineData(10, "district")]
    [InlineData(49.9, "district")]
    [InlineData(50, "city-sized")]
    [InlineData(199, "city-sized")]
    [InlineData(200, "very large")]
    [InlineData(10000, "very large")]
    public void DescribeAreaBandsOnTheDocumentedBoundaries(double areaSqKm, string expected) =>
        Assert.Equal(expected, GeoMath.DescribeArea(areaSqKm));
}
