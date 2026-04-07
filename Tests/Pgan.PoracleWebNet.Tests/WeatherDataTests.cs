using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests;

public class WeatherDataTests
{
    [Theory]
    [InlineData(1, "Clear", "wb_sunny", new[] { "Fire", "Grass", "Ground" })]
    [InlineData(2, "Rainy", "water_drop", new[] { "Water", "Electric", "Bug" })]
    [InlineData(3, "Partly Cloudy", "filter_drama", new[] { "Normal", "Rock" })]
    [InlineData(4, "Cloudy", "cloud", new[] { "Fairy", "Fighting", "Poison" })]
    [InlineData(5, "Windy", "air", new[] { "Dragon", "Flying", "Psychic" })]
    [InlineData(6, "Snow", "ac_unit", new[] { "Ice", "Steel" })]
    [InlineData(7, "Fog", "foggy", new[] { "Dark", "Ghost" })]
    public void FromConditionReturnsCorrectNameIconAndTypes(
        int condition, string expectedName, string expectedIcon, string[] expectedTypes)
    {
        var result = WeatherData.FromCondition(condition);

        Assert.Equal(condition, result.Condition);
        Assert.Equal(expectedName, result.ConditionName);
        Assert.Equal(expectedIcon, result.Icon);
        Assert.Equal(expectedTypes, result.BoostedTypes);
    }

    [Fact]
    public void FromConditionZeroReturnsUnknown()
    {
        var result = WeatherData.FromCondition(0);

        Assert.Equal(0, result.Condition);
        Assert.Equal("Unknown", result.ConditionName);
        Assert.Equal("help_outline", result.Icon);
        Assert.Empty(result.BoostedTypes);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(99)]
    [InlineData(-1)]
    public void FromConditionOutOfRangeReturnsUnknown(int condition)
    {
        var result = WeatherData.FromCondition(condition);

        Assert.Equal(condition, result.Condition);
        Assert.Equal("Unknown", result.ConditionName);
        Assert.Equal("help_outline", result.Icon);
        Assert.Empty(result.BoostedTypes);
    }

    [Fact]
    public void FromConditionWithSeverityGreaterThanZeroSetsHasWarning()
    {
        var result = WeatherData.FromCondition(1, severity: 2);

        Assert.True(result.HasWarning);
        Assert.Equal(2, result.Severity);
    }

    [Fact]
    public void FromConditionWithWarnWeatherTrueSetsHasWarning()
    {
        var result = WeatherData.FromCondition(1, warnWeather: true);

        Assert.True(result.HasWarning);
    }

    [Fact]
    public void FromConditionWithNoWarningFlagsSetsHasWarningFalse()
    {
        var result = WeatherData.FromCondition(1, severity: 0, warnWeather: false);

        Assert.False(result.HasWarning);
        Assert.Equal(0, result.Severity);
    }

    [Fact]
    public void FromConditionWithSeverityAndWarnWeatherBothSetHasWarning()
    {
        var result = WeatherData.FromCondition(3, severity: 1, warnWeather: true);

        Assert.True(result.HasWarning);
        Assert.Equal(1, result.Severity);
    }

    [Fact]
    public void FromConditionWithUnixTimestampConvertsUpdatedAt()
    {
        // 2024-01-15 12:00:00 UTC = 1705320000
        long unixTs = 1705320000;
        var expected = DateTimeOffset.FromUnixTimeSeconds(unixTs);

        var result = WeatherData.FromCondition(1, updatedUnix: unixTs);

        Assert.NotNull(result.UpdatedAt);
        Assert.Equal(expected, result.UpdatedAt!.Value);
    }

    [Fact]
    public void FromConditionWithZeroTimestampReturnsEpoch()
    {
        var result = WeatherData.FromCondition(1, updatedUnix: 0);

        Assert.NotNull(result.UpdatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.UpdatedAt!.Value);
    }

    [Fact]
    public void FromConditionWithNullTimestampReturnsNullUpdatedAt()
    {
        var result = WeatherData.FromCondition(1, updatedUnix: null);

        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public void FromConditionDefaultParametersHaveNoWarningAndNullTimestamp()
    {
        var result = WeatherData.FromCondition(2);

        Assert.False(result.HasWarning);
        Assert.Equal(0, result.Severity);
        Assert.Null(result.UpdatedAt);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 2)]
    [InlineData(7, 2)]
    public void FromConditionReturnsCorrectBoostedTypeCount(int condition, int expectedCount)
    {
        var result = WeatherData.FromCondition(condition);

        Assert.Equal(expectedCount, result.BoostedTypes.Count);
    }
}
