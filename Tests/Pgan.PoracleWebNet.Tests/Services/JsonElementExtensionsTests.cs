using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models.Helpers;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class JsonElementExtensionsTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void AStringAreaIsReturnedAsIs()
    {
        var el = Parse("""{"area":"[\"a\"]"}""");
        Assert.Equal("[\"a\"]", el.GetAreaListJsonOrNull("area"));
    }

    [Fact]
    public void AnArrayAreaIsSerialised()
    {
        var el = Parse("""{"area":["a","b"]}""");
        Assert.Equal("[\"a\",\"b\"]", el.GetAreaListJsonOrNull("area"));
    }

    [Fact]
    public void AnEmptyArrayIsAnEmptyList()
    {
        var el = Parse("""{"area":[]}""");
        Assert.Equal("[]", el.GetAreaListJsonOrNull("area"));
    }

    [Fact]
    public void NullOrAbsentIsNull()
    {
        Assert.Null(Parse("""{"area":null}""").GetAreaListJsonOrNull("area"));
        Assert.Null(Parse("""{}""").GetAreaListJsonOrNull("area"));
    }

    [Fact]
    public void KeepsCaseAndOrder()
    {
        var el = Parse("""{"area":["West End","downtown"]}""");
        Assert.Equal("[\"West End\",\"downtown\"]", el.GetAreaListJsonOrNull("area"));
    }

    [Fact]
    public void MatchesWhatAreaListJsonWritesForNonAsciiNames()
    {
        // AreaListJson.Serialize uses the default encoder, which escapes non-ASCII ("Café"). The array
        // shape must produce the same text, so a human read from either server shape is identical, and
        // it parses back to the real name.
        var el = Parse("""{"area":["Café"]}""");
        var result = el.GetAreaListJsonOrNull("area");

        Assert.Equal(AreaListJson.Serialize(["Café"]), result);
        Assert.Equal(["Café"], AreaListJson.Parse(result));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[1]")]
    public void AWrongKindThrows(string value)
    {
        var el = Parse("{\"area\":" + value + "}");
        var ex = Assert.Throws<InvalidOperationException>(() => el.GetAreaListJsonOrNull("area"));
        Assert.Contains("area", ex.Message, StringComparison.Ordinal);
    }
}
