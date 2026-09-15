using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// The hidden-area list is read inside the geofence feed, which is the single geofence source for
/// Poracle and whose last good response PoracleJS caches. So the direction it fails in matters more
/// than the parsing does: a value it cannot read must hide nothing. See #885.
/// </summary>
public class HiddenAreasTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"not\":\"an array\"}")]
    [InlineData("[1, 2, 3]")]
    public void ParseHidesNothingWhenTheValueCannotBeRead(string? raw)
    {
        Assert.Empty(HiddenAreas.Parse(raw));
    }

    [Fact]
    public void ParseReadsTheNames()
    {
        var hidden = HiddenAreas.Parse("[\"staging\",\"test fence\"]");

        Assert.Equal(2, hidden.Count);
        Assert.Contains("staging", hidden);
        Assert.Contains("test fence", hidden);
    }

    /// <summary>
    /// Poracle matches area names case-sensitively and stores them lowercased. A name that arrives
    /// capitalised must still hide the fence, or the toggle silently does nothing.
    /// </summary>
    [Fact]
    public void ParseMatchesRegardlessOfCase()
    {
        var hidden = HiddenAreas.Parse("[\"Staging\"]");

        Assert.Contains("staging", hidden);
        Assert.Contains("STAGING", hidden);
    }

    [Fact]
    public void ParseSkipsEntriesItCannotUseAndKeepsTheRest()
    {
        var hidden = HiddenAreas.Parse("[\"good\", \"\", \"   \", 7, null, \"also good\"]");

        Assert.Equal(2, hidden.Count);
        Assert.Contains("good", hidden);
        Assert.Contains("also good", hidden);
    }

    [Fact]
    public void ParseStopsAtTheCap()
    {
        var many = Enumerable.Range(0, HiddenAreas.MaxEntries + 50).Select(i => $"\"area{i}\"");

        Assert.Equal(HiddenAreas.MaxEntries, HiddenAreas.Parse($"[{string.Join(",", many)}]").Count);
    }

    [Fact]
    public void SerializeNormalizesDeduplicatesAndOrders()
    {
        var json = HiddenAreas.Serialize(["  Zulu  ", "alpha", "ALPHA", "", "  "]);

        Assert.Equal("[\"alpha\",\"zulu\"]", json);
    }

    [Fact]
    public void SerializeAndParseRoundTrip()
    {
        var names = new[] { "staging", "test fence", "old region" };

        Assert.Equal(
            names.OrderBy(n => n, StringComparer.Ordinal),
            HiddenAreas.Parse(HiddenAreas.Serialize(names)).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// An empty list is how an operator clears the setting, so it has to stay writable. Refusing it
    /// would make hiding one-way.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("[\"staging\"]")]
    public void ValidateAcceptsWhatTheAdminPageWrites(string? value)
    {
        Assert.True(HiddenAreas.TryValidate(value, out var error));
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    [InlineData("[\"   \"]")]
    public void ValidateRefusesWhatCouldNotBeRenderedOrApplied(string value)
    {
        Assert.False(HiddenAreas.TryValidate(value, out var error));
        Assert.NotEqual(string.Empty, error);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void ValidateBoundsTheListLength(int count, bool accepted)
    {
        var entries = Enumerable.Range(0, count).Select(i => $"\"area{i}\"");

        Assert.Equal(accepted, HiddenAreas.TryValidate($"[{string.Join(",", entries)}]", out _));
    }

    [Fact]
    public void ValidateRefusesAnOverlongName()
    {
        var name = new string('a', 201);

        Assert.False(HiddenAreas.TryValidate($"[\"{name}\"]", out _));
    }
}
