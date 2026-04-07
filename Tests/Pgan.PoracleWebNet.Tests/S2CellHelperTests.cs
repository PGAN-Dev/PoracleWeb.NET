using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests;

public class S2CellHelperTests
{
    [Theory]
    [InlineData(40.7128, -74.0060)]  // New York City
    [InlineData(35.6762, 139.6503)]  // Tokyo
    [InlineData(-33.8688, 151.2093)] // Sydney
    [InlineData(0.0, 0.0)]          // Null Island
    public void LatLonToCellIdReturnsValidCellStructure(double lat, double lon)
    {
        var cellId = S2CellHelper.LatLonToCellId(lat, lon, 10);

        // Face should be in [0, 5].
        var face = (int)((ulong)cellId >> 61);
        Assert.InRange(face, 0, 5);

        // For level 10, sentinel bit is at position 2 * (30 - 10) = 40.
        var sentinelBit = (cellId >> 40) & 1;
        Assert.Equal(1L, sentinelBit);

        // All bits below the sentinel should be zero.
        var belowSentinel = cellId & ((1L << 40) - 1);
        Assert.Equal(0L, belowSentinel);
    }

    [Theory]
    [InlineData(40.7128, -74.0060)]  // New York City
    [InlineData(35.6762, 139.6503)]  // Tokyo
    [InlineData(-33.8688, 151.2093)] // Sydney
    public void LatLonToWeatherCellIdEqualsLatLonToCellIdLevel10(double lat, double lon)
    {
        var weatherCellId = S2CellHelper.LatLonToWeatherCellId(lat, lon);
        var level10CellId = S2CellHelper.LatLonToCellId(lat, lon, 10);

        Assert.Equal(level10CellId, weatherCellId);
    }

    [Fact]
    public void NorthPoleReturnsValidCell()
    {
        var cellId = S2CellHelper.LatLonToCellId(90.0, 0.0, 10);

        var face = (int)((ulong)cellId >> 61);
        Assert.InRange(face, 0, 5);

        var sentinelBit = (cellId >> 40) & 1;
        Assert.Equal(1L, sentinelBit);
    }

    [Fact]
    public void SouthPoleReturnsValidCell()
    {
        var cellId = S2CellHelper.LatLonToCellId(-90.0, 0.0, 10);

        var face = (int)((ulong)cellId >> 61);
        Assert.InRange(face, 0, 5);

        var sentinelBit = (cellId >> 40) & 1;
        Assert.Equal(1L, sentinelBit);
    }

    [Theory]
    [InlineData(0.0, 180.0)]
    [InlineData(0.0, -180.0)]
    public void AntiMeridianReturnsValidCell(double lat, double lon)
    {
        var cellId = S2CellHelper.LatLonToCellId(lat, lon, 10);

        var face = (int)((ulong)cellId >> 61);
        Assert.InRange(face, 0, 5);

        var sentinelBit = (cellId >> 40) & 1;
        Assert.Equal(1L, sentinelBit);

        var belowSentinel = cellId & ((1L << 40) - 1);
        Assert.Equal(0L, belowSentinel);
    }

    [Fact]
    public void NearbyCoordinatesReturnSameCellId()
    {
        // Two points very close together in NYC (within ~10m) should share the same level-10 cell.
        var cellId1 = S2CellHelper.LatLonToWeatherCellId(40.7128, -74.0060);
        var cellId2 = S2CellHelper.LatLonToWeatherCellId(40.7129, -74.0061);

        Assert.Equal(cellId1, cellId2);
    }

    [Fact]
    public void DistantCoordinatesReturnDifferentCellIds()
    {
        // NYC and Tokyo should be in different cells.
        var nyc = S2CellHelper.LatLonToWeatherCellId(40.7128, -74.0060);
        var tokyo = S2CellHelper.LatLonToWeatherCellId(35.6762, 139.6503);

        Assert.NotEqual(nyc, tokyo);
    }

    [Fact]
    public void NullIslandReturnsValidCell()
    {
        var cellId = S2CellHelper.LatLonToCellId(0.0, 0.0, 10);

        // Cell ID must be non-zero and structurally valid.
        Assert.NotEqual(0L, cellId);

        var face = (int)((ulong)cellId >> 61);
        Assert.InRange(face, 0, 5);

        var sentinelBit = (cellId >> 40) & 1;
        Assert.Equal(1L, sentinelBit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void DifferentLevelsProduceSentinelAtCorrectPosition(int level)
    {
        var cellId = S2CellHelper.LatLonToCellId(40.7128, -74.0060, level);

        var sentinelShift = 2 * (30 - level);
        var sentinelBit = (cellId >> sentinelShift) & 1;
        Assert.Equal(1L, sentinelBit);

        // All bits below the sentinel should be zero.
        if (sentinelShift > 0)
        {
            var belowSentinel = cellId & ((1L << sentinelShift) - 1);
            Assert.Equal(0L, belowSentinel);
        }
    }

    [Fact]
    public void CellIdIsNonZero()
    {
        // Every valid cell ID should be non-zero (at minimum the face and sentinel bits are set).
        var cellId = S2CellHelper.LatLonToWeatherCellId(0.0, 0.0);

        Assert.NotEqual(0L, cellId);
    }

    [Fact]
    public void SameInputAlwaysReturnsSameOutput()
    {
        var first = S2CellHelper.LatLonToWeatherCellId(40.7128, -74.0060);
        var second = S2CellHelper.LatLonToWeatherCellId(40.7128, -74.0060);

        Assert.Equal(first, second);
    }
}
