using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Rewrites a v1-shaped tracking row into the body <c>/api/v2</c> will accept, for the nine types
/// PoracleWeb writes through that surface.
/// </summary>
/// <remarks>
/// <para>
/// The whole of PoracleWeb builds and compares alarm bodies in v1's shape — <see cref="TrackingFieldPreserver"/>,
/// <see cref="TrackingUpdateReconciler"/>, <c>BulkUidRemap</c> and <c>QuickPickService</c> all do. Keeping
/// that shape as the single internal currency and translating once, at the wire, is what stops a v1-shaped
/// row leaking into a v2 request: every <c>V2*Rule</c> sets <c>additionalProperties: false</c>, so one stray
/// <c>ping</c> is a 422 and the write fails outright. Verified live against 5.2.1.
/// </para>
/// <para>
/// <b>One field table per type, never a shared one.</b> <c>V2PokemonRule</c> declares 28 integer filters and
/// <c>V2FortRule</c> declares one; <c>V2FortRule</c> has no <c>clean</c>/<c>edit</c>/<c>summary</c> at all.
/// A shared set would leak a field into a type that refuses it, and <c>additionalProperties: false</c> turns
/// that into a 422 the fallback silently papers over — the failure nobody notices.
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
/// <b>Sentinels are sent verbatim.</b> The migration guide says to omit them, but a v2 write carrying
/// <c>level: 9000</c>, <c>costume: 9000</c>, <c>evolution: 9000</c> or <c>move: 9000</c> stores exactly what
/// v1 stores — verified on 5.2.1 by writing a raid, an egg and a max battle through both surfaces and
/// diffing the v1 read, which came back byte-identical but for the rotated uid. The v2 <em>response</em>
/// reports them as null, which is what the guide describes; the row does not. Omitting them instead would
/// leave <see cref="TrackingUpdateReconciler"/> comparing a stored 9000 against an absent field.
/// </para>
/// <para>
/// <b>The translator never widens or narrows what PoracleNG will accept.</b> Anything it cannot express
/// faithfully — a property it does not know, an enum outside its range, an egg without a level — makes it
/// answer false, and the caller sends the row to the frozen v1 surface instead. Refusing outright would
/// mean a PoracleNG newer than this translator broke every edit; dropping the field silently would be #730
/// all over again. Falling back does neither.
/// </para>
/// </remarks>
internal static class TrackingV2Translator
{
    /// <summary>
    /// Addressing and presentation. v2 carries none of it in the rule body, and reconstructs all of it
    /// itself: <c>uid</c>/<c>id</c>/<c>profile_no</c> come from the route, and <c>description</c> is a
    /// display string PoracleNG computes rather than a stored column. <c>ping</c> is NOT here — it is a
    /// real column that v2 blanks, so it is handled in <see cref="TryWritePing"/>.
    /// </summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "uid", "id", "profile_no", "description",
    };

    /// <summary>v2's gender enum: index is the v1 integer.</summary>
    private static readonly string[] Genders = ["any", "male", "female", "genderless"];

    /// <summary>v2's team enum: index is the v1 integer, and 4 ("any") is PoracleWeb's default.</summary>
    private static readonly string[] Teams = ["harmony", "mystic", "valor", "instinct", "any"];

    /// <summary>v2's RSVP enum: index is the v1 integer.</summary>
    private static readonly string[] RsvpChanges = ["none", "rsvp", "rsvp_only"];

    /// <summary>The four leagues <c>V2PokemonRule</c> permits. 0 means "no PVP filter".</summary>
    private static readonly HashSet<int> Leagues = [0, 500, 1500, 2500];

    /// <summary>The three fort types <c>V2FortRule</c> permits.</summary>
    private static readonly HashSet<string> FortTypes = new(StringComparer.Ordinal)
    {
        "pokestop", "gym", "everything",
    };

    /// <summary>
    /// Every field each <c>V2*Rule</c> declares, taken from 5.2.1's <c>openapi.golden.json</c>, sorted into
    /// how it has to be written. A schema field missing from its type's table would go to v1 forever
    /// without anyone noticing, which is why <c>TrackingV2TypeTranslationTests</c> asserts the tables against
    /// the schema rather than against themselves.
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
            // body v2 answers 422 to — verified live on 5.2.1. Those rows go to v1, which stores level 0
            // as it always has.
            Required = ["level"],
            PositiveIntegers = ["level"],
        },
        ["quest"] = new TypeSpec
        {
            Integers = ["amount", "distance", "form", "reward", "reward_type"],
            Booleans = ["shiny"],
            Required = ["reward_type"],
        },
        ["gym"] = new TypeSpec
        {
            Integers = ["distance"],
            Strings = ["gym_id"],
            Booleans = ["battle_changes", "slot_changes"],
            IntEnums = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["team"] = Teams },
            Required = ["team"],
        },
        ["maxbattle"] = new TypeSpec
        {
            Integers = ["distance", "evolution", "form", "level", "move", "pokemon_id"],
            Strings = ["station_id"],
            Booleans = ["gmax"],
        },
        ["nest"] = new TypeSpec
        {
            Integers = ["distance", "form", "min_spawn_avg", "pokemon_id"],
        },
        ["lure"] = new TypeSpec
        {
            Integers = ["distance", "lure_id"],
            Required = ["lure_id"],
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
        },
    };

    /// <summary>The tracking types this build has a v2 field table for.</summary>
    public static bool Handles(string type) => Specs.ContainsKey(type);

    /// <summary>
    /// Translates one v1-shaped row. Returns false — leaving <paramref name="translated"/> untouched —
    /// when the row carries something v2 cannot be told faithfully, or the type has no v2 table.
    /// </summary>
    /// <param name="type">The tracking type, as PoracleNG names it in the route.</param>
    /// <param name="row">A single v1-shaped alarm object, as every alarm service already builds.</param>
    /// <param name="translated">The v2 body on success.</param>
    /// <param name="unsupported">What stopped the translation, for the log. Null on success.</param>
    public static bool TryTranslate(string type, JsonElement row, out JsonElement translated, out string? unsupported)
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

                if (!TryWriteProperty(writer, spec, property, out unsupported))
                {
                    return false;
                }
            }

            writer.WriteEndObject();
        }

        translated = JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
        return true;
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
        Utf8JsonWriter writer, TypeSpec spec, JsonProperty property, out string? unsupported)
    {
        unsupported = null;

        switch (property.Name)
        {
            case "clean":
                return spec.HasCleanFlags
                    ? TryWriteClean(writer, property.Value, out unsupported)
                    : TryWriteAbsentClean(property.Value, out unsupported);

            case "ping":
                return TryWritePing(property.Value, out unsupported);

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
            // A field this build has never heard of. Newer PoracleNG, older PoracleWeb — send the
            // row to v1, which takes anything, rather than dropping the user's value.
            unsupported = $"unknown property {property.Name}";
            return false;
        }

        if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            unsupported = $"{property.Name} is not a number";
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
    /// nothing to carry and is dropped; anything else is a value v2 could not be told, so the row goes
    /// to v1.
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
    /// <c>ping</c> is the mention prepended to the DM — a role or user the alert is meant to notify. It is
    /// a real tracking column on every type and the v1 body carries it, but no <c>V2*Rule</c> has a field
    /// for it and the handlers store <c>Ping: ""</c> unconditionally ("server-managed"). Verified live
    /// against 5.2.1: a rule holding <c>&lt;@&amp;400027130022592512&gt;</c> came back with an empty ping
    /// after one v2 PUT.
    /// </summary>
    /// <remarks>
    /// So an empty ping is dropped — v2 would store the same empty string — but a set one sends the row to
    /// v1, which keeps it. Silently discarding it here is exactly the #730 shape this translator exists to
    /// avoid, and it lands on webhook alarms, where the role mention is the entire point of the alert.
    /// </remarks>
    private static bool TryWritePing(JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind is JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString())))
        {
            return true;
        }

        unsupported = "ping is set, and v2 would blank it";
        return false;
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

        /// <summary>Whether this type has <c>clean</c>/<c>edit</c>/<c>summary</c> at all. Fort does not.</summary>
        public bool HasCleanFlags { get; init; } = true;

        /// <summary>Fields that must be present and non-null, or the row goes to v1.</summary>
        public string[] Required { get; init; } = [];

        /// <summary>Required fields v2 also constrains to 1 or more.</summary>
        public HashSet<string> PositiveIntegers { get; init; } = new(StringComparer.Ordinal);
    }
}
