using System.Runtime.CompilerServices;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG's v2 schema bounds every numeric filter field. PoracleWeb.NET stores values outside several
/// of those bounds -- values PoracleNG itself wrote, through v1 and through its own bot -- so echoing a
/// stored row into a v2 write is a 422 with no fallback, on the edit path, in front of the user.
/// </summary>
/// <remarks>
/// <para>
/// The rule is narrow on purpose. A field may be omitted only where v2's own write default is the
/// identical value, so the row is stored exactly as it already is. Everywhere else the row goes to v1,
/// which stores what it is given. Omitting more widely would silently edit the filter: verified against a
/// live 5.2.1, omitting <c>size</c> stores -1 while sending 0 stores 0, and the server reports the two as
/// different rules.
/// </para>
/// <para>
/// Every case below came from scanning production against all 50 bounds, not from guessing which fields
/// looked like wildcards. That scan is what turned up <c>pvp_ranking_best = 0</c> on roughly sixteen
/// thousand rules and <c>pokemon_id = 0</c> on six hundred, neither of which is a wildcard and neither of
/// which a sentinel table would have caught.
/// </para>
/// </remarks>
public class TrackingV2BoundsTests
{
    /// <summary>
    /// A minimal row per type satisfying v2's required fields, so a refusal can only come from the field
    /// under test.
    /// </summary>
    private static readonly Dictionary<string, string> Base = new(StringComparer.Ordinal)
    {
        ["pokemon"] = @"{""pokemon_id"":25,""distance"":1000}",
        ["raid"] = @"{""level"":5,""team"":4,""distance"":1000}",
        ["egg"] = @"{""level"":5,""team"":4,""distance"":1000}",
        ["quest"] = @"{""reward_type"":2,""distance"":1000}",
        ["gym"] = @"{""team"":4,""distance"":1000}",
        ["maxbattle"] = @"{""pokemon_id"":150,""distance"":1000}",
        ["nest"] = @"{""pokemon_id"":25,""distance"":1000}",
        ["lure"] = @"{""lure_id"":501,""distance"":1000}",
        ["fort"] = @"{""fort_type"":""everything"",""include_empty"":false,""distance"":1000}",
    };

    /// <summary>
    /// type, field, the value production actually holds, and whether the row may still go to v2 with the
    /// field omitted.
    /// </summary>
    public static TheoryData<string, string, int, bool> ProductionRows() =>
        new()
        {
            { "pokemon", "size", -1, true },
            { "pokemon", "rarity", -1, true },
            { "pokemon", "min_iv", -1, true },
            { "nest", "pokemon_id", 0, true },
            { "pokemon", "size", 0, false },
            { "pokemon", "pvp_ranking_best", 0, false },
            { "pokemon", "pvp_ranking_worst", 0, false },
            { "pokemon", "pokemon_id", 0, false },
            { "raid", "pokemon_id", 0, false },
            { "raid", "level", 9000, false },
            { "maxbattle", "level", 9000, false },
        };

    [Theory]
    [MemberData(nameof(ProductionRows))]
    public void AStoredValueOutsideItsBoundIsNeverEchoed(string type, string field, int stored, bool omittable)
    {
        var translated = TrackingV2Translator.TryTranslate(type, With(type, field, stored), out var body, out var unsupported);

        if (omittable)
        {
            Assert.True(translated, $"{type}.{field}={stored} should still reach v2 with the field omitted: {unsupported}");
            Assert.False(body.TryGetProperty(field, out _), $"{type}.{field}={stored} was echoed rather than omitted");
        }
        else
        {
            Assert.False(translated, $"{type}.{field}={stored} must go to v1, because omitting it would change what is stored");
            Assert.NotNull(unsupported);
        }
    }

    /// <summary>
    /// The sweep the per-case theory cannot do: every bound in every table, rather than the handful
    /// anyone thought of. A bound declared on a field the integer writer never reaches would be dead, and
    /// this is what notices, because the out-of-range value would sail onto the wire.
    /// </summary>
    [Fact]
    public void NoBoundIsDeadAndNoOutOfRangeValueReachesTheWire()
    {
        var leaked = new List<string>();

        foreach (var (type, bounds) in TrackingV2Translator.BoundsByType)
        {
            foreach (var (field, bound) in bounds)
            {
                var outside = bound.Min is { } min ? min - 1 : bound.Max!.Value + 1;

                if (TrackingV2Translator.TryTranslate(type, With(type, field, outside), out var body, out _)
                    && body.TryGetProperty(field, out var written)
                    && written.ValueKind == JsonValueKind.Number)
                {
                    leaked.Add($"{type}.{field}={outside} (bound {bound})");
                }
            }
        }

        Assert.Empty(leaked);
    }

    /// <summary>The legitimate-case-still-passes half. A guard that refuses everything also passes a
    /// refusal test.</summary>
    [Fact]
    public void AValueInsideItsBoundStillReachesTheWire()
    {
        foreach (var (type, bounds) in TrackingV2Translator.BoundsByType)
        {
            foreach (var (field, bound) in bounds)
            {
                var inside = bound.Min ?? 0;

                Assert.True(
                    TrackingV2Translator.TryTranslate(type, With(type, field, inside), out var body, out var unsupported),
                    $"{type}.{field}={inside} is inside {bound} and must not be refused: {unsupported}");
                Assert.True(body.TryGetProperty(field, out _), $"{type}.{field}={inside} is legitimate and was dropped");
            }
        }
    }

    /// <summary>
    /// min_iv 0 is a floor, not a wildcard: PoracleNG renders it "iv: 0%-100%" against "iv: ?%-100%" for
    /// the -1 wildcard. Dropping it would widen the filter, and the two differ by one, which is exactly
    /// the pair a sentinel table gets wrong.
    /// </summary>
    [Fact]
    public void MinIvZeroIsAFloorAndSurvivesWhileMinusOneIsOmitted()
    {
        Assert.True(TrackingV2Translator.TryTranslate("pokemon", With("pokemon", "min_iv", 0), out var floor, out _));
        Assert.Equal(0, floor.GetProperty("min_iv").GetInt32());

        Assert.True(TrackingV2Translator.TryTranslate("pokemon", With("pokemon", "min_iv", -1), out var wildcard, out _));
        Assert.False(wildcard.TryGetProperty("min_iv", out _));
    }

    /// <summary>
    /// The bound tables are generated from PoracleNG's <c>openapi.golden.json</c>, and this is what keeps
    /// that generation load-bearing. When upstream moves a bound, the build fails here rather than a
    /// user's edit failing later.
    /// </summary>
    [Fact]
    public void BoundTablesMatchPoracleNgsPublishedSchema()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));

        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in fixture.RootElement.EnumerateObject())
        {
            foreach (var field in type.Value.EnumerateObject())
            {
                expected.Add(
                    $"{type.Name}.{field.Name}=[{Render(field.Value.GetProperty("min"))},{Render(field.Value.GetProperty("max"))}]");
            }
        }

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (type, bounds) in TrackingV2Translator.BoundsByType)
        {
            foreach (var (field, bound) in bounds)
            {
                actual.Add($"{type}.{field}=[{bound.Min?.ToString() ?? "null"},{bound.Max?.ToString() ?? "null"}]");
            }
        }

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Resolved from the test source rather than the output directory, so the fixture needs no build-file
    /// entry and is read straight out of the repository it is checked into.
    /// </summary>
    private static string FixturePath([CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "poracleng-v2-bounds.json");

    private static string Render(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? "null" : value.GetInt32().ToString();

    private static JsonElement With(string type, string field, int value)
    {
        using var row = JsonDocument.Parse(Base[type]);
        var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in row.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, field, StringComparison.Ordinal))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteNumber(field, value);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }
}
