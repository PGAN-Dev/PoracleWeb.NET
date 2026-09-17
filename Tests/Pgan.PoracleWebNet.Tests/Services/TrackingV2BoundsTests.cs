using System.Runtime.CompilerServices;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG's v2 schema bounds every numeric filter field on an unreleased branch, and PoracleWeb.NET
/// stores values outside ten of those bounds -- values PoracleNG itself wrote, through v1 and through its
/// own bot. Echoing such a row into a v2 write on a server that enforces the bound is a 422 with no
/// fallback, on the edit path, in front of the user.
/// </summary>
/// <remarks>
/// <para>
/// The limits come from the server's own <c>/openapi.json</c>, not from a table shipped here, so the
/// guard applies exactly where it is enforced. That distinction is the whole design: measured against a
/// live 5.2.1, every one of the ten values is accepted today, so shipping the limits would have dropped
/// 68% of one instance's Pokemon rules off the v2 write path to guard against a server nobody runs.
/// <see cref="NothingIsRefusedByAServerThatDeclaresNoBounds"/> is the test for that.
/// </para>
/// <para>
/// Where a bound does apply, a field is omitted only if v2's own write default is the identical value,
/// so the row stores what it already holds. Everywhere else the row goes to v1. Verified against a live
/// 5.2.1: omitting <c>size</c> stores -1 while sending 0 stores 0, and the server reports the two as
/// different rules, so <c>size 0</c> is refused rather than dropped.
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
    /// field omitted once a server bounds it.
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
            // level is refused rather than omitted for a different reason from the rest: the by-level
            // write default is 90, which matches every tier, so dropping it would turn a rule for one
            // boss into a rule for every raid in range. PoracleNG writes the 9000 itself whenever
            // pokemon_id names a specific boss.
            { "raid", "level", 9000, false },
            { "maxbattle", "level", 9000, false },
        };

    /// <summary>
    /// The test this design exists to satisfy, and the one whose absence made the first attempt a
    /// regression. Every value in <see cref="ProductionRows"/> is accepted by the 5.2.1 these instances
    /// run, which declares one bound in total. Against such a server nothing may be refused and nothing
    /// may be omitted: the request goes out exactly as it does today.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProductionRows))]
    public void NothingIsRefusedByAServerThatDeclaresNoBounds(string type, string field, int stored, bool omittable)
    {
        _ = omittable;

        Assert.True(
            TrackingV2Translator.TryTranslate(type, With(type, field, stored), null, out var body, out var unsupported),
            $"{type}.{field}={stored} must still reach v2 on a server that does not bound it: {unsupported}");

        Assert.Equal(stored, body.GetProperty(field).GetInt32());
    }

    [Theory]
    [MemberData(nameof(ProductionRows))]
    public void AStoredValueOutsideItsBoundIsNeverEchoed(string type, string field, int stored, bool omittable)
    {
        var translated = TrackingV2Translator.TryTranslate(
            type, With(type, field, stored), Bounds(type), out var body, out var unsupported);

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
    /// The sweep the per-case theory cannot do: every bound the server declares, rather than the handful
    /// anyone thought of. A bound on a field the integer writer never reaches would be dead, and this is
    /// what notices, because the out-of-range value would sail onto the wire.
    /// </summary>
    [Fact]
    public void NoBoundIsDeadAndNoOutOfRangeValueReachesTheWire()
    {
        var leaked = new List<string>();

        foreach (var (type, bounds) in Fixture())
        {
            foreach (var (field, bound) in bounds)
            {
                var outside = bound.Min is { } min ? min - 1 : bound.Max!.Value + 1;

                if (TrackingV2Translator.TryTranslate(type, With(type, field, outside), bounds, out var body, out _)
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
        foreach (var (type, bounds) in Fixture())
        {
            foreach (var (field, bound) in bounds)
            {
                var inside = bound.Min ?? 0;

                Assert.True(
                    TrackingV2Translator.TryTranslate(type, With(type, field, inside), bounds, out var body, out var unsupported),
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
        var bounds = Bounds("pokemon");

        Assert.True(TrackingV2Translator.TryTranslate("pokemon", With("pokemon", "min_iv", 0), bounds, out var floor, out _));
        Assert.Equal(0, floor.GetProperty("min_iv").GetInt32());

        Assert.True(TrackingV2Translator.TryTranslate("pokemon", With("pokemon", "min_iv", -1), bounds, out var wildcard, out _));
        Assert.False(wildcard.TryGetProperty("min_iv", out _));
    }

    // ──────────────────────────────────────────────────────────────
    // Reading the limits off the server
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParseReadsTheLimitsARuleSchemaDeclares()
    {
        var bounds = V2SchemaBounds.Parse(
            @"{""components"":{""schemas"":{
                 ""V2PokemonRule"":{""properties"":{
                   ""min_iv"":{""minimum"":0,""maximum"":100},
                   ""pokemon_id"":{""minimum"":1},
                   ""template"":{""type"":""string""}}}}}}");

        Assert.Equal(new TrackingV2Translator.Bound(0, 100), bounds["pokemon"]["min_iv"]);
        Assert.Equal(new TrackingV2Translator.Bound(1, null), bounds["pokemon"]["pokemon_id"]);
        Assert.False(bounds["pokemon"].ContainsKey("template"));
    }

    /// <summary>
    /// The envelopes wrapping each rule carry the same properties, so matching them too would work by
    /// accident and then break the day one of them diverges.
    /// </summary>
    [Fact]
    public void ParseIgnoresTheResponseEnvelopesThatWrapARule()
    {
        var bounds = V2SchemaBounds.Parse(
            @"{""components"":{""schemas"":{
                 ""V2CreateOutputV2PokemonRuleBody"":{""properties"":{""min_iv"":{""minimum"":0}}},
                 ""Poracle2GruntPokemon"":{""properties"":{""id"":{""minimum"":1}}}}}}");

        Assert.Empty(bounds);
    }

    /// <summary>
    /// A server we cannot ask has to mean no limits, never all of them. The other way round, one failed
    /// HTTP call would refuse every filter on the site.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData(@"{""components"":{}}")]
    [InlineData(@"{""openapi"":""3.1.0""}")]
    public void ParseYieldsNoLimitsRatherThanFailing(string? document)
    {
        Assert.Empty(V2SchemaBounds.Parse(document));
    }

    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The limits the unreleased PoracleNG branch declares, kept as a file so these tests exercise a real
    /// schema rather than one written to match the code. Test data, not a shipped table: at runtime the
    /// instance being written to is the authority.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>> Fixture(
        [CallerFilePath] string? here = null)
    {
        var path = Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "poracleng-v2-bounds.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var byType = new Dictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>>(StringComparer.Ordinal);

        foreach (var type in document.RootElement.EnumerateObject())
        {
            var fields = new Dictionary<string, TrackingV2Translator.Bound>(StringComparer.Ordinal);

            foreach (var field in type.Value.EnumerateObject())
            {
                fields[field.Name] = new TrackingV2Translator.Bound(
                    AsNullableInt(field.Value.GetProperty("min")),
                    AsNullableInt(field.Value.GetProperty("max")));
            }

            byType[type.Name] = fields;
        }

        return byType;
    }

    private static IReadOnlyDictionary<string, TrackingV2Translator.Bound> Bounds(string type) => Fixture()[type];

    private static int? AsNullableInt(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();

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
