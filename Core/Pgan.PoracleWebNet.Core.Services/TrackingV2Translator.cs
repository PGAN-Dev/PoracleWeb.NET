using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Rewrites a v1-shaped tracking row into the body <c>/api/v2</c> will accept, for the ten types
/// PoracleWeb.NET writes through that surface, and (<see cref="ToV1Row"/>) a rule a v2 read returns back
/// into the v1 row, from the same field tables so the two directions cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// The whole of PoracleWeb builds and compares alarm bodies in v1's shape — <see cref="TrackingFieldPreserver"/>
/// and <c>QuickPickService</c> both do. Keeping
/// that shape as the single internal currency and translating once, at the wire, is what stops a v1-shaped
/// row leaking into a v2 request: every <c>V2*Rule</c> sets <c>additionalProperties: false</c>, so one stray
/// <c>ping</c> is a 422 and the write fails outright. Verified live against 5.2.1.
/// </para>
/// <para>
/// <b>One field table per type, never a shared one.</b> <c>V2PokemonRule</c> declares 28 integer filters and
/// <c>V2FortRule</c> declares one; <c>V2FortRule</c> has no <c>clean</c>/<c>edit</c>/<c>summary</c> at all.
/// A shared set would leak a field into a type that refuses it, and <c>additionalProperties: false</c> turns
/// that into a 422.
/// </para>
/// <para>
/// Four kinds of field genuinely change shape. <c>clean</c> is a 3-bit mask on v1 and three booleans on v2;
/// several 0/1 columns (<c>exclusive</c>, <c>slot_changes</c>, <c>battle_changes</c>, <c>gmax</c>,
/// <c>shiny</c>, <c>include_empty</c>) become real booleans; three integer columns become string enums
/// (<c>team</c>, <c>gender</c>, <c>rsvp_changes</c>); and <c>change_types</c> is stored as a JSON string but
/// wanted as an array. Four more — <c>uid</c>, <c>id</c>, <c>profile_no</c>, <c>description</c> — are
/// addressing and presentation rather than filter, and v2 has no place for them.
/// </para>
/// <para>
/// <b>Defaults go out as absent.</b> A property holding the value a v2 read reports as null
/// (<see cref="DefaultsFor"/>) is left out unless the type lists it in <c>Required</c>, because that is what
/// v2 stores an absent property as, and v2 bounds some sentinels (<c>level</c> maximum 90), so echoing
/// <c>level: 9000</c> on a by-boss raid would be a 422. Raid and max battle <c>level</c> is read against
/// <c>pokemon_id</c>: 9000 is always omitted, and 90 when no boss is named.
/// </para>
/// <para>
/// <b>The translator never widens or narrows what PoracleNG will accept.</b> Anything it cannot express
/// faithfully — a property it does not know, an enum outside its range, an egg without a level — makes it
/// answer false with the reason, and the caller refuses the write; dropping the field silently would be #730
/// all over again. The one exception is a property the target server's own
/// <c>/openapi.json</c> declares on the rule: that is sent verbatim, so a field the v2 read copied through
/// (a personal-shiny PoracleNG's <c>personal_shiny</c>) survives the edit that follows.
/// </para>
/// </remarks>
internal static class TrackingV2Translator
{
    /// <summary>
    /// Addressing and presentation. v2 carries none of it in the rule body, and reconstructs all of it
    /// itself: <c>uid</c>/<c>id</c>/<c>profile_no</c> come from the route, and <c>description</c> is a
    /// display string PoracleNG computes rather than a stored column. <c>ping</c> is NOT here — it is a
    /// real column, written whenever it is set, so it is handled in
    /// <see cref="TryWritePing"/>.
    /// </summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "uid", "id", "profile_no", "description",
    };

    /// <summary>v2's gender enum: index is the v1 integer.</summary>
    private static readonly string[] Genders = ["any", "male", "female", "genderless"];

    /// <summary>
    /// v2's invasion gender enum. Three values, not the shared four: <c>V2InvasionRule.gender</c> is
    /// <c>any|male|female</c> with no <c>genderless</c>, so a stored 3 has no v2 representation and the
    /// row is refused rather than being mapped to something that means a different thing.
    /// </summary>
    /// <remarks>
    /// Latent rather than live: production holds 0, 1 and 2 only across 201 invasion rules. But
    /// <c>InvasionCreate.Gender</c> is <c>[Range(0, 3)]</c>, so 3 is expressible here and would be sent.
    /// </remarks>
    private static readonly string[] InvasionGenders = ["any", "male", "female"];

    /// <summary>v2's team enum: index is the v1 integer, and 4 ("any") is PoracleWeb's default.</summary>
    private static readonly string[] Teams = ["harmony", "mystic", "valor", "instinct", "any"];

    /// <summary>v2's RSVP enum: index is the v1 integer.</summary>
    private static readonly string[] RsvpChanges = ["none", "rsvp", "rsvp_only"];

    /// <summary>The project-wide "any" sentinel PoracleNG stores and v2 reports as null (<c>wcAnyID</c>).</summary>
    private const int AnyId = 9000;

    /// <summary>
    /// The raid and max battle level stored on a by-pokemon rule (<c>wcLevelPlaceholder</c>). v2 reports it
    /// as null, and also reports <see cref="AnyTier"/> as null; which one a null means depends on whether
    /// the rule names a pokemon.
    /// </summary>
    private const int LevelPlaceholder = 9000;

    /// <summary>The raid and max battle level stored on a by-level rule with no tier (<c>wcLevelAny</c>).</summary>
    private const int AnyTier = 90;

    /// <summary>The four leagues <c>V2PokemonRule</c> permits. 0 means "no PVP filter".</summary>
    private static readonly HashSet<int> Leagues = [0, 500, 1500, 2500];

    /// <summary>The three fort types <c>V2FortRule</c> permits.</summary>
    private static readonly HashSet<string> FortTypes = new(StringComparer.Ordinal)
    {
        "pokestop", "gym", "everything",
    };

    /// <summary>
    /// Every field each <c>V2*Rule</c> declares, sorted into how it has to be written and read. A schema
    /// field missing from its type's table would be silently unwritable, which is why
    /// <c>TrackingV2TypeTranslationTests</c> asserts the tables against the 5.4.0 schema fixture rather than
    /// against themselves, and <c>TrackingV2ReadTranslationTests</c> asserts every nullable property has a
    /// read default.
    /// </summary>
    private static readonly Dictionary<string, TypeSpec> Specs = new(StringComparer.Ordinal)
    {
        ["pokemon"] = new TypeSpec
        {
            Integers =
            [
                "atk", "costume", "def", "distance", "form", "max_atk", "max_cp", "max_def", "max_iv",
                "max_level", "max_rarity", "max_size", "max_sta", "max_weight", "min_cp", "min_iv",
                "min_level", "min_time", "min_weight", "pokemon_id", "pvp_ranking_best", "pvp_ranking_cap",
                "pvp_ranking_evolution", "pvp_ranking_min_cp", "pvp_ranking_worst", "rarity", "size", "sta",
            ],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["gender"] = Genders },
            BoundedIntegers = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
            {
                ["pvp_ranking_league"] = Leagues,
            },

            // v2 makes pokemon_id the one required field. A row without it could only ever 422.
            Required = ["pokemon_id"],
            PersonalBits = true,

            Defaults = ReadDefaults(
                cleanFlags: true,
                ("pokemon_id", 0), ("form", 0), ("costume", 9000), ("min_iv", -1), ("max_iv", 100), ("min_cp", 0),
                ("max_cp", 9000), ("min_level", 0), ("max_level", 55), ("atk", 0), ("def", 0), ("sta", 0),
                ("max_atk", 15), ("max_def", 15), ("max_sta", 15), ("gender", 0), ("min_weight", 0),
                ("max_weight", 9000000), ("min_time", 0), ("rarity", -1), ("max_rarity", 6), ("size", -1),
                ("max_size", 5), ("pvp_ranking_league", 0), ("pvp_ranking_best", 1), ("pvp_ranking_worst", 4096),
                ("pvp_ranking_min_cp", 0), ("pvp_ranking_cap", 0), ("pvp_ranking_evolution", 0)),
        },
        ["raid"] = new TypeSpec
        {
            Integers = ["costume", "distance", "evolution", "form", "level", "move", "pokemon_id"],
            Strings = ["gym_id"],
            Booleans = ["exclusive"],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["team"] = Teams,
                ["rsvp_changes"] = RsvpChanges,
            },

            Defaults = ReadDefaults(
                cleanFlags: true,
                ("pokemon_id", AnyId), ("form", 0), ("costume", AnyId), ("level", LevelPlaceholder), ("team", 4),
                ("exclusive", 0), ("move", AnyId), ("evolution", AnyId), ("gym_id", null), ("rsvp_changes", 0)),
            LevelFollowsPokemonId = true,
        },
        ["egg"] = new TypeSpec
        {
            Integers = ["distance", "level"],
            Strings = ["gym_id"],
            Booleans = ["exclusive"],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["team"] = Teams,
                ["rsvp_changes"] = RsvpChanges,
            },

            // V2EggRule declares level required with minimum 1, and Egg.Level is a plain int defaulting to
            // 0. A profile import, a quick-pick apply or a cleaning write-back that never set one builds a
            // body v2 answers 422 to — verified live on 5.2.1. Those rows are refused with the reason.
            Required = ["level"],
            PositiveIntegers = ["level"],

            // level is required on V2EggRule and always read back, so it has no default.
            Defaults = ReadDefaults(
                cleanFlags: true,
                ("team", 4), ("exclusive", 0), ("gym_id", null), ("rsvp_changes", 0)),
        },
        ["quest"] = new TypeSpec
        {
            Integers = ["amount", "distance", "form", "reward", "reward_type"],
            Booleans = ["shiny"],
            Required = ["reward_type"],

            Defaults = ReadDefaults(cleanFlags: true, ("reward", 0), ("form", 0), ("shiny", 0), ("amount", 0)),
        },
        ["gym"] = new TypeSpec
        {
            Integers = ["distance"],
            Strings = ["gym_id"],
            Booleans = ["battle_changes", "slot_changes"],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["team"] = Teams },
            Required = ["team"],

            Defaults = ReadDefaults(
                cleanFlags: true,
                ("slot_changes", 0), ("battle_changes", 0), ("gym_id", null)),
        },
        ["maxbattle"] = new TypeSpec
        {
            Integers = ["distance", "evolution", "form", "level", "move", "pokemon_id"],
            Strings = ["station_id"],
            Booleans = ["gmax"],

            Defaults = ReadDefaults(
                cleanFlags: true,
                ("pokemon_id", AnyId), ("form", 0), ("level", LevelPlaceholder), ("move", AnyId), ("gmax", 0),
                ("evolution", AnyId), ("station_id", null)),
            LevelFollowsPokemonId = true,
        },
        ["nest"] = new TypeSpec
        {
            Integers = ["distance", "form", "min_spawn_avg", "pokemon_id"],

            Defaults = ReadDefaults(cleanFlags: true, ("pokemon_id", 0), ("form", 0), ("min_spawn_avg", 0)),
        },
        ["lure"] = new TypeSpec
        {
            Integers = ["distance", "lure_id"],
            Required = ["lure_id"],

            Defaults = ReadDefaults(cleanFlags: true),
        },
        ["invasion"] = new TypeSpec
        {
            Integers = ["distance"],

            // grunt_type is the targeting field and the one every v2 read emits. type_id and grunt_id are
            // input conveniences that resolve to it; this application only ever holds the name, so it
            // sends the name and never the other two. Which names are acceptable is the server's own
            // question: v2 answers 422 naming a grunt_type its masterdata does not list.
            Strings = ["grunt_type"],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["gender"] = InvasionGenders,
            },

            // Verified live, because the schema contradicts itself here: V2InvasionRule.gender says gender
            // is "ONLY valid together with type_id", while grunt_type says it "may be combined with
            // gender". Posting {"grunt_type":"water","gender":"male"} to a running build stores gender 1,
            // so grunt_type's description is the correct one.
            Required = ["grunt_type"],

            // grunt_type is emitted on every read, so it has no default. gender is null both at "any" and
            // on the everything/boss catch-alls, which carry no gender in v2 even where the row stores one.
            Defaults = ReadDefaults(cleanFlags: true, ("gender", 0)),
        },

        ["fort"] = new TypeSpec
        {
            Integers = ["distance"],
            Booleans = ["include_empty"],
            StringArrays = ["change_types"],
            AllowedStrings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["fort_type"] = FortTypes,
            },

            // V2FortRule has no clean/edit/summary — and fort_tracking has no column for them either.
            HasCleanFlags = false,

            // include_empty defaults to TRUE on v2 and FALSE on v1. Verified live: a PUT that omitted it
            // flipped a stored 0 to 1, and the alert text gained "including empty changes". So it is
            // required here even though the schema does not require it — a fort body without one is not
            // something this translator can send faithfully.
            Required = ["include_empty"],

            // Read side: PoracleNG reports include_empty as null when it is TRUE (wcFortIncludeEmpty), so a
            // null here is a stored 1 -- the opposite of v1's write default, and verified on the 5.4.0 bed.
            // fort_type's wildcard is the "everything" catch-all, and change_types "[]" means any change.
            Defaults = ReadDefaults(
                cleanFlags: false,
                ("fort_type", "everything"), ("include_empty", 1), ("change_types", "[]")),
        },
    };

    /// <summary>
    /// Types PoracleWeb.NET reads through v2 but never writes through <see cref="TryTranslate"/>.
    /// <c>incident</c> has its own proxy (<c>PoracleIncidentProxy</c>) for writes, so it must not make
    /// <see cref="Handles"/> answer true.
    /// </summary>
    private static readonly Dictionary<string, TypeSpec> ReadOnlySpecs = new(StringComparer.Ordinal)
    {
        ["incident"] = new TypeSpec
        {
            Integers = ["display_type", "distance"],
            Required = ["display_type"],
            Defaults = ReadDefaults(cleanFlags: true),
        },
    };

    private const string PersonalShinyField = "personal_shiny";
    private const string PersonalBackgroundField = "personal_background";

    /// <summary>The tracking types this build has a v2 field table for.</summary>
    public static bool Handles(string type) => Specs.ContainsKey(type);

    /// <summary>
    /// The v1 value each nullable property of a type's <c>V2*Rule</c> stands for when a v2 read reports it
    /// as null, keyed by the v2 property name. <c>clean</c>, <c>edit</c> and <c>summary</c> map to
    /// <c>false</c>: they fold into the one v1 <c>clean</c> mask, where false contributes no bit.
    /// </summary>
    /// <remarks>
    /// Taken from PoracleNG's own wildcard constants (<c>processor/internal/api/v2_wildcards.go</c>) and its
    /// per-type <c>*RowToRule</c> read projections, not from the <c>*Create</c> initialisers: a wrong value
    /// here is read, carried forward by <see cref="TrackingFieldPreserver"/>, and written back on the next
    /// edit. Raid and max battle <c>level</c> is listed as the by-pokemon placeholder;
    /// <see cref="ToV1Row"/> reads a null level on a by-level rule as the any-tier wildcard instead.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The type has no v2 table.</exception>
    internal static IReadOnlyDictionary<string, JsonElement> DefaultsFor(string type) => ReadSpec(type).Defaults;

    /// <summary>
    /// Turns one rule from a v2 read back into the v1 row every caller in PoracleWeb.NET consumes: enum
    /// names to their integers, booleans to 0/1 columns, <c>clean</c>/<c>edit</c>/<c>summary</c> to the
    /// bitmask, <c>change_types</c> to its JSON string, and every null (or absent) filter to the value
    /// PoracleNG stores for it. <c>uid</c>, <c>profile_no</c> and <c>description</c> are kept when present,
    /// and <c>id</c> is <paramref name="humanId"/>.
    /// </summary>
    /// <remarks>
    /// A property this build does not model is copied through verbatim, never dropped: anything a newer
    /// PoracleNG adds has to survive the read so the edit that follows can carry them. A personal-shiny PoracleNG's
    /// <c>personal_shiny</c>/<c>personal_background</c> are modelled on pokemon: they fold into the v1
    /// <c>personal</c> bitmask, set only when a bit is.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The type has no v2 table, or a modelled property holds a value this build cannot read: an enum name
    /// it does not know, most likely from a newer PoracleNG. Guessing an integer for it would be written
    /// back as the user's filter.
    /// </exception>
    internal static JsonElement ToV1Row(string type, JsonElement v2Rule, string humanId)
    {
        var spec = ReadSpec(type);

        if (v2Rule.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"A v2 {type} rule must be a JSON object, not {v2Rule.ValueKind}.");
        }

        var row = new JsonObject();
        var mask = 0;
        var personal = 0;

        foreach (var property in v2Rule.EnumerateObject())
        {
            var name = property.Name;

            switch (name)
            {
                case "id":
                    // Never sent by v2; the row's owner is the human the read was for.
                    continue;

                case "clean" when spec.HasCleanFlags:
                    mask |= ReadFlag(type, property, CleanFlags.AutoDelete);
                    continue;

                case "edit" when spec.HasCleanFlags:
                    mask |= ReadFlag(type, property, CleanFlags.Edit);
                    continue;

                case "summary" when spec.HasCleanFlags:
                    mask |= ReadFlag(type, property, CleanFlags.Summary);
                    continue;

                // Personal shiny (jfberry/PoracleNG#255): two booleans on v2, the one
                // personal bitmask everywhere in PoracleWeb.NET.
                case PersonalShinyField when spec.PersonalBits:
                    personal |= ReadFlag(type, property, PersonalFlags.Shiny);
                    continue;

                case PersonalBackgroundField when spec.PersonalBits:
                    personal |= ReadFlag(type, property, PersonalFlags.Background);
                    continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null && spec.Defaults.TryGetValue(name, out var fallback))
            {
                row[name] = JsonNode.Parse(fallback.GetRawText());
                continue;
            }

            row[name] = ReadValue(type, spec, property);
        }

        foreach (var (name, fallback) in spec.Defaults)
        {
            if (name is "clean" or "edit" or "summary" || row.ContainsKey(name))
            {
                continue;
            }

            row[name] = JsonNode.Parse(fallback.GetRawText());
        }

        if (spec.LevelFollowsPokemonId && IsNullOrAbsent(v2Rule, "level") && IsNullOrAbsent(v2Rule, "pokemon_id"))
        {
            // v2 nulls both 9000 (by-pokemon placeholder) and 90 (by-level, any tier). With no pokemon named
            // the rule is by level, so the null is the any-tier wildcard. PoracleNG's raidLevelOrNull.
            row["level"] = AnyTier;
        }

        if (spec.HasCleanFlags)
        {
            row["clean"] = mask;
        }

        if (personal != 0)
        {
            // Only when set: a pokemon rule without the flags reads exactly as it does on a public server.
            row["personal"] = personal;
        }

        row["id"] = humanId;

        return JsonSerializer.SerializeToElement(row);
    }

    private static TypeSpec ReadSpec(string type) =>
        Specs.TryGetValue(type, out var spec) || ReadOnlySpecs.TryGetValue(type, out spec)
            ? spec
            : throw new InvalidOperationException($"No v2 field table for tracking type '{type}'.");

    private static bool IsNullOrAbsent(JsonElement rule, string name) =>
        !rule.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static int ReadFlag(string type, JsonProperty property, int bit) =>
        property.Value.ValueKind switch
        {
            JsonValueKind.True => bit,
            JsonValueKind.False or JsonValueKind.Null => 0,
            _ => throw new InvalidOperationException(
                $"v2 {type} rule: {property.Name} is {property.Value.ValueKind}, not a boolean."),
        };

    /// <summary>One property of a v2 rule that is not at its default, in its v1 shape. The inverse of <see cref="TryWriteProperty"/>.</summary>
    private static JsonNode? ReadValue(string type, TypeSpec spec, JsonProperty property)
    {
        var name = property.Name;
        var value = property.Value;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (spec.IntEnums.TryGetValue(name, out var names))
        {
            var index = value.ValueKind == JsonValueKind.String ? Array.IndexOf(names, value.GetString()) : -1;
            return index >= 0
                ? index
                : throw new InvalidOperationException(
                    $"v2 {type} rule: {name} is {value.GetRawText()}, not one of {string.Join(", ", names)}.");
        }

        if (spec.AllowedStrings.TryGetValue(name, out var allowed))
        {
            return value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()!)
                ? value.GetString()
                : throw new InvalidOperationException(
                    $"v2 {type} rule: {name} is {value.GetRawText()}, not one of {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
        }

        if (spec.Booleans.Contains(name))
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => throw new InvalidOperationException($"v2 {type} rule: {name} is {value.ValueKind}, not a boolean."),
            };
        }

        if (spec.StringArrays.Contains(name))
        {
            // v1 holds the column as a JSON string, written compactly the way PoracleNG's json.Marshal does.
            return value.ValueKind switch
            {
                JsonValueKind.Array => JsonNode.Parse(value.GetRawText())!.ToJsonString(),
                JsonValueKind.String => value.GetString(),
                _ => throw new InvalidOperationException($"v2 {type} rule: {name} is {value.ValueKind}, not an array."),
            };
        }

        // Integers, free strings, override_areas, uid/profile_no/description, and anything this build does
        // not model: verbatim.
        return JsonNode.Parse(value.GetRawText());
    }

    private static Dictionary<string, JsonElement> ReadDefaults(bool cleanFlags, params (string Name, object? Value)[] entries)
    {
        var defaults = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            // Every V2*Rule declares these. v2 reports an empty string as null, and an empty area list as null.
            ["distance"] = JsonSerializer.SerializeToElement(0),
            ["template"] = JsonSerializer.SerializeToElement(string.Empty),
            ["ping"] = JsonSerializer.SerializeToElement(string.Empty),
            ["override_location_label"] = JsonSerializer.SerializeToElement(string.Empty),
            ["override_areas"] = JsonSerializer.SerializeToElement<object?>(null),
        };

        if (cleanFlags)
        {
            defaults["clean"] = JsonSerializer.SerializeToElement(false);
            defaults["edit"] = JsonSerializer.SerializeToElement(false);
            defaults["summary"] = JsonSerializer.SerializeToElement(false);
        }

        foreach (var (name, value) in entries)
        {
            defaults[name] = JsonSerializer.SerializeToElement(value);
        }

        return defaults;
    }

    /// <summary>
    /// Translates one v1-shaped row. Returns false — leaving <paramref name="translated"/> untouched —
    /// when the row carries something v2 cannot be told faithfully, or the type has no v2 table.
    /// </summary>
    /// <param name="type">The tracking type, as PoracleNG names it in the route.</param>
    /// <param name="row">A single v1-shaped alarm object, as every alarm service already builds.</param>
    /// <param name="translated">The v2 body on success.</param>
    /// <param name="unsupported">What stopped the translation, for the log. Null on success.</param>
    public static bool TryTranslate(
        string type,
        JsonElement row,
        IReadOnlyDictionary<string, Bound>? serverBounds,
        out JsonElement translated,
        out string? unsupported) =>
        TryTranslate(type, row, serverBounds, serverDeclared: null, out translated, out unsupported);

    /// <summary>
    /// <see cref="TryTranslate(string, JsonElement, IReadOnlyDictionary{string, Bound}?, out JsonElement, out string?)"/>,
    /// carrying through any property this build does not model that the server's own <c>V2*Rule</c> for
    /// <paramref name="type"/> declares.
    /// </summary>
    /// <param name="serverDeclared">
    /// Every property name the target server's <c>/openapi.json</c> declares on this type's rule schema, or
    /// null when that could not be read. A property outside the field table is sent verbatim only when it is
    /// in this set: that is how a personal-shiny PoracleNG's <c>personal_shiny</c>/<c>personal_background</c>, which the
    /// v2 read copies through, survive an edit. Undeclared, it would be a 422 under
    /// <c>additionalProperties: false</c>, so the row still declines.
    /// </param>
    public static bool TryTranslate(
        string type,
        JsonElement row,
        IReadOnlyDictionary<string, Bound>? serverBounds,
        IReadOnlySet<string>? serverDeclared,
        out JsonElement translated,
        out string? unsupported)
    {
        translated = default;
        unsupported = null;

        if (!Specs.TryGetValue(type, out var spec))
        {
            unsupported = $"no v2 field table for {type}";
            return false;
        }

        if (row.ValueKind != JsonValueKind.Object)
        {
            unsupported = "the body is not a single rule object";
            return false;
        }

        if (!SatisfiesRequired(spec, row, out unsupported))
        {
            return false;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var property in row.EnumerateObject())
            {
                if (Dropped.Contains(property.Name))
                {
                    continue;
                }

                if (IsDefault(spec, row, property))
                {
                    continue;
                }

                if (!TryWriteProperty(writer, spec, serverBounds, serverDeclared, property, out unsupported))
                {
                    return false;
                }
            }

            writer.WriteEndObject();
        }

        translated = JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
        return true;
    }

    /// <summary>
    /// Whether a property holds exactly the value a v2 read reports as null, so leaving it out stores the same
    /// thing. A <c>Required</c> field is never omitted. Raid and max battle <c>level</c> follows the rule's
    /// pokemon: 9000 is the by-boss placeholder v2 stores itself and bounds out (maximum 90), and 90 is the
    /// any-tier wildcard when no boss is named.
    /// </summary>
    private static bool IsDefault(TypeSpec spec, JsonElement row, JsonProperty property)
    {
        // template is never omitted. Its read default "" cannot tell an empty template from the server's own
        // default name: omitting it makes PoracleNG store that name ("1"), a v2 read reports it back as null,
        // and ToV1Row turns that into "". The v1 upsert paths (bulk distance, cleaning) then send "" against
        // a stored "1", PoracleNG counts template plus distance/clean as two updatable differences and INSERTS
        // a second rule instead of updating. Sending the string we hold keeps the round trip matching. A null
        // template means "not supplied" and is still omitted.
        if ((property.Name == "template" && property.Value.ValueKind == JsonValueKind.String)
            || Array.IndexOf(spec.Required, property.Name) >= 0)
        {
            return false;
        }

        if (spec.LevelFollowsPokemonId
            && property.Name == "level"
            && property.Value.ValueKind == JsonValueKind.Number
            && property.Value.TryGetInt32(out var level))
        {
            if (level == LevelPlaceholder)
            {
                return true;
            }

            return level == AnyTier
                && (!row.TryGetProperty("pokemon_id", out var boss)
                    || boss.ValueKind == JsonValueKind.Null
                    || (boss.ValueKind == JsonValueKind.Number && boss.TryGetInt32(out var id) && id == AnyId));
        }

        return spec.Defaults.TryGetValue(property.Name, out var fallback) && SameValue(fallback, property.Value);
    }

    /// <summary>
    /// Compares a stored v1 value to a read default the way v2 does: numbers as numbers (a 0/1 column and a
    /// boolean are the same flag), and an empty string and null as one.
    /// </summary>
    private static bool SameValue(JsonElement fallback, JsonElement value)
    {
        if (TryAsNumber(fallback, out var left) && TryAsNumber(value, out var right))
        {
            return left == right;
        }

        if (IsEmptyText(fallback) && IsEmptyText(value))
        {
            return true;
        }

        // change_types: an empty list is "[]" in v1's column and [] in a model.
        if (fallback.ValueKind == JsonValueKind.String && fallback.GetString() == "[]" && value.ValueKind == JsonValueKind.Array)
        {
            return value.GetArrayLength() == 0;
        }

        return fallback.ValueKind == JsonValueKind.String
            && value.ValueKind == JsonValueKind.String
            && string.Equals(fallback.GetString(), value.GetString(), StringComparison.Ordinal);
    }

    private static bool IsEmptyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString()));

    private static bool TryAsNumber(JsonElement value, out double number)
    {
        number = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDouble(out number);
            case JsonValueKind.True:
                number = 1;
                return true;
            case JsonValueKind.False:
                return true;
            default:
                return false;
        }
    }

    private static bool SatisfiesRequired(TypeSpec spec, JsonElement row, out string? unsupported)
    {
        unsupported = null;

        foreach (var required in spec.Required)
        {
            if (!row.TryGetProperty(required, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                unsupported = $"{required} is missing, and v2 requires it";
                return false;
            }

            if (spec.PositiveIntegers.Contains(required)
                && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 1))
            {
                unsupported = $"{required} must be at least 1 on v2";
                return false;
            }
        }

        return true;
    }

    private static bool TryWriteProperty(
        Utf8JsonWriter writer,
        TypeSpec spec,
        IReadOnlyDictionary<string, Bound>? serverBounds,
        IReadOnlySet<string>? serverDeclared,
        JsonProperty property,
        out string? unsupported)
    {
        unsupported = null;

        switch (property.Name)
        {
            case "personal":
                return TryWritePersonal(writer, spec, property.Value, DeclaresPersonal(serverDeclared), out unsupported);

            case "clean":
                return spec.HasCleanFlags
                    ? TryWriteClean(writer, property.Value, out unsupported)
                    : TryWriteAbsentClean(property.Value, out unsupported);

            case "ping":
                return TryWritePing(writer, property.Value, out unsupported);

            case "override_areas":
                return TryWriteArrayLike(writer, "override_areas", property.Value, out unsupported);

            case "template":
            case "override_location_label":
                return TryWriteString(writer, property, out unsupported);
        }

        if (spec.IntEnums.TryGetValue(property.Name, out var names))
        {
            return TryWriteIntEnum(writer, property, names, out unsupported);
        }

        if (spec.BoundedIntegers.TryGetValue(property.Name, out var allowedNumbers))
        {
            return TryWriteBoundedInteger(writer, property, allowedNumbers, out unsupported);
        }

        if (spec.AllowedStrings.TryGetValue(property.Name, out var allowedStrings))
        {
            return TryWriteAllowedString(writer, property, allowedStrings, out unsupported);
        }

        if (spec.Booleans.Contains(property.Name))
        {
            return TryWriteBoolean(writer, property, out unsupported);
        }

        if (spec.StringArrays.Contains(property.Name))
        {
            return TryWriteArrayLike(writer, property.Name, property.Value, out unsupported);
        }

        if (spec.Strings.Contains(property.Name))
        {
            return TryWriteString(writer, property, out unsupported);
        }

        if (!spec.Integers.Contains(property.Name))
        {
            if (serverDeclared is not null && serverDeclared.Contains(property.Name))
            {
                // Not modelled here, but this server's own rule schema declares it: the read copied it
                // through in the server's shape, so it goes back the same way. A personal-shiny PoracleNG's
                // personal_shiny/personal_background, and whatever a newer PoracleNG adds.
                property.WriteTo(writer);
                return true;
            }

            // A field this build has never heard of and the server does not declare. Newer PoracleNG,
            // older PoracleWeb — refuse the row rather than dropping the
            // user's value.
            unsupported = $"unknown property {property.Name}";
            return false;
        }

        if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            unsupported = $"{property.Name} is not a number";
            return false;
        }

        if (property.Value.ValueKind == JsonValueKind.Number
            && property.Value.TryGetInt32(out var stored)
            && serverBounds is not null
            && serverBounds.TryGetValue(property.Name, out var bound)
            && !bound.Contains(stored))
        {
            // A value equal to the read default never gets here (IsDefault omits it). Anywhere else,
            // echoing it is a 422 and omitting it silently edits the user's filter, so it is refused.
            unsupported = $"{property.Name} is {stored}, outside v2's {bound}";
            return false;
        }

        property.WriteTo(writer);
        return true;
    }

    private static bool TryWriteString(Utf8JsonWriter writer, JsonProperty property, out string? unsupported)
    {
        unsupported = null;

        if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            unsupported = $"{property.Name} is not a string";
            return false;
        }

        property.WriteTo(writer);
        return true;
    }

    private static bool TryWriteAllowedString(
        Utf8JsonWriter writer, JsonProperty property, HashSet<string> allowed, out string? unsupported)
    {
        unsupported = null;

        if (property.Value.ValueKind == JsonValueKind.Null)
        {
            property.WriteTo(writer);
            return true;
        }

        if (property.Value.ValueKind != JsonValueKind.String
            || property.Value.GetString() is not { } value
            || !allowed.Contains(value))
        {
            unsupported = $"{property.Name} is not one of {string.Join(", ", allowed.Order(StringComparer.Ordinal))}";
            return false;
        }

        property.WriteTo(writer);
        return true;
    }

    private static bool TryWriteBoundedInteger(
        Utf8JsonWriter writer, JsonProperty property, HashSet<int> allowed, out string? unsupported)
    {
        unsupported = null;

        if (property.Value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value))
        {
            unsupported = $"{property.Name} is not a number";
            return false;
        }

        if (!allowed.Contains(value))
        {
            unsupported =
                $"{property.Name} {value} is not one of {string.Join(", ", allowed.Order())}";
            return false;
        }

        writer.WriteNumber(property.Name, value);
        return true;
    }

    /// <summary>A v1 0/1 column that v2 declares as a real boolean.</summary>
    private static bool TryWriteBoolean(Utf8JsonWriter writer, JsonProperty property, out string? unsupported)
    {
        unsupported = null;

        switch (property.Value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.True:
            case JsonValueKind.False:
                property.WriteTo(writer);
                return true;

            case JsonValueKind.Number when property.Value.TryGetInt32(out var flag) && flag is 0 or 1:
                writer.WriteBoolean(property.Name, flag == 1);
                return true;

            default:
                unsupported = $"{property.Name} is not a 0/1 flag";
                return false;
        }
    }

    private static bool TryWriteIntEnum(
        Utf8JsonWriter writer, JsonProperty property, string[] names, out string? unsupported)
    {
        unsupported = null;

        if (property.Value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        // Already a v2 enum string — reached when a stored row came back from a v2 read rather than a v1 one.
        if (property.Value.ValueKind == JsonValueKind.String)
        {
            var existing = property.Value.GetString();
            if (existing is not null && Array.IndexOf(names, existing) >= 0)
            {
                property.WriteTo(writer);
                return true;
            }

            unsupported = $"{property.Name} is not one of {string.Join(", ", names)}";
            return false;
        }

        if (property.Value.ValueKind != JsonValueKind.Number
            || !property.Value.TryGetInt32(out var index)
            || index < 0
            || index >= names.Length)
        {
            unsupported = $"{property.Name} is outside 0-{names.Length - 1}";
            return false;
        }

        writer.WriteString(property.Name, names[index]);
        return true;
    }

    /// <summary>The 3-bit mask becomes three booleans. Bits outside the three known ones are not v2's.</summary>
    private static bool TryWriteClean(Utf8JsonWriter writer, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var mask))
        {
            unsupported = "clean is not a bitmask";
            return false;
        }

        if ((mask & ~CleanFlags.All) != 0)
        {
            // A bit PoracleWeb does not model. v2 has no field for it, so translating would drop it;
            // v1 stores the integer as-is. See the clean-bitmask note in CLAUDE.md.
            unsupported = $"clean carries bits outside the three known flags ({mask})";
            return false;
        }

        writer.WriteBoolean("clean", CleanFlags.IsAutoDelete(mask));
        writer.WriteBoolean("edit", CleanFlags.IsEdit(mask));
        writer.WriteBoolean("summary", CleanFlags.IsSummary(mask));
        return true;
    }

    /// <summary>
    /// <c>fort</c> is the one type with no <c>clean</c> anywhere — no v2 field and no column. A zero is
    /// nothing to carry and is dropped; anything else is a value v2 could not be told, so the row is
    /// refused.
    /// </summary>
    private static bool TryWriteAbsentClean(JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind == JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var mask) && mask == 0))
        {
            return true;
        }

        unsupported = "clean is set on a type v2 has no clean field for";
        return false;
    }

    /// <summary>
    /// <c>ping</c> is the mention prepended to the DM, a role or user the alert is meant to notify.
    /// PoracleNG 5.4.0 declares it on every <c>V2*Rule</c> and stores it, so a set string is written
    /// verbatim. An empty one is dropped: v2 would store the same empty string either way.
    /// </summary>
    private static bool TryWritePing(Utf8JsonWriter writer, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind is JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString())))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            writer.WriteString("ping", value.GetString());
            return true;
        }

        unsupported = "ping is not a string";
        return false;
    }

    /// <summary>
    /// Whether the target server's <c>V2PokemonRule</c> declares both personal booleans. Both, not either:
    /// they arrive together upstream, and a server with one is not one this build knows how to talk to.
    /// </summary>
    private static bool DeclaresPersonal(IReadOnlySet<string>? serverDeclared) =>
        serverDeclared is not null
        && serverDeclared.Contains(PersonalShinyField)
        && serverDeclared.Contains(PersonalBackgroundField);

    /// <summary>
    /// <c>personal</c> is a bitmask on v1 (jfberry/PoracleNG#255) and two booleans on v2,
    /// <c>personal_shiny</c> and <c>personal_background</c>. <see cref="Models.Monster.Personal"/> always
    /// serialises, so 0 -- what almost every rule holds -- must write nothing and cost nothing: v2 reads an
    /// omitted flag as clear, which is exactly what 0 says.
    /// </summary>
    /// <remarks>
    /// Anything v2 cannot say refuses the row rather than being dropped: a reserved bit (4, 8) has no v2
    /// field, and a set flag on a server whose <c>/openapi.json</c> does not declare both booleans would be a
    /// 422. There is no v1 path to fall back to any more. The read side is <see cref="ToV1Row"/>, which folds
    /// the two booleans back into this bitmask.
    /// </remarks>
    private static bool TryWritePersonal(
        Utf8JsonWriter writer, TypeSpec spec, JsonElement value, bool personalSupported, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var bits))
        {
            unsupported = "personal is not an integer";
            return false;
        }

        if (bits == 0)
        {
            return true;
        }

        if (!spec.PersonalBits)
        {
            unsupported = "personal is not a field of this type";
            return false;
        }

        if (PersonalFlags.HasUnknownBits(bits))
        {
            unsupported = "personal carries a bit v2 cannot express";
            return false;
        }

        if (!personalSupported)
        {
            unsupported = "personal is set, and this server's v2 does not know it";
            return false;
        }

        if (PersonalFlags.IsShiny(bits))
        {
            writer.WriteBoolean("personal_shiny", true);
        }

        if (PersonalFlags.IsBackground(bits))
        {
            writer.WriteBoolean("personal_background", true);
        }

        return true;
    }

    /// <summary>
    /// <c>override_areas</c> and <c>change_types</c> arrive as real arrays from the models, but a stored row
    /// carried forward by <see cref="TrackingFieldPreserver"/> holds the column verbatim, and PoracleNG's
    /// columns are JSON strings. Normalise rather than refuse — this is PoracleWeb's own read that produced
    /// the string.
    /// </summary>
    private static bool TryWriteArrayLike(
        Utf8JsonWriter writer, string name, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Array:
                writer.WritePropertyName(name);
                value.WriteTo(writer);
                return true;

            case JsonValueKind.String:
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    writer.WriteNull(name);
                    return true;
                }

                try
                {
                    using var parsed = JsonDocument.Parse(raw);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        unsupported = $"{name} is a string that is not a JSON array";
                        return false;
                    }

                    writer.WritePropertyName(name);
                    parsed.RootElement.WriteTo(writer);
                    return true;
                }
                catch (JsonException)
                {
                    unsupported = $"{name} is a string that is not JSON";
                    return false;
                }

            default:
                unsupported = $"{name} is neither an array nor null";
                return false;
        }
    }

    /// <summary>How one type's v1 columns map onto its <c>V2*Rule</c>.</summary>
    /// <summary>One field's v2 schema range. A null end is unbounded in that direction.</summary>
    internal readonly record struct Bound(int? Min, int? Max)
    {
        public bool Contains(int value) => (this.Min is null || value >= this.Min) && (this.Max is null || value <= this.Max);

        public override string ToString() =>
            (this.Min, this.Max) switch
            {
                (null, null) => "unbounded",
                (not null, null) => $"minimum {this.Min}",
                (null, not null) => $"maximum {this.Max}",
                _ => $"{this.Min}-{this.Max}",
            };
    }

    private sealed record TypeSpec
    {
        /// <summary>Names v2 declares as integers, written through unchanged.</summary>
        public required HashSet<string> Integers { get; init; }

        /// <summary>
        /// Names accepted as free strings. <c>template</c> and <c>override_location_label</c> are implicit,
        /// because every <c>V2*Rule</c> declares both.
        /// </summary>
        public HashSet<string> Strings { get; init; } = new(StringComparer.Ordinal);

        /// <summary>v1 0/1 columns v2 declares as booleans.</summary>
        public HashSet<string> Booleans { get; init; } = new(StringComparer.Ordinal);

        /// <summary>v1 integer columns v2 declares as string enums, the array indexed by the v1 value.</summary>
        public Dictionary<string, string[]> IntEnums { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Integer columns v2 restricts to a fixed set of values.</summary>
        public Dictionary<string, HashSet<int>> BoundedIntegers { get; init; } = new(StringComparer.Ordinal);

        /// <summary>v1 string columns v2 restricts to a fixed set.</summary>
        public Dictionary<string, HashSet<string>> AllowedStrings { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Columns stored as a JSON string that v2 declares as an array.</summary>
        public HashSet<string> StringArrays { get; init; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether this type has <c>personal_shiny</c> and <c>personal_background</c>. Pokemon only; the
        /// <c>personal</c> column does not exist on the other tracking tables.
        /// </summary>
        public bool PersonalBits { get; init; }

        /// <summary>Whether this type has <c>clean</c>/<c>edit</c>/<c>summary</c> at all. Fort does not.</summary>
        public bool HasCleanFlags { get; init; } = true;

        /// <summary>Fields that must be present and non-null, or the row is refused.</summary>
        public string[] Required { get; init; } = [];

        /// <summary>Required fields v2 also constrains to 1 or more.</summary>
        public HashSet<string> PositiveIntegers { get; init; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Read side: the v1 value a null (or absent) property stands for, keyed by the v2 name. See
        /// <see cref="DefaultsFor"/>.
        /// </summary>
        public required Dictionary<string, JsonElement> Defaults { get; init; }

        /// <summary>
        /// Raid and max battle: a null <c>level</c> is the by-pokemon placeholder when the rule names a
        /// pokemon, and the any-tier wildcard when it does not.
        /// </summary>
        public bool LevelFollowsPokemonId { get; init; }
    }
}
