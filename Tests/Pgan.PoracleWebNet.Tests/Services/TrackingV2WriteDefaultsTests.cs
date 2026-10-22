using System.Text.Json;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The write direction of <see cref="TrackingV2Translator"/>: a value equal to the one a v2 read reports as
/// null goes out as absent, because that is what v2 stores an absent property as, and v2 bounds some
/// sentinels (<c>level</c> maxes at 90) so they cannot be echoed back.
/// </summary>
public class TrackingV2WriteDefaultsTests
{
    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void ANamedBossOmitsLevel9000AndStaysInsideTheSchema(string type)
    {
        var body = Translate(type, """{"pokemon_id":150,"level":9000,"distance":0}""");

        Assert.False(body.TryGetProperty("level", out _));
        Assert.Equal(150, body.GetProperty("pokemon_id").GetInt32());
        AssertValidAgainstSchema(type, body);
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void AByLevelRuleKeepsItsTier(string type)
    {
        var body = Translate(type, """{"pokemon_id":9000,"level":5}""");

        Assert.Equal(5, body.GetProperty("level").GetInt32());
        Assert.False(body.TryGetProperty("pokemon_id", out _));
        AssertValidAgainstSchema(type, body);
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void AByLevelRuleForAnyTierOmitsLevel90(string type)
    {
        Assert.False(Translate(type, """{"pokemon_id":9000,"level":90}""").TryGetProperty("level", out _));
        Assert.False(Translate(type, """{"level":90}""").TryGetProperty("level", out _));
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void AnyBossAtLevel9000OmitsLevel(string type)
    {
        // Stored as 90 by v2, which is the any-tier wildcard.
        Assert.False(Translate(type, """{"pokemon_id":9000,"level":9000}""").TryGetProperty("level", out _));
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void Level9000WithNoPokemonIdOmitsLevelAndStaysInsideTheSchema(string type)
    {
        // No pokemon named and no tier: v2 bounds level at 90, so echoing 9000 would be a 422.
        var body = Translate(type, """{"level":9000,"distance":0}""");

        Assert.False(body.TryGetProperty("level", out _));
        Assert.False(body.TryGetProperty("pokemon_id", out _));
        AssertValidAgainstSchema(type, body);
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void ANamedBossAtLevel90StillSendsIt(string type)
    {
        Assert.Equal(90, Translate(type, """{"pokemon_id":150,"level":90}""").GetProperty("level").GetInt32());
    }

    [Fact]
    public void APokemonAtDefaultsSendsOnlyThePokemonId()
    {
        var row = PoracleJsonHelper.SerializeToElement(new MonsterCreate().ToMonster());
        var body = Translate("pokemon", row.GetRawText());

        var defaults = TrackingV2Translator.DefaultsFor("pokemon");
        var leaked = body.EnumerateObject()
            .Where(p => p.Name != "pokemon_id" && defaults.TryGetValue(p.Name, out var d) && d.GetRawText() == p.Value.GetRawText())
            .Select(p => p.Name)
            .ToList();

        Assert.True(leaked.Count == 0, $"Sent a default: {string.Join(", ", leaked)}");
        Assert.Equal(0, body.GetProperty("pokemon_id").GetInt32());
        AssertValidAgainstSchema("pokemon", body);
    }

    [Theory]
    [InlineData("raid", """{"pokemon_id":150,"gym_id":"","team":4}""")]
    [InlineData("egg", """{"level":5,"gym_id":"","team":4}""")]
    [InlineData("gym", """{"team":4,"gym_id":""}""")]
    public void AnEmptyGymIdIsOmitted(string type, string row)
    {
        Assert.False(Translate(type, row).TryGetProperty("gym_id", out _));
    }

    [Theory]
    [InlineData("raid", """{"pokemon_id":150,"gym_id":"abc.16","team":4}""")]
    [InlineData("egg", """{"level":5,"gym_id":"abc.16","team":4}""")]
    [InlineData("gym", """{"team":4,"gym_id":"abc.16"}""")]
    public void AGymIdThatIsSetIsSent(string type, string row)
    {
        Assert.Equal("abc.16", Translate(type, row).GetProperty("gym_id").GetString());
    }

    [Fact]
    public void FortIncludeEmptyZeroIsSentFalse()
    {
        var body = Translate("fort", """{"fort_type":"everything","include_empty":0,"change_types":"[]","distance":0}""");

        Assert.False(body.GetProperty("include_empty").GetBoolean());
        Assert.False(body.TryGetProperty("fort_type", out _));
        Assert.False(body.TryGetProperty("change_types", out _));
    }

    [Fact]
    public void FortWithoutIncludeEmptyIsRefused()
    {
        Assert.False(TrackingV2Translator.TryTranslate("fort", Row("""{"fort_type":"gym"}"""), null, out _, out var unsupported));
        Assert.Contains("include_empty", unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEggAtLevelZeroIsRefusedWithAReason()
    {
        Assert.False(TrackingV2Translator.TryTranslate("egg", Row("""{"level":0,"team":4}"""), null, out _, out var unsupported));
        Assert.Contains("level", unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDefaultValuesAreSent()
    {
        var pokemon = Translate("pokemon", """{"pokemon_id":25,"min_iv":90,"pvp_ranking_best":5,"min_cp":0}""");
        Assert.Equal(90, pokemon.GetProperty("min_iv").GetInt32());
        Assert.Equal(5, pokemon.GetProperty("pvp_ranking_best").GetInt32());
        Assert.False(pokemon.TryGetProperty("min_cp", out _));

        Assert.Equal("valor", Translate("raid", """{"pokemon_id":150,"team":2}""").GetProperty("team").GetString());
    }

    [Theory]
    [InlineData("pokemon", """{"pokemon_id":25}""")]
    [InlineData("raid", """{"pokemon_id":150}""")]
    [InlineData("egg", """{"level":5}""")]
    [InlineData("quest", """{"reward_type":7}""")]
    [InlineData("gym", """{"team":4}""")]
    [InlineData("maxbattle", """{"pokemon_id":150}""")]
    [InlineData("nest", """{"pokemon_id":25}""")]
    [InlineData("lure", """{"lure_id":501}""")]
    [InlineData("invasion", """{"grunt_type":"water"}""")]
    [InlineData("fort", """{"include_empty":0}""")]
    public void PingIsAlwaysWritten(string type, string row)
    {
        var withPing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row)!;
        withPing["ping"] = JsonSerializer.SerializeToElement("<@&1>");

        var body = Translate(type, JsonSerializer.Serialize(withPing));

        Assert.Equal("<@&1>", body.GetProperty("ping").GetString());
    }

    [Theory]
    [InlineData("pokemon", """{"pokemon_id":0}""")]
    [InlineData("quest", """{"reward_type":7}""")]
    [InlineData("lure", """{"lure_id":0}""")]
    public void ARequiredFieldIsSentEvenAtItsDefault(string type, string row)
    {
        var body = Translate(type, row);
        var field = Row(row).EnumerateObject().Single().Name;

        Assert.True(body.TryGetProperty(field, out _));
    }

    [Theory]
    [MemberData(nameof(TypeRows))]
    public void ARuleReadWithEveryFilterNullWritesBackWithEveryFilterAbsent(string type, string row)
    {
        // The read side fills each null with the v1 value DefaultsFor names; the write side has to recognise
        // every one of those values as "absent" again. A default the two sides disagree on would be written
        // back as a filter the user never set. template is the one deliberate exception: an empty one is
        // sent (AnEmptyTemplateIsStillSentSoTheV1RoundTripMatches).
        var v2Rule = Translate(type, row);
        var v1Row = TrackingV2Translator.ToV1Row(type, v2Rule, "user1");

        var body = Translate(type, v1Row.GetRawText());

        var stated = v2Rule.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var leaked = body.EnumerateObject()
            .Where(p => !stated.Contains(p.Name) && p.Name != "template" && TrackingV2Translator.DefaultsFor(type).ContainsKey(p.Name))
            .Select(p => $"{p.Name}={p.Value.GetRawText()}")
            .ToList();

        Assert.NotEmpty(TrackingV2Translator.DefaultsFor(type));
        Assert.True(leaked.Count == 0, $"{type} wrote back a default as a value: {string.Join(", ", leaked)}");
        AssertValidAgainstSchema(type, body);
    }

    [Theory]
    [MemberData(nameof(TypeRows))]
    public void AnEmptyTemplateIsStillSentSoTheV1RoundTripMatches(string type, string row)
    {
        var withTemplate = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row)!;
        withTemplate["template"] = JsonSerializer.SerializeToElement(string.Empty);

        var body = Translate(type, JsonSerializer.Serialize(withTemplate));

        Assert.Equal(string.Empty, body.GetProperty("template").GetString());
    }

    [Fact]
    public void ANullTemplateIsStillOmitted()
    {
        Assert.False(Translate("raid", """{"pokemon_id":150,"template":null}""").TryGetProperty("template", out _));
    }

    public static TheoryData<string, string> TypeRows() =>
        new()
        {
            { "pokemon", """{"pokemon_id":25}""" },
            { "raid", """{"pokemon_id":150}""" },
            { "egg", """{"level":5}""" },
            { "quest", """{"reward_type":7}""" },
            { "gym", """{"team":4}""" },
            { "maxbattle", """{"pokemon_id":150}""" },
            { "nest", """{"pokemon_id":25}""" },
            { "lure", """{"lure_id":501}""" },
            { "invasion", """{"grunt_type":"water"}""" },
            { "fort", """{"include_empty":0}""" },
        };

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Translate(string type, string row)
    {
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(row), null, out var translated, out var unsupported), unsupported);
        return translated;
    }

    /// <summary>
    /// The body names only properties the rule declares, carries every one it requires, and keeps every
    /// integer inside the bounds the schema states.
    /// </summary>
    private static void AssertValidAgainstSchema(string type, JsonElement body)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture()));
        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(TrackingV2ReadTranslationTests.SchemaName(type));
        var declared = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var required = schema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()!).ToList()
            : [];

        var properties = schema.GetProperty("properties");
        foreach (var property in body.EnumerateObject())
        {
            Assert.Contains(property.Name, declared);

            if (property.Value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var value = property.Value.GetDouble();
            var bounds = properties.GetProperty(property.Name);
            if (bounds.TryGetProperty("minimum", out var min))
            {
                Assert.True(value >= min.GetDouble(), $"V2{type}Rule.{property.Name} = {value} is below its minimum {min}");
            }

            if (bounds.TryGetProperty("maximum", out var max))
            {
                Assert.True(value <= max.GetDouble(), $"V2{type}Rule.{property.Name} = {value} is above its maximum {max}");
            }
        }

        foreach (var name in required)
        {
            Assert.True(body.TryGetProperty(name, out _), $"V2{type}Rule requires {name}");
        }
    }

    private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "poracleng-openapi-5.4.0.json");
}
