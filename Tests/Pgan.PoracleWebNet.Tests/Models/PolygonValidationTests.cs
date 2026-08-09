using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// Create validated only the point count, so <c>[[1],[2],[3]]</c> and coordinates off the globe were
/// stored verbatim, served by the anonymous geofence feed that PoracleJS reads, and crashed the owner's
/// GeoJSON export. Import validated both. One rule now, shared by every path. See #410.
/// </summary>
public class PolygonValidationTests
{
    private static double[][] Square(double lat = 40, double lon = -75) =>
        [[lat, lon], [lat + 0.01, lon], [lat + 0.01, lon + 0.01], [lat, lon + 0.01]];

    [Fact]
    public void AcceptsAnOrdinarySquare()
    {
        Assert.True(PolygonValidation.TryValidate(Square(), out var error));
        Assert.Empty(error);
    }

    [Fact]
    public void RejectsPointsThatAreNotPairs()
    {
        // The reported payload.
        Assert.False(PolygonValidation.TryValidate([[1.0], [2.0], [3.0]], out var error));
        Assert.Contains("[latitude, longitude] pair", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPointsWithTooManyValues()
    {
        Assert.False(PolygonValidation.TryValidate([[1, 2, 3], [4, 5, 6], [7, 8, 9]], out _));
    }

    [Fact]
    public void NamesTheOffendingPoint()
    {
        // A 300-point polygon with one bad vertex is unusable feedback without the index.
        var polygon = Square().ToList();
        polygon.Insert(2, [1.0]);

        PolygonValidation.TryValidate([.. polygon], out var error);

        Assert.Contains("point 2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOutOfRangeCoordinates()
    {
        Assert.False(PolygonValidation.TryValidate([[999, -999], [998, -998], [997, -997]], out var error));
        Assert.Contains("out of valid range", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void RejectsCoordinatesJustOutsideTheBounds(double lat, double lon)
    {
        Assert.False(PolygonValidation.TryValidate([[lat, lon], [1, 1], [2, 2]], out _));
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void AcceptsCoordinatesExactlyOnTheBounds(double lat, double lon)
    {
        Assert.True(PolygonValidation.TryValidate([[lat, lon], [1, 1], [2, 2]], out _));
    }

    [Fact]
    public void RejectsNonFiniteCoordinates()
    {
        // JSON cannot carry these, but a computed polygon can, and they serialize as null and poison the feed.
        Assert.False(PolygonValidation.TryValidate([[double.NaN, 0], [1, 1], [2, 2]], out _));
        Assert.False(PolygonValidation.TryValidate([[double.PositiveInfinity, 0], [1, 1], [2, 2]], out _));
    }

    [Fact]
    public void RejectsFewerThanThreePoints()
    {
        Assert.False(PolygonValidation.TryValidate([[1, 1], [2, 2]], out var error));
        Assert.Contains("at least 3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMoreThanFiveHundredPoints()
    {
        var polygon = Enumerable.Range(0, 501).Select(i => new double[] { 40 + (i * 0.0001), -75 }).ToArray();

        Assert.False(PolygonValidation.TryValidate(polygon, out var error));
        Assert.Contains("exceed 500", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsExactlyFiveHundredPoints()
    {
        var polygon = Enumerable.Range(0, 500).Select(i => new double[] { 40 + (i * 0.0001), -75 }).ToArray();

        Assert.True(PolygonValidation.TryValidate(polygon, out _));
    }

    [Fact]
    public void RejectsNullPolygonAndNullPoints()
    {
        Assert.False(PolygonValidation.TryValidate(null, out _));
        Assert.False(PolygonValidation.TryValidate([null!, [1, 1], [2, 2]], out _));
    }

    [Fact]
    public void IsWellFormedAgreesWithTryValidate()
    {
        Assert.True(PolygonValidation.IsWellFormed(Square()));
        Assert.False(PolygonValidation.IsWellFormed([[1.0], [2.0], [3.0]]));
        Assert.False(PolygonValidation.IsWellFormed(null));
    }
}
