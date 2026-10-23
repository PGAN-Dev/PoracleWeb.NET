using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The read direction of <see cref="TrackingV2Translator"/>: a rule as PoracleNG 5.4.0's
/// <c>/api/v2/humans/{id}/tracking/{type}</c> returns it, turned back into the v1 row every caller in
/// PoracleWeb.NET still consumes.
/// </summary>
/// <remarks>
/// <para>
/// v2 reports every filter sitting at its stored wildcard as null. The v1 row has to carry the stored value
/// instead, and the value chosen is not cosmetic: <c>TrackingFieldPreserver</c> re-reads a row before every
/// edit and writes what it read back, so a wrong default becomes the stored filter on the next save.
/// </para>
/// <para>
/// The defaults are PoracleNG's own wildcard constants (<c>processor/internal/api/v2_wildcards.go</c> and the
/// per-type <c>*RowToRule</c> functions on <c>develop</c> 715c0d7e), and the fixture
/// <c>poracleng-540-v2-read-pairs.json</c> holds real v1/v2 pairs read from the 5.4.0 bed, so the assertions
/// are against what the server actually answers rather than against this translator's model of it.
/// </para>
/// </remarks>
public class TrackingV2ReadTranslationTests
{
    /// <summary>The eleven tracking types the 5.4.0 schema declares a <c>V2*Rule</c> for.</summary>
    public static TheoryData<string> AllV2Types() => new()
    {
        "pokemon", "raid", "egg", "quest", "gym", "maxbattle", "nest", "lure", "invasion", "fort", "incident",
    };

    /// <summary>The ten types PoracleWeb.NET also writes through <c>TryTranslate</c>.</summary>
    public static TheoryData<string> WritableTypes() => new()
    {
        "pokemon", "raid", "egg", "quest", "gym", "maxbattle", "nest", "lure", "invasion", "fort",
    };

    // ──────────────────────────────────────────────────────────────
    // The defaults table, against the schema, in both directions.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllV2Types))]
    public void EveryNullablePropertyOfTheRuleHasADefault(string type)
    {
        var defaults = TrackingV2Translator.DefaultsFor(type);

        var missing = SchemaProperties(type)
            .Where(p => p.Nullable)
            .Select(p => p.Name)
            .Where(name => !defaults.ContainsKey(name))
            .ToList();

        Assert.True(missing.Count == 0, $"V2 {type} nullable properties with no default: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(AllV2Types))]
    public void EveryDefaultNamesARealSchemaProperty(string type)
    {
        var declared = SchemaProperties(type).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var stray = TrackingV2Translator.DefaultsFor(type).Keys.Where(name => !declared.Contains(name)).ToList();

        Assert.True(stray.Count == 0, $"Defaults for {type} name properties the schema does not declare: {string.Join(", ", stray)}");
    }

    [Fact]
    public void AnUnknownTypeHasNoDefaultsAndCannotBeRead()
    {
        Assert.Throws<InvalidOperationException>(() => TrackingV2Translator.DefaultsFor("nonsense"));
        Assert.Throws<InvalidOperationException>(() => TrackingV2Translator.ToV1Row("nonsense", Json("""{"uid":1}"""), "human-1"));
    }

    // ──────────────────────────────────────────────────────────────
    // Real rules from the 5.4.0 bed.
    // ──────────────────────────────────────────────────────────────

    public static TheoryData<int> BedPairs()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < LoadPairs().Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BedPairs))]
    public void ARuleReadFromTheBedBecomesTheRowV1ReturnedForIt(int index)
    {
        var pair = LoadPairs()[index];
        var type = pair["type"]!.GetValue<string>();
        var v1 = pair["v1"]!.AsObject();
        var v2 = Json(pair["v2"]!.ToJsonString());

        var expected = (JsonObject)v1.DeepClone();

        // A per-type v2 read carries no profile_no (only the all_profiles snapshot does), so the v1 row's
        // copy has no source here. Task 2's snapshot test covers it.
        if (!v2.TryGetProperty("profile_no", out _))
        {
            expected.Remove("profile_no");
        }

        // Values PoracleNG's own read reports differently from what v1 stored: the legacy zeros it
        // canonicalises (#227) and an invasion catch-all's gender, which v2 never emits. Not ours to undo.
        if (pair["readChanges"] is JsonObject changes)
        {
            foreach (var (name, value) in changes)
            {
                expected[name] = value!.DeepClone();
            }
        }

        var actual = TrackingV2Translator.ToV1Row(type, v2, v1["id"]!.GetValue<string>());

        AssertSameRow(expected, actual);
    }

    [Fact]
    public void TheBedPairsCoverEveryWritableType()
    {
        var covered = LoadPairs().Select(p => p["type"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

        foreach (var type in WritableTypes())
        {
            Assert.Contains(type, covered);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // v1 -> TryTranslate -> ToV1Row is the identity.
    // ──────────────────────────────────────────────────────────────

    public static TheoryData<string, string> RepresentativeRows() => new()
    {
        {
            "pokemon", """
            {"uid":36486,"id":"user1","ping":"<@&1>","clean":7,"distance":1000,"template":"1","pokemon_id":25,
             "form":0,"costume":9000,"min_iv":90,"max_iv":100,"min_cp":0,"max_cp":9000,"min_level":0,
             "max_level":55,"atk":0,"def":0,"sta":0,"max_atk":15,"max_def":15,"max_sta":15,"gender":3,
             "min_weight":0,"max_weight":9000000,"min_time":0,"rarity":2,"max_rarity":6,"size":3,"max_size":5,
             "pvp_ranking_league":1500,"pvp_ranking_best":1,"pvp_ranking_worst":100,"pvp_ranking_min_cp":0,
             "pvp_ranking_cap":50,"pvp_ranking_evolution":0,"override_location_label":"",
             "override_areas":["area-1","area-2"]}
            """
        },
        {
            "raid", """
            {"uid":414,"id":"user1","ping":"","clean":1,"distance":0,"template":"1","team":2,"pokemon_id":150,
             "form":0,"costume":9000,"level":9000,"exclusive":1,"move":9000,"evolution":9000,"gym_id":"abc.16",
             "rsvp_changes":2,"override_location_label":"","override_areas":null}
            """
        },
        {
            "egg", """
            {"uid":178,"id":"user1","ping":"","clean":2,"distance":250,"template":"1","team":0,"level":5,
             "exclusive":0,"gym_id":null,"rsvp_changes":1,"override_location_label":"","override_areas":null}
            """
        },
        {
            "quest", """
            {"uid":900,"id":"user1","ping":"","clean":5,"distance":0,"template":"2","reward_type":7,"reward":25,
             "form":0,"shiny":1,"amount":3,"override_location_label":"","override_areas":null}
            """
        },
        {
            "gym", """
            {"uid":150,"id":"user1","ping":"","clean":0,"distance":0,"template":"1","team":3,"slot_changes":1,
             "battle_changes":1,"gym_id":null,"override_location_label":"","override_areas":null}
            """
        },
        {
            "maxbattle", """
            {"uid":91,"id":"user1","ping":"","clean":4,"distance":0,"template":"1","pokemon_id":9000,"form":0,
             "level":5,"move":9000,"gmax":1,"evolution":9000,"station_id":null,"override_location_label":"",
             "override_areas":null}
            """
        },
        {
            "nest", """
            {"uid":965,"id":"user1","ping":"","clean":0,"distance":0,"template":"1","pokemon_id":0,
             "min_spawn_avg":5,"form":0,"override_location_label":"","override_areas":null}
            """
        },
        {
            "lure", """
            {"uid":266,"id":"user1","ping":"","clean":3,"distance":500,"template":"1","lure_id":501,
             "override_location_label":"home","override_areas":null}
            """
        },
        {
            "invasion", """
            {"uid":407,"id":"user1","ping":"","clean":0,"distance":0,"template":"1","gender":2,
             "grunt_type":"water","override_location_label":"","override_areas":null}
            """
        },
        {
            "fort", """
            {"uid":63,"id":"user1","ping":"","distance":0,"template":"1","fort_type":"gym","include_empty":0,
             "change_types":"[\"name\",\"location\"]","override_location_label":"","override_areas":null}
            """
        },
    };

    [Theory]
    [MemberData(nameof(RepresentativeRows))]
    public void AV1RowSurvivesTheTripThroughV2AndBack(string type, string row)
    {
        var original = Json(row);

        Assert.True(
            TrackingV2Translator.TryTranslate(type, original, null, out var body, out var unsupported),
            unsupported);

        // A v2 read carries the uid beside the rule; TryTranslate drops it from a write body.
        var asRead = JsonNode.Parse(body.GetRawText())!.AsObject();
        asRead["uid"] = original.GetProperty("uid").GetInt32();

        var back = TrackingV2Translator.ToV1Row(type, Json(asRead.ToJsonString()), "user1");

        AssertSameRow(JsonNode.Parse(row)!.AsObject(), back);
    }

    // ──────────────────────────────────────────────────────────────
    // Null becomes the stored wildcard.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllV2Types))]
    public void ARuleOfNothingButNullsReadsAsEveryDefault(string type)
    {
        var rule = new JsonObject { ["uid"] = 1 };
        foreach (var property in SchemaProperties(type).Where(p => p.Nullable))
        {
            rule[property.Name] = null;
        }

        var row = TrackingV2Translator.ToV1Row(type, Json(rule.ToJsonString()), "human-1");

        foreach (var (name, value) in TrackingV2Translator.DefaultsFor(type))
        {
            if (name is "clean" or "edit" or "summary")
            {
                continue;
            }

            if (name == "level" && type is "raid" or "maxbattle")
            {
                // Resolved against pokemon_id, which is null here too: a by-level rule with no tier.
                continue;
            }

            Assert.True(
                JsonElement.DeepEquals(value, row.GetProperty(name)),
                $"{type}.{name}: expected {value.GetRawText()}, read {row.GetProperty(name).GetRawText()}");
        }

        if (type != "fort")
        {
            Assert.Equal(0, row.GetProperty("clean").GetInt32());
        }
    }

    [Theory]
    [InlineData("pokemon", "min_iv", -1)]
    [InlineData("pokemon", "max_iv", 100)]
    [InlineData("pokemon", "max_cp", 9000)]
    [InlineData("pokemon", "max_level", 55)]
    [InlineData("pokemon", "max_weight", 9000000)]
    [InlineData("pokemon", "max_atk", 15)]
    [InlineData("pokemon", "pvp_ranking_best", 1)]
    [InlineData("pokemon", "pvp_ranking_worst", 4096)]
    [InlineData("pokemon", "costume", 9000)]
    [InlineData("pokemon", "size", -1)]
    [InlineData("pokemon", "max_size", 5)]
    [InlineData("pokemon", "rarity", -1)]
    [InlineData("pokemon", "max_rarity", 6)]
    [InlineData("pokemon", "pokemon_id", 0)]
    [InlineData("pokemon", "gender", 0)]
    [InlineData("pokemon", "min_cp", 0)]
    [InlineData("raid", "pokemon_id", 9000)]
    [InlineData("raid", "costume", 9000)]
    [InlineData("raid", "move", 9000)]
    [InlineData("raid", "evolution", 9000)]
    [InlineData("raid", "team", 4)]
    [InlineData("raid", "rsvp_changes", 0)]
    [InlineData("raid", "exclusive", 0)]
    [InlineData("egg", "team", 4)]
    [InlineData("maxbattle", "pokemon_id", 9000)]
    [InlineData("maxbattle", "move", 9000)]
    [InlineData("maxbattle", "evolution", 9000)]
    [InlineData("maxbattle", "gmax", 0)]
    [InlineData("nest", "pokemon_id", 0)]
    [InlineData("quest", "reward", 0)]
    [InlineData("invasion", "gender", 0)]
    [InlineData("fort", "include_empty", 1)]
    public void ANullFilterReadsAsItsStoredWildcard(string type, string field, int stored)
    {
        var row = TrackingV2Translator.ToV1Row(type, Json($$"""{"uid":1,"{{field}}":null}"""), "human-1");

        Assert.Equal(stored, row.GetProperty(field).GetInt32());
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    [InlineData("fort")]
    [InlineData("invasion")]
    public void TheSharedStringsReadAsEmptyAndOverrideAreasAsNull(string type)
    {
        var row = TrackingV2Translator.ToV1Row(
            type,
            Json("""{"uid":1,"template":null,"ping":null,"override_location_label":null,"override_areas":null}"""),
            "human-1");

        Assert.Equal(string.Empty, row.GetProperty("template").GetString());
        Assert.Equal(string.Empty, row.GetProperty("ping").GetString());
        Assert.Equal(string.Empty, row.GetProperty("override_location_label").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("override_areas").ValueKind);
    }

    [Fact]
    public void ANullFortIncludeEmptyIsTrueBecauseThatIsWhatV2HidesAsNull()
    {
        // PoracleNG reads a stored include_empty 1 as null (wcFortIncludeEmpty = true), verified on the bed:
        // a fort rule v1 reports as include_empty 1 comes back from v2 as null. Reading null as 0 would
        // flip every such rule on its next edit.
        Assert.Equal(1, Read("fort", """{"uid":1,"include_empty":null}""").GetProperty("include_empty").GetInt32());
        Assert.Equal(0, Read("fort", """{"uid":1,"include_empty":false}""").GetProperty("include_empty").GetInt32());
        Assert.Equal(1, Read("fort", """{"uid":1,"include_empty":true}""").GetProperty("include_empty").GetInt32());
    }

    [Fact]
    public void ANullFortTypeIsTheEverythingCatchAllAndNullChangeTypesIsAnEmptyArray()
    {
        var row = Read("fort", """{"uid":1,"fort_type":null,"change_types":null}""");

        Assert.Equal("everything", row.GetProperty("fort_type").GetString());
        Assert.Equal("[]", row.GetProperty("change_types").GetString());
    }

    [Fact]
    public void FortChangeTypesBecomeTheCompactJsonStringV1Stores()
    {
        var row = Read("fort", """{"uid":1,"change_types":["name","location","removal","new"]}""");

        Assert.Equal("""["name","location","removal","new"]""", row.GetProperty("change_types").GetString());
    }

    [Fact]
    public void FortGainsNoCleanColumn()
    {
        Assert.False(Read("fort", """{"uid":1,"distance":null}""").TryGetProperty("clean", out _));
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void ANullLevelOnASpecificBossIsTheLevelPlaceholder(string type)
    {
        // PoracleNG stores 9000 for a by-pokemon rule's level and reports it as null (raidLevelOrNull).
        Assert.Equal(9000, Read(type, """{"uid":1,"pokemon_id":150,"level":null}""").GetProperty("level").GetInt32());
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void ANullLevelOnAByLevelRuleIsTheAnyTierWildcard(string type)
    {
        // A by-level rule with no tier is stored as 90 (wcLevelAny), and is also reported as null. The
        // pokemon_id beside it says which of the two the null stands for.
        var row = Read(type, """{"uid":1,"pokemon_id":null,"level":null}""");

        Assert.Equal(9000, row.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(90, row.GetProperty("level").GetInt32());
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("maxbattle")]
    public void AConcreteTierIsReadAsStored(string type)
    {
        var row = Read(type, """{"uid":1,"pokemon_id":null,"level":5}""");

        Assert.Equal(9000, row.GetProperty("pokemon_id").GetInt32());
        Assert.Equal(5, row.GetProperty("level").GetInt32());
    }

    [Fact]
    public void AMissingPropertyGetsItsDefaultToo()
    {
        // The v1 row is always complete; a property v2 left out entirely is read as its wildcard.
        var row = Read("gym", """{"uid":1,"team":"valor"}""");

        Assert.Equal(2, row.GetProperty("team").GetInt32());
        Assert.Equal(0, row.GetProperty("distance").GetInt32());
        Assert.Equal(0, row.GetProperty("slot_changes").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("gym_id").ValueKind);
        Assert.Equal(string.Empty, row.GetProperty("template").GetString());
        Assert.Equal(0, row.GetProperty("clean").GetInt32());
    }

    // ──────────────────────────────────────────────────────────────
    // Shape changes, inverted.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, false, true, 4)]
    [InlineData(true, true, true, 7)]
    public void CleanEditAndSummaryBecomeTheBitmask(bool clean, bool edit, bool summary, int mask)
    {
        static string Flag(bool value) => value ? "true" : "null";

        var row = Read("pokemon", $$"""{"uid":1,"clean":{{Flag(clean)}},"edit":{{Flag(edit)}},"summary":{{Flag(summary)}}}""");

        Assert.Equal(mask, row.GetProperty("clean").GetInt32());
        Assert.False(row.TryGetProperty("edit", out _));
        Assert.False(row.TryGetProperty("summary", out _));
    }

    [Theory]
    [InlineData("harmony", 0)]
    [InlineData("mystic", 1)]
    [InlineData("valor", 2)]
    [InlineData("instinct", 3)]
    [InlineData("any", 4)]
    public void ATeamNameBecomesItsInteger(string name, int stored)
    {
        Assert.Equal(stored, Read("gym", $$"""{"uid":1,"team":"{{name}}"}""").GetProperty("team").GetInt32());
    }

    [Theory]
    [InlineData("pokemon", "gender", "genderless", 3)]
    [InlineData("invasion", "gender", "female", 2)]
    [InlineData("raid", "rsvp_changes", "rsvp_only", 2)]
    [InlineData("egg", "rsvp_changes", "rsvp", 1)]
    public void AnEnumNameBecomesItsInteger(string type, string field, string name, int stored)
    {
        Assert.Equal(stored, Read(type, $$"""{"uid":1,"{{field}}":"{{name}}"}""").GetProperty(field).GetInt32());
    }

    [Theory]
    [InlineData("raid", "exclusive")]
    [InlineData("egg", "exclusive")]
    [InlineData("gym", "slot_changes")]
    [InlineData("gym", "battle_changes")]
    [InlineData("quest", "shiny")]
    [InlineData("maxbattle", "gmax")]
    public void ABooleanBecomesAZeroOrOneColumn(string type, string field)
    {
        Assert.Equal(1, Read(type, $$"""{"uid":1,"{{field}}":true}""").GetProperty(field).GetInt32());
        Assert.Equal(0, Read(type, $$"""{"uid":1,"{{field}}":false}""").GetProperty(field).GetInt32());
    }

    [Theory]
    [InlineData("pokemon", "gender", "both")]
    [InlineData("invasion", "gender", "genderless")]
    [InlineData("raid", "team", "rocket")]
    [InlineData("egg", "rsvp_changes", "maybe")]
    [InlineData("gym", "team", "Valor")]
    public void AnEnumNameThisBuildDoesNotKnowThrowsNamingTheTypeAndProperty(string type, string field, string name)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => TrackingV2Translator.ToV1Row(type, Json($$"""{"uid":1,"{{field}}":"{{name}}"}"""), "human-1"));

        Assert.Contains(type, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(field, thrown.Message, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────
    // Identity, and properties this build does not model.
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheIdIsTheHumanTheRuleWasReadFor()
    {
        Assert.Equal("human-7", TrackingV2Translator.ToV1Row("lure", Json("""{"uid":1,"lure_id":501}"""), "human-7")
            .GetProperty("id").GetString());
    }

    [Fact]
    public void UidProfileNoAndDescriptionAreKept()
    {
        var row = Read("lure", """{"uid":12,"profile_no":3,"description":"Lure type: **Normal Lure**","lure_id":501}""");

        Assert.Equal(12, row.GetProperty("uid").GetInt32());
        Assert.Equal(3, row.GetProperty("profile_no").GetInt32());
        Assert.Equal("Lure type: **Normal Lure**", row.GetProperty("description").GetString());
    }

    [Fact]
    public void ARuleWithoutProfileNoGainsNone()
    {
        // A per-type read is active-profile only and carries no profile_no; the caller knows the profile.
        Assert.False(Read("lure", """{"uid":12,"lure_id":501}""").TryGetProperty("profile_no", out _));
    }

    [Theory]
    [MemberData(nameof(AllV2Types))]
    public void AnUnknownPropertyIsCopiedThroughUnchanged(string type)
    {
        // A personal-shiny PoracleNG's personal_shiny / personal_background, or any field a newer PoracleNG adds.
        var row = Read(type, """{"uid":1,"personal_shiny":true,"personal_background":{"a":[1,2]},"newer":null}""");

        Assert.True(row.GetProperty("personal_shiny").GetBoolean());
        Assert.Equal("""{"a":[1,2]}""", row.GetProperty("personal_background").GetRawText());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("newer").ValueKind);
    }

    [Fact]
    public void AnIncidentKeepsItsDisplayType()
    {
        var row = Read("incident", """{"uid":1,"display_type":8,"clean":true,"distance":null}""");

        Assert.Equal(8, row.GetProperty("display_type").GetInt32());
        Assert.Equal(1, row.GetProperty("clean").GetInt32());
        Assert.Equal(0, row.GetProperty("distance").GetInt32());
    }

    // ──────────────────────────────────────────────────────────────

    private static JsonElement Read(string type, string rule) =>
        TrackingV2Translator.ToV1Row(type, Json(rule), "human-1");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static void AssertSameRow(JsonObject expected, JsonElement actual)
    {
        var actualNode = JsonNode.Parse(actual.GetRawText())!.AsObject();

        var differences = expected.Select(p => p.Key)
            .Union(actualNode.Select(p => p.Key))
            .Where(name => !JsonNode.DeepEquals(expected[name], actualNode[name])
                || expected.ContainsKey(name) != actualNode.ContainsKey(name))
            .Select(name => $"{name}: expected {Describe(expected, name)}, read {Describe(actualNode, name)}")
            .ToList();

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private static string Describe(JsonObject row, string name) =>
        row.ContainsKey(name) ? row[name]?.ToJsonString() ?? "null" : "<absent>";

    private static List<JsonObject> LoadPairs()
    {
        var pairs = JsonNode.Parse(File.ReadAllText(Fixture("poracleng-540-v2-read-pairs.json")))!.AsArray();
        return pairs.Select(p => p!.AsObject()).ToList();
    }

    private static List<(string Name, bool Nullable)> SchemaProperties(string type)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("poracleng-openapi-5.4.0.json")));
        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(SchemaName(type));

        return schema.GetProperty("properties").EnumerateObject()
            .Select(p => (p.Name, IsNullable(p.Value)))
            .ToList();
    }

    private static bool IsNullable(JsonElement property) =>
        property.TryGetProperty("type", out var kind)
        && kind.ValueKind == JsonValueKind.Array
        && kind.EnumerateArray().Any(t => t.GetString() == "null");

    internal static string SchemaName(string type) =>
        $"V2{char.ToUpperInvariant(type[0])}{type[1..]}Rule";

    private static string Fixture(string name, [System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", name);
}
