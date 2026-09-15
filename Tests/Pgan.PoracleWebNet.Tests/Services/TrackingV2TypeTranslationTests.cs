using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The nine per-type field tables <see cref="TrackingV2Translator"/> carries, checked against the schema
/// they were derived from and against the rows PoracleNG actually stores.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>V2*Rule</c> sets <c>additionalProperties: false</c>, so a field the table leaks is a 422 and a
/// field the table misses is a value quietly sent to v1 forever. Both failures are invisible at runtime,
/// because the fallback catches them and the write still succeeds — which is exactly why they need a test
/// that reads the schema rather than the translator.
/// </para>
/// <para>
/// <c>V2Schema</c> is the property list of each <c>V2*Rule</c> in 5.2.1's
/// <c>processor/internal/api/testdata/openapi.golden.json</c>, and <c>V1Row</c> is a full row of the
/// matching v1 tracking columns. Both were confirmed against the live 5.2.1 instance rather than read from
/// the Go source, per CLAUDE.md.
/// </para>
/// </remarks>
public class TrackingV2TypeTranslationTests
{
    /// <summary>Every property each <c>V2*Rule</c> declares, from 5.2.1's openapi.golden.json.</summary>
    private static readonly Dictionary<string, string[]> V2Schema = new(StringComparer.Ordinal)
    {
        ["pokemon"] =
        [
            "atk", "clean", "costume", "def", "distance", "edit", "form", "gender", "max_atk", "max_cp",
            "max_def", "max_iv", "max_level", "max_rarity", "max_size", "max_sta", "max_weight", "min_cp",
            "min_iv", "min_level", "min_time", "min_weight", "override_areas", "override_location_label",
            "pokemon_id", "pvp_ranking_best", "pvp_ranking_cap", "pvp_ranking_evolution",
            "pvp_ranking_league", "pvp_ranking_min_cp", "pvp_ranking_worst", "rarity", "size", "sta",
            "summary", "template",
        ],
        ["raid"] =
        [
            "clean", "costume", "distance", "edit", "evolution", "exclusive", "form", "gym_id", "level",
            "move", "override_areas", "override_location_label", "pokemon_id", "rsvp_changes", "summary",
            "team", "template",
        ],
        ["egg"] =
        [
            "clean", "distance", "edit", "exclusive", "gym_id", "level", "override_areas",
            "override_location_label", "rsvp_changes", "summary", "team", "template",
        ],
        ["quest"] =
        [
            "amount", "clean", "distance", "edit", "form", "override_areas", "override_location_label",
            "reward", "reward_type", "shiny", "summary", "template",
        ],
        ["gym"] =
        [
            "battle_changes", "clean", "distance", "edit", "gym_id", "override_areas",
            "override_location_label", "slot_changes", "summary", "team", "template",
        ],
        ["maxbattle"] =
        [
            "clean", "distance", "edit", "evolution", "form", "gmax", "level", "move", "override_areas",
            "override_location_label", "pokemon_id", "station_id", "summary", "template",
        ],
        ["nest"] =
        [
            "clean", "distance", "edit", "form", "min_spawn_avg", "override_areas",
            "override_location_label", "pokemon_id", "summary", "template",
        ],
        ["lure"] =
        [
            "clean", "distance", "edit", "lure_id", "override_areas", "override_location_label", "summary",
            "template",
        ],
        ["fort"] =
        [
            "change_types", "distance", "fort_type", "include_empty", "override_areas",
            "override_location_label", "template",
        ],
    };

    /// <summary>
    /// A full v1 row of each type, in the shape PoracleWeb's models serialize and PoracleNG's v1 read
    /// returns. Sentinels are present on purpose: they are sent verbatim and 5.2.1 stores them exactly as
    /// v1 does.
    /// </summary>
    private static readonly Dictionary<string, string> V1Row = new(StringComparer.Ordinal)
    {
        ["pokemon"] = """
            {"uid":36486,"id":"user1","profile_no":1,"ping":"","description":"**Pikachu**","clean":3,
             "distance":1000,"template":"1","pokemon_id":25,"form":0,"costume":9000,"min_iv":90,
             "max_iv":100,"min_cp":0,"max_cp":9000,"min_level":0,"max_level":55,"atk":0,"def":0,"sta":0,
             "max_atk":15,"max_def":15,"max_sta":15,"gender":2,"min_weight":0,"max_weight":9000000,
             "min_time":0,"rarity":0,"max_rarity":6,"size":0,"max_size":5,"pvp_ranking_league":1500,
             "pvp_ranking_best":1,"pvp_ranking_worst":100,"pvp_ranking_min_cp":0,"pvp_ranking_cap":50,
             "pvp_ranking_evolution":0,"override_location_label":"","override_areas":null}
            """,
        ["raid"] = """
            {"uid":414,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "team":4,"pokemon_id":150,"form":0,"costume":9000,"level":9000,"exclusive":0,"move":9000,
             "evolution":9000,"gym_id":null,"rsvp_changes":0,"override_location_label":"",
             "override_areas":null,"description":"**Mewtwo**"}
            """,
        ["egg"] = """
            {"uid":178,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "team":4,"level":5,"exclusive":0,"gym_id":null,"rsvp_changes":0,
             "override_location_label":"","override_areas":null,"description":"**Level 5 eggs**"}
            """,
        ["quest"] = """
            {"uid":900,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "reward_type":7,"reward":25,"form":0,"shiny":0,"amount":1,"override_location_label":"",
             "override_areas":null,"description":"**Pikachu**"}
            """,
        ["gym"] = """
            {"uid":150,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "team":4,"slot_changes":1,"battle_changes":0,"gym_id":"","override_location_label":"",
             "override_areas":null,"description":"**All team's gyms**"}
            """,
        ["maxbattle"] = """
            {"uid":91,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "pokemon_id":150,"form":0,"level":9000,"move":9000,"gmax":0,"evolution":9000,
             "station_id":null,"override_location_label":"","override_areas":null,
             "description":"**Mewtwo**"}
            """,
        ["nest"] = """
            {"uid":965,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "pokemon_id":25,"min_spawn_avg":1,"form":0,"override_location_label":"",
             "override_areas":null,"description":"**Pikachu**"}
            """,
        ["lure"] = """
            {"uid":266,"id":"user1","profile_no":1,"ping":"","clean":0,"distance":0,"template":"1",
             "lure_id":501,"override_location_label":"","override_areas":null,
             "description":"Lure type: **Normal Lure**"}
            """,
        ["fort"] = """
            {"uid":63,"id":"user1","profile_no":1,"ping":"","distance":0,"template":"1","fort_type":"gym",
             "include_empty":0,"change_types":"[\"name\"]","override_location_label":"",
             "override_areas":null,"description":"Fort updates: **gym**"}
            """,
    };

    public static TheoryData<string> V2Types()
    {
        var data = new TheoryData<string>();
        foreach (var type in V2Schema.Keys)
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
            TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), out _, out var unsupported),
            $"A stored {type} row must reach /api/v2, not fall back: {unsupported}");
    }

    [Theory]
    [MemberData(nameof(V2Types))]
    public void NothingOutsideTheSchemaReachesTheWire(string type)
    {
        // additionalProperties: false. One leaked field is a 422, and the fallback hides it.
        TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), out var translated, out _);

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
        TrackingV2Translator.TryTranslate(type, Row(V1Row[type]), out var translated, out _);

        var written = translated.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var missing = V2Schema[type].Where(name => !written.Contains(name)).ToList();

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

        Assert.Equal(expected, body.GetProperty("rsvp_changes").GetString());
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
                "fort", Row("""{"fort_type":"gym","distance":0}"""), out _, out var unsupported),
            "A fort row with no include_empty cannot be sent faithfully and must fall back to v1.");
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
    public void SentinelsAreSentVerbatimRatherThanOmitted()
    {
        // The migration guide says to omit them. Verified on 5.2.1 instead: a raid PUT carrying level,
        // costume, move and evolution at 9000 stored exactly what the v1 create stores, and the v1 read
        // came back byte-identical but for the rotated uid. Omitting them would leave
        // TrackingUpdateReconciler comparing a stored 9000 against an absent field.
        var body = Translate("raid", """{"pokemon_id":150,"level":9000,"costume":9000,"move":9000,"evolution":9000}""");

        foreach (var name in new[] { "level", "costume", "move", "evolution" })
        {
            Assert.Equal(9000, body.GetProperty(name).GetInt32());
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Required fields. Each refusal is paired with the legitimate case that must still reach v2.
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void AnEggWithoutALevelGoesToV1RatherThanBeingRefused()
    {
        // V2EggRule declares level required with minimum 1 and Egg.Level is a plain int defaulting to 0,
        // so profile import, quick-pick apply and the cleaning fetch-mutate-POST all build eggs v2 answers
        // 422 to -- verified live. v1 has stored level 0 for years and keeps doing so.
        Assert.False(
            TrackingV2Translator.TryTranslate("egg", Row("""{"level":0,"team":4}"""), out _, out var unsupported));
        Assert.Contains("level", unsupported, StringComparison.Ordinal);

        Assert.True(TrackingV2Translator.TryTranslate("egg", Row("""{"level":1,"team":4}"""), out _, out _));
        Assert.True(TrackingV2Translator.TryTranslate("egg", Row("""{"level":5,"team":4}"""), out _, out _));
    }

    [Theory]
    [InlineData("lure", """{"distance":0}""", """{"lure_id":501}""")]
    [InlineData("quest", """{"distance":0}""", """{"reward_type":7}""")]
    [InlineData("gym", """{"distance":0}""", """{"team":4}""")]
    [InlineData("pokemon", """{"distance":0}""", """{"pokemon_id":25}""")]
    public void ARowMissingWhatV2RequiresGoesToV1AndAnOrdinaryOneDoesNot(
        string type, string without, string with)
    {
        Assert.False(TrackingV2Translator.TryTranslate(type, Row(without), out _, out _));
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(with), out _, out var unsupported), unsupported);
    }

    // ──────────────────────────────────────────────────────────────
    // Declining rather than guessing, on the eight types that joined the pilot.
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("raid", """{"pokemon_id":150,"some_field_from_a_newer_poracle":4}""")]
    [InlineData("raid", """{"pokemon_id":150,"team":9}""")]
    [InlineData("raid", """{"pokemon_id":150,"rsvp_changes":7}""")]
    [InlineData("raid", """{"pokemon_id":150,"ping":"<@&400027130022592512>"}""")]
    [InlineData("raid", """{"pokemon_id":150,"clean":9}""")]
    [InlineData("gym", """{"team":4,"slot_changes":2}""")]
    [InlineData("fort", """{"fort_type":"stadium","include_empty":0}""")]
    [InlineData("fort", """{"fort_type":"gym","include_empty":0,"clean":1}""")]
    public void ARowV2CannotCarryFaithfullyDeclines(string type, string row)
    {
        Assert.False(TrackingV2Translator.TryTranslate(type, Row(row), out _, out var unsupported));
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
                Row($$"""{"fort_type":"{{fortType}}","include_empty":0}"""),
                out _,
                out var unsupported),
            unsupported);
    }

    [Fact]
    public void InvasionAndAnythingUnknownHasNoTableAndSaysSo()
    {
        Assert.False(TrackingV2Translator.Handles("invasion"));
        Assert.False(TrackingV2Translator.TryTranslate("invasion", Row("""{"grunt_type":"blanche"}"""), out _, out _));
    }

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Translate(string type, string row)
    {
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(row), out var translated, out var unsupported), unsupported);
        return translated;
    }
}
