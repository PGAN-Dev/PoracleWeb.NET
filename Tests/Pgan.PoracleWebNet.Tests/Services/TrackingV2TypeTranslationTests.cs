using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The ten per-type field tables <see cref="TrackingV2Translator"/> carries, checked against the schema
/// they were derived from and against the rows PoracleNG actually stores.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>V2*Rule</c> sets <c>additionalProperties: false</c>, so a field the table leaks is a 422 and a
/// field the table misses is a value the translator cannot state, so the write is refused. Neither shows up
/// until a user's edit fails on it, which is why they need a test that reads the schema rather than the
/// translator.
/// </para>
/// <para>
/// <c>V2Schema</c> is the property list of each <c>V2*Rule</c>, read from the PoracleNG 5.4.0
/// <c>/openapi.json</c> fixture (<c>Fixtures/poracleng-openapi-5.4.0.json</c>) rather than typed out, so a
/// schema change shows up here the day the fixture is refreshed. <c>V1Row</c> is a full row of the matching
/// v1 tracking columns, confirmed against a live instance rather than read from the Go source, per CLAUDE.md.
/// </para>
/// </remarks>
public class TrackingV2TypeTranslationTests
{
    /// <summary>Every property each <c>V2*Rule</c> declares, from the 5.4.0 openapi fixture.</summary>
    private static readonly string[] V2SchemaTypes = ["pokemon", "raid", "egg", "quest", "gym", "maxbattle", "nest", "lure", "invasion", "fort"];
    private static readonly Dictionary<string, string[]> V2Schema = LoadV2Schema();

    /// <summary>
    /// Schema properties no v1 column feeds. <c>V2InvasionRule</c>'s <c>type_id</c>, <c>grunt_id</c>,
    /// <c>everything</c> and <c>boss</c> are input conveniences that resolve to a <c>grunt_type</c>; this
    /// application only ever holds the name, so it sends <c>grunt_type</c> and never the other four.
    /// </summary>
    private static readonly Dictionary<string, string[]> NoV1Source = new(StringComparer.Ordinal)
    {
        ["invasion"] = ["boss", "everything", "grunt_id", "type_id"],
    };

    /// <summary>
    /// A full v1 row of each type, in the shape PoracleWeb's models serialize and PoracleNG's v1 read
    /// returns. Sentinels are present on purpose: they are sent verbatim and 5.2.1 stores them exactly as
    /// v1 does.
    /// </summary>
    /// <remarks>
    /// These rows exist to prove every schema field is written, so each one has to be a row v2 will
    /// actually accept. Three values moved when the bound tables landed: pokemon <c>rarity</c> and
    /// <c>size</c> were 0, and raid/maxbattle were by-boss rows holding <c>level</c> 9000. All three are
    /// real stored values and all three are outside v2's declared range, so a server enforcing it refuses
    /// them and they cannot carry a field-coverage assertion. They are asserted in
    /// <see cref="TrackingV2BoundsTests"/> instead, against the production counts that found them.
    /// </remarks>
    private static readonly Dictionary<string, string> V1Row = new(StringComparer.Ordinal)
    {
        ["pokemon"] = """
            {"uid":36486,"id":"user1","profile_no":1,"ping":"<@&1>","description":"**Pikachu**","clean":3,
             "distance":1000,"template":"1","pokemon_id":25,"form":0,"costume":9000,"min_iv":90,
             "max_iv":100,"min_cp":0,"max_cp":9000,"min_level":0,"max_level":55,"atk":0,"def":0,"sta":0,
             "max_atk":15,"max_def":15,"max_sta":15,"gender":2,"min_weight":0,"max_weight":9000000,
             "min_time":0,"rarity":2,"max_rarity":6,"size":3,"max_size":5,"pvp_ranking_league":1500,
             "pvp_ranking_best":1,"pvp_ranking_worst":100,"pvp_ranking_min_cp":0,"pvp_ranking_cap":50,
             "pvp_ranking_evolution":0,"override_location_label":"","override_areas":null}
            """,
        ["raid"] = """
            {"uid":414,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "team":4,"pokemon_id":9000,"form":0,"costume":9000,"level":5,"exclusive":0,"move":9000,
             "evolution":9000,"gym_id":null,"rsvp_changes":0,"override_location_label":"",
             "override_areas":null,"description":"**Mewtwo**"}
            """,
        ["egg"] = """
            {"uid":178,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "team":4,"level":5,"exclusive":0,"gym_id":null,"rsvp_changes":0,
             "override_location_label":"","override_areas":null,"description":"**Level 5 eggs**"}
            """,
        ["quest"] = """
            {"uid":900,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "reward_type":7,"reward":25,"form":0,"shiny":0,"amount":1,"override_location_label":"",
             "override_areas":null,"description":"**Pikachu**"}
            """,
        ["gym"] = """
            {"uid":150,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "team":4,"slot_changes":1,"battle_changes":0,"gym_id":"","override_location_label":"",
             "override_areas":null,"description":"**All team's gyms**"}
            """,
        ["maxbattle"] = """
            {"uid":91,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "pokemon_id":9000,"form":0,"level":5,"move":9000,"gmax":0,"evolution":9000,
             "station_id":null,"override_location_label":"","override_areas":null,
             "description":"**Mewtwo**"}
            """,
        ["nest"] = """
            {"uid":965,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "pokemon_id":25,"min_spawn_avg":1,"form":0,"override_location_label":"",
             "override_areas":null,"description":"**Pikachu**"}
            """,
        ["lure"] = """
            {"uid":266,"id":"user1","profile_no":1,"ping":"<@&1>","clean":0,"distance":0,"template":"1",
             "lure_id":501,"override_location_label":"","override_areas":null,
             "description":"Lure type: **Normal Lure**"}
            """,
        ["invasion"] = """
            {"uid":407,"id":"user1","profile_no":1,"ping":"<@&1>","clean":5,"distance":500,"template":"1",
             "gender":2,"grunt_type":"water","override_location_label":"","override_areas":null,
             "description":"Grunt type: **Water** | gender: female"}
            """,
        ["fort"] = """
            {"uid":63,"id":"user1","profile_no":1,"ping":"<@&1>","distance":0,"template":"1","fort_type":"gym",
             "include_empty":0,"change_types":"[\"name\"]","override_location_label":"",
             "override_areas":null,"description":"Fort updates: **gym**"}
            """,
    };

    public static TheoryData<string> V2Types()
    {
        var data = new TheoryData<string>();
        foreach (var type in V1Row.Keys)
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(V2Types))]
    public void AFullStoredRowTranslatesForEveryTypeThatMoved(string type)
    {
        Assert.True(
            TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), ServerDoesNotBoundAnything, out _, out var unsupported),
            $"A stored {type} row must reach /api/v2, not fall back: {unsupported}");
    }

    [Theory]
    [MemberData(nameof(V2Types))]
    public void NothingOutsideTheSchemaReachesTheWire(string type)
    {
        // additionalProperties: false. One leaked field is a 422, and the fallback hides it.
        TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), ServerDoesNotBoundAnything, out var translated, out _);

        var leaked = translated.EnumerateObject()
            .Select(p => p.Name)
            .Where(name => !V2Schema[type].Contains(name, StringComparer.Ordinal))
            .ToList();

        Assert.True(leaked.Count == 0, $"V2{type}Rule has no field for: {string.Join(", ", leaked)}");
    }

    [Theory]
    [MemberData(nameof(V2Types))]
    public void EverySchemaFieldWithAV1SourceIsWritten(string type)
    {
        // The other half. A schema field missing from the type's table is a filter PoracleWeb would send
        // to v1 forever without anyone noticing, because the write still succeeds.
        TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), ServerDoesNotBoundAnything, out var translated, out _);

        var written = translated.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var noSource = NoV1Source.GetValueOrDefault(type, []);
        var row = Row(V1Row[type]);

        // A field holding its default is rightly absent: v2 stores an absent property as that value.
        var missing = V2Schema[type]
            .Where(name => !written.Contains(name) && !noSource.Contains(name) && !HoldsItsDefault(type, row, name))
            .ToList();

        Assert.True(missing.Count == 0, $"The {type} table does not write: {string.Join(", ", missing)}");
    }

    // ──────────────────────────────────────────────────────────────
    // The shape changes, one per kind. Values taken from a live 5.2.1 round-trip.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "harmony")]
    [InlineData(1, "mystic")]
    [InlineData(2, "valor")]
    [InlineData(3, "instinct")]
    [InlineData(4, "any")]
    public void TeamBecomesItsEnumName(int team, string expected)
    {
        var body = Translate("gym", $$"""{"team":{{team}},"distance":0}""");

        Assert.Equal(expected, body.GetProperty("team").GetString());
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "rsvp")]
    [InlineData(2, "rsvp_only")]
    public void RsvpChangesBecomesItsEnumName(int rsvp, string expected)
    {
        var body = Translate("egg", $$"""{"level":5,"rsvp_changes":{{rsvp}}}""");

        // 0 ("none") is the read default, so it goes out as absent.
        Assert.Equal(rsvp == 0 ? null : expected, body.TryGetProperty("rsvp_changes", out var sent) ? sent.GetString() : null);
    }

    [Theory]
    [InlineData("raid", """{"exclusive":1}""", "exclusive")]
    [InlineData("gym", """{"team":4,"slot_changes":1}""", "slot_changes")]
    [InlineData("gym", """{"team":4,"battle_changes":1}""", "battle_changes")]
    [InlineData("quest", """{"reward_type":7,"shiny":1}""", "shiny")]
    [InlineData("maxbattle", """{"gmax":1}""", "gmax")]
    [InlineData("fort", """{"include_empty":1}""", "include_empty")]
    public void EveryZeroOrOneColumnBecomesARealBoolean(string type, string row, string field)
    {
        Assert.True(Translate(type, row).GetProperty(field).GetBoolean());
    }

    [Fact]
    public void FortAlwaysStatesIncludeEmptyBecauseOmittingItFlipsTheDefault()
    {
        // Verified live on 5.2.1: a PUT that omitted include_empty turned a stored 0 into a 1 and the
        // alert text gained "including empty changes". v1 defaults it FALSE, v2 defaults it TRUE.
        Assert.False(Translate("fort", """{"fort_type":"gym","include_empty":0}""")
            .GetProperty("include_empty").GetBoolean());

        Assert.False(
            TrackingV2Translator.TryTranslate(
                "fort", Row("""{"fort_type":"gym","distance":0}"""), ServerDoesNotBoundAnything, out _, out var unsupported),
            "A fort row with no include_empty cannot be sent faithfully, so the translator must decline it.");
        Assert.Contains("include_empty", unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void FortCarriesNoCleanFieldAtAll()
    {
        // V2FortRule has no clean/edit/summary, and fort_tracking has no column for them either.
        var body = Translate("fort", """{"fort_type":"gym","include_empty":0,"clean":0}""");

        foreach (var name in new[] { "clean", "edit", "summary" })
        {
            Assert.False(body.TryGetProperty(name, out _), $"V2FortRule has no {name}");
        }
    }

    [Fact]
    public void AJsonStringChangeTypesColumnBecomesAnArray()
    {
        // The v1 read returns change_types as a JSON string, and TrackingFieldPreserver carries the column
        // forward verbatim. v2 declares an array.
        var body = Translate("fort", """{"fort_type":"gym","include_empty":0,"change_types":"[\"name\"]"}""");

        Assert.Equal("name", body.GetProperty("change_types").EnumerateArray().Single().GetString());
    }

    [Fact]
    public void The9000SentinelGoesOutAsAbsentAndARealValueIsSent()
    {
        // 9000 is what v2 reports as null for costume, move and evolution, so an absent property stores the
        // same thing. A real value is still sent.
        var body = Translate("raid", @"{""pokemon_id"":9000,""level"":5,""costume"":9000,""move"":9000,""evolution"":9000}");

        foreach (var name in new[] { "costume", "move", "evolution", "pokemon_id" })
        {
            Assert.False(body.TryGetProperty(name, out _), $"{name} 9000 should be absent");
        }

        Assert.Equal(5, body.GetProperty("level").GetInt32());
        Assert.Equal(
            7,
            Translate("raid", @"{""pokemon_id"":9000,""level"":5,""move"":7}").GetProperty("move").GetInt32());
    }

    // ──────────────────────────────────────────────────────────────
    // Required fields. Each refusal is paired with the legitimate case that must still reach v2.
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void AnEggWithoutALevelIsRefused()
    {
        // V2EggRule declares level required with minimum 1 and Egg.Level is a plain int defaulting to 0, so an
        // egg built without one is a 422 from v2 -- verified live. There is no v1 write left to send it to, so
        // the translator declines and the write is refused before anything is sent.
        Assert.False(
            TrackingV2Translator.TryTranslate("egg", Row("""{"level":0,"team":4}"""), ServerDoesNotBoundAnything, out _, out var unsupported));
        Assert.Contains("level", unsupported, StringComparison.Ordinal);

        Assert.True(TrackingV2Translator.TryTranslate("egg", Row("""{"level":1,"team":4}"""), ServerDoesNotBoundAnything, out _, out _));
        Assert.True(TrackingV2Translator.TryTranslate("egg", Row("""{"level":5,"team":4}"""), ServerDoesNotBoundAnything, out _, out _));
    }

    [Theory]
    [InlineData("lure", """{"distance":0}""", """{"lure_id":501}""")]
    [InlineData("quest", """{"distance":0}""", """{"reward_type":7}""")]
    [InlineData("gym", """{"distance":0}""", """{"team":4}""")]
    [InlineData("pokemon", """{"distance":0}""", """{"pokemon_id":25}""")]
    public void ARowMissingWhatV2RequiresIsRefusedAndAnOrdinaryOneIsNot(
        string type, string without, string with)
    {
        Assert.False(TrackingV2Translator.TryTranslate(type, Row(without), ServerDoesNotBoundAnything, out _, out _));
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(with), ServerDoesNotBoundAnything, out _, out var unsupported), unsupported);
    }

    // ──────────────────────────────────────────────────────────────
    // Declining rather than guessing, on the eight types that joined the pilot.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("raid", """{"pokemon_id":150,"some_field_from_a_newer_poracle":4}""")]
    [InlineData("raid", """{"pokemon_id":150,"team":9}""")]
    [InlineData("raid", """{"pokemon_id":150,"rsvp_changes":7}""")]
    [InlineData("raid", """{"pokemon_id":150,"clean":9}""")]
    [InlineData("gym", """{"team":4,"slot_changes":2}""")]
    [InlineData("fort", """{"fort_type":"stadium","include_empty":0}""")]
    [InlineData("fort", """{"fort_type":"gym","include_empty":0,"clean":1}""")]
    public void ARowV2CannotCarryFaithfullyDeclines(string type, string row)
    {
        Assert.False(TrackingV2Translator.TryTranslate(type, Row(row), ServerDoesNotBoundAnything, out _, out var unsupported));
        Assert.NotNull(unsupported);
    }

    /// <summary>PoracleNG 5.4.0 stores ping on every type, so a set one is written verbatim and an empty one is dropped.</summary>
    [Fact]
    public void ASetPingIsAlwaysWrittenThrough()
    {
        const string mention = "<@&400027130022592512>";

        Assert.True(
            TrackingV2Translator.TryTranslate(
                "raid", Row($$"""{"pokemon_id":150,"ping":"{{mention}}"}"""), ServerDoesNotBoundAnything,
                out var translated, out var unsupported),
            unsupported);
        Assert.Equal(mention, translated.GetProperty("ping").GetString());
    }

    [Fact]
    public void AnEmptyPingNeverBlocksTheWrite()
    {
        Assert.True(
            TrackingV2Translator.TryTranslate(
                "raid", Row("""{"pokemon_id":150,"ping":""}"""), ServerDoesNotBoundAnything,
                out var translated, out var unsupported),
            unsupported);
        Assert.False(translated.TryGetProperty("ping", out _));
    }

    [Fact]
    public void ANonStringPingDeclines()
    {
        Assert.False(
            TrackingV2Translator.TryTranslate(
                "raid", Row("""{"pokemon_id":150,"ping":5}"""), ServerDoesNotBoundAnything, out _, out var unsupported));
        Assert.NotNull(unsupported);
    }

    [Theory]
    [InlineData("pokestop")]
    [InlineData("gym")]
    [InlineData("everything")]
    public void EveryFortTypeThatIsActuallyStoredStillReachesV2(string fortType)
    {
        // The legitimate-case half of the fort_type refusal above. These three are the only values
        // FortChangeOptions.ValidFortTypes permits, so all of them must translate.
        Assert.True(
            TrackingV2Translator.TryTranslate(
                "fort",
                Row($$"""{"fort_type":"{{fortType}}","include_empty":0}"""), ServerDoesNotBoundAnything, out _,
                out var unsupported),
            unsupported);
    }

    [Fact]
    public void AnythingWithNoV2TableSaysSo()
    {
        Assert.False(TrackingV2Translator.Handles("nonsense"));
        Assert.False(TrackingV2Translator.TryTranslate("nonsense", Row("""{"uid":1}"""), ServerDoesNotBoundAnything, out _, out _));
    }

    [Theory]
    [InlineData("blanche")]
    [InlineData("giovanni")]
    [InlineData("water")]
    [InlineData("everything")]
    public void AnInvasionRuleTranslatesByItsGruntName(string gruntType)
    {
        // grunt_type is the targeting field and the only one this application ever holds. Whether a
        // particular name is one the server knows is asked of the server, in PoracleTrackingProxy --
        // the translator's job is the shape. See #841.
        var translated = Translate("invasion", $$"""{"grunt_type":"{{gruntType}}","distance":500}""");

        Assert.Equal(gruntType, translated.GetProperty("grunt_type").GetString());
        Assert.Equal(500, translated.GetProperty("distance").GetInt32());
        Assert.False(translated.TryGetProperty("type_id", out _));
        Assert.False(translated.TryGetProperty("grunt_id", out _));
    }

    [Theory]
    [InlineData(0, "any")]
    [InlineData(1, "male")]
    [InlineData(2, "female")]
    public void AnInvasionGenderTranslatesAlongsideTheGruntName(int stored, string expected)
    {
        // The schema contradicts itself here -- V2InvasionRule.gender says gender is "ONLY valid together
        // with type_id" while grunt_type says it "may be combined with gender". A live build stores
        // gender 1 for {"grunt_type":"water","gender":"male"}, so grunt_type's description is the right
        // one and these three must translate.
        var translated = Translate("invasion", $$"""{"grunt_type":"water","gender":{{stored}}}""");

        // 0 ("any") is the read default, so it goes out as absent.
        Assert.Equal(stored == 0 ? null : expected, translated.TryGetProperty("gender", out var sent) ? sent.GetString() : null);
    }

    [Fact]
    public void AGenderlessInvasionIsRefusedBecauseV2HasNoWordForIt()
    {
        // V2InvasionRule.gender is any|male|female. InvasionCreate.Gender is [Range(0, 3)], so 3 is
        // expressible here; mapping it to any of the three would mean something the user did not ask for.
        // Latent rather than live -- production holds 0, 1 and 2 only across 201 rules.
        Assert.False(
            TrackingV2Translator.TryTranslate(
                "invasion",
                Row("""{"grunt_type":"water","gender":3}"""), ServerDoesNotBoundAnything, out _,
                out var unsupported));

        Assert.Contains("gender", unsupported, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvasionRuleWithNoGruntNameCannotBeTranslated()
    {
        // grunt_type is the one targeting field; without it v2 would store "everything", which is a
        // different rule from the one being edited.
        Assert.False(
            TrackingV2Translator.TryTranslate(
                "invasion", Row("""{"distance":500}"""), ServerDoesNotBoundAnything, out _, out _));
    }

    /// <summary>
    /// These tests are about shape translation, not value limits, so they run against a server that
    /// declares none. The limits have their own file, and passing them here would couple every shape
    /// assertion to whatever upstream currently bounds.
    /// </summary>
    private const IReadOnlyDictionary<string, TrackingV2Translator.Bound>? ServerDoesNotBoundAnything = null;

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Whether the stored row holds, for a v2 field, the value a v2 read reports as null.</summary>
    private static bool HoldsItsDefault(string type, JsonElement row, string v2Name)
    {
        // clean, edit and summary are one v1 mask.
        var v1Name = v2Name is "edit" or "summary" ? "clean" : v2Name;

        if (!row.TryGetProperty(v1Name, out var value) || !TrackingV2Translator.DefaultsFor(type).TryGetValue(v2Name, out var fallback))
        {
            return false;
        }

        static string? Normalise(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.String => e.GetString(),
            _ => e.GetRawText(),
        };

        return v1Name == "clean" && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() == 0
            : Normalise(value) == Normalise(fallback);
    }

    [Fact]
    public void TheSchemaListsAreReadFromTheFixtureAndCarryWhat540Added()
    {
        // ping arrived on every V2*Rule with jfberry/PoracleNG#249, summary with the clean split. A schema
        // list typed out by hand from 5.2.1 had neither, so it could not catch a table that leaked or missed them.
        foreach (var type in V1Row.Keys)
        {
            Assert.Contains("ping", V2Schema[type]);
        }

        Assert.Contains("summary", V2Schema["pokemon"]);
        Assert.DoesNotContain("summary", V2Schema["fort"]);
        Assert.Contains("grunt_type", V2Schema["invasion"]);
    }

    private static Dictionary<string, string[]> LoadV2Schema()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture()));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        return V2SchemaTypes
            .ToDictionary(
                type => type,
                type => schemas.GetProperty(TrackingV2ReadTranslationTests.SchemaName(type)).GetProperty("properties")
                    .EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "poracleng-openapi-5.4.0.json");

    private static JsonElement Translate(string type, string row)
    {
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(row), ServerDoesNotBoundAnything, out var translated, out var unsupported), unsupported);
        return translated;
    }
}
