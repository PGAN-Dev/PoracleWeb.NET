using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Tests.Helpers;

public class AreaListJsonTests
{
    // --- Parse ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseReturnsEmptyForNullOrWhitespace(string? input)
    {
        var result = AreaListJson.Parse(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseReadsJsonArray()
    {
        var result = AreaListJson.Parse("[\"downtown\",\"west end\"]");
        Assert.Equal(2, result.Count);
        Assert.Equal("downtown", result[0]);
        Assert.Equal("west end", result[1]);
    }

    [Fact]
    public void ParseReadsEmptyJsonArray()
    {
        var result = AreaListJson.Parse("[]");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseFallsBackToCommaSeparatedForLegacyRows()
    {
        // Older PoracleWeb rows stored areas as CSV rather than JSON. Must still parse.
        var result = AreaListJson.Parse("downtown,west end,uptown");
        Assert.Equal(3, result.Count);
        Assert.Contains("downtown", result);
        Assert.Contains("west end", result);
        Assert.Contains("uptown", result);
    }

    [Fact]
    public void ParseTrimsWhitespaceInCsvFallback()
    {
        var result = AreaListJson.Parse("downtown, west end ,uptown");
        Assert.Equal(3, result.Count);
        Assert.Contains("west end", result);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ "[1, 2, 3]")]
    [InlineData(/*lang=json,strict*/ "[{\"nested\":\"object\"}]")]
    [InlineData(/*lang=json,strict*/ "{\"not\":\"an_array\"}")]
    public void ParseReturnsEmptyForMalformedJsonInsteadOfCsvGarbage(string input)
    {
        // Bracketed input that fails JSON deserialization must not fall through to the CSV
        // split — otherwise "[1, 2, 3]" becomes ["[1", " 2", " 3]"] which is garbage masquerading
        // as a valid area list. Empty is the only safe answer.
        var result = AreaListJson.Parse(input);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ "\"foo\"")]
    [InlineData(/*lang=json,strict*/ "\"some,csv,looking,thing\"")]
    public void ParseReturnsEmptyForJsonStringLiteralInsteadOfQuotedGarbage(string input)
    {
        // String literals are valid JSON but not valid List<string>. The pre-fix CSV fallback
        // would turn "\"foo\"" into ["\"foo\""] (one entry with literal quotes) — empty is
        // the right answer for any JSON-shaped input that fails the List<string> shape check.
        var result = AreaListJson.Parse(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseStillFallsBackToCsvForNonBracketedInput()
    {
        // Guard against the previous fix being too aggressive — unbracketed input that can't
        // parse as JSON is still assumed to be CSV (the legacy PoracleWeb format).
        var result = AreaListJson.Parse("downtown,west end");
        Assert.Equal(2, result.Count);
    }

    // --- Serialize ---

    [Fact]
    public void SerializeEmitsJsonArray()
    {
        var result = AreaListJson.Serialize(["downtown", "west end"]);
        Assert.Equal("[\"downtown\",\"west end\"]", result);
    }

    [Fact]
    public void SerializeEmptyListEmitsBracketPair()
    {
        // The column is NOT NULL and downstream code (PoracleJS, the PoracleNG handler) expects
        // a valid JSON array — never an empty string. Guarded by the helper so every caller is safe.
        var result = AreaListJson.Serialize([]);
        Assert.Equal("[]", result);
    }

    [Fact]
    public void SerializeRoundTripsWithParse()
    {
        var original = new List<string> { "alpha", "beta", "gamma" };
        var json = AreaListJson.Serialize(original);
        var roundTripped = AreaListJson.Parse(json);
        Assert.Equal(original, roundTripped);
    }
}
