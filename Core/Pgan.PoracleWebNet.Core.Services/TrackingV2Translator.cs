using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Rewrites a v1-shaped pokemon row into the body <c>/api/v2</c> will accept.
/// </summary>
/// <remarks>
/// <para>
/// The whole of PoracleWeb builds and compares alarm bodies in v1's shape — <see cref="TrackingFieldPreserver"/>,
/// <see cref="TrackingUpdateReconciler"/>, <c>BulkUidRemap</c> and <c>QuickPickService</c> all do. Keeping
/// that shape as the single internal currency and translating once, at the wire, is what stops a v1-shaped
/// row leaking into a v2 request: <c>V2PokemonRule</c> sets <c>additionalProperties: false</c>, so one stray
/// <c>ping</c> is a 422 and the write fails outright. Verified live against 5.2.1.
/// </para>
/// <para>
/// Three fields genuinely change shape. <c>clean</c> is a 3-bit mask on v1 and three booleans on v2
/// (<c>clean</c>/<c>edit</c>/<c>summary</c>); <c>gender</c> is an int on v1 and
/// <c>any|male|female|genderless</c> on v2; <c>pvp_ranking_league</c> is any int on v1 and an enum of
/// {0, 500, 1500, 2500} on v2. Five more — <c>uid</c>, <c>id</c>, <c>profile_no</c>, <c>ping</c>,
/// <c>description</c> — are addressing and presentation rather than filter, and v2 has no place for them.
/// </para>
/// <para>
/// <b>The translator never widens or narrows what PoracleNG will accept.</b> Anything it cannot express
/// faithfully — a property it does not know, a gender outside 0-3, a league outside the enum — makes it
/// answer false, and the caller sends the row to the frozen v1 surface instead. Refusing outright would
/// mean a PoracleNG newer than this translator broke every pokemon edit; dropping the field silently would
/// be #730 all over again. Falling back does neither.
/// </para>
/// </remarks>
internal static class TrackingV2Translator
{
    /// <summary>
    /// Addressing and presentation. v2 carries none of it in the rule body, and reconstructs all of it
    /// itself: <c>uid</c>/<c>id</c>/<c>profile_no</c> come from the route, and <c>description</c> is a
    /// display string PoracleNG computes rather than a stored column. <c>ping</c> is NOT here — it is a
    /// real column that v2 blanks, so it is handled in <see cref="TryWriteProperty"/>.
    /// </summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "uid", "id", "profile_no", "description",
    };

    /// <summary>Every integer filter <c>V2PokemonRule</c> declares, taken from 5.2.1's openapi.golden.json.</summary>
    private static readonly HashSet<string> IntegerFields = new(StringComparer.Ordinal)
    {
        "atk", "costume", "def", "distance", "form", "max_atk", "max_cp", "max_def", "max_iv",
        "max_level", "max_rarity", "max_size", "max_sta", "max_weight", "min_cp", "min_iv",
        "min_level", "min_time", "min_weight", "pokemon_id", "pvp_ranking_best", "pvp_ranking_cap",
        "pvp_ranking_evolution", "pvp_ranking_min_cp", "pvp_ranking_worst", "rarity", "size", "sta",
    };

    /// <summary>The four leagues <c>V2PokemonRule</c> permits. 0 means "no PVP filter".</summary>
    private static readonly HashSet<int> Leagues = [0, 500, 1500, 2500];

    /// <summary>v2's gender enum: index is the v1 integer.</summary>
    private static readonly string[] Genders = ["any", "male", "female", "genderless"];

    /// <summary>
    /// Translates one v1-shaped pokemon row. Returns false — leaving <paramref name="translated"/>
    /// untouched — when the row carries something v2 cannot be told faithfully.
    /// </summary>
    /// <param name="row">A single v1-shaped alarm object, as every alarm service already builds.</param>
    /// <param name="translated">The v2 body on success.</param>
    /// <param name="unsupported">What stopped the translation, for the log. Null on success.</param>
    public static bool TryTranslatePokemon(JsonElement row, out JsonElement translated, out string? unsupported)
    {
        translated = default;
        unsupported = null;

        if (row.ValueKind != JsonValueKind.Object)
        {
            unsupported = "the body is not a single rule object";
            return false;
        }

        if (!row.TryGetProperty("pokemon_id", out var speciesId) || speciesId.ValueKind != JsonValueKind.Number)
        {
            // v2 makes pokemon_id the one required field. A row without it could only ever 422.
            unsupported = "pokemon_id is missing";
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

                if (!TryWriteProperty(writer, property, out unsupported))
                {
                    return false;
                }
            }

            writer.WriteEndObject();
        }

        translated = JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
        return true;
    }

    private static bool TryWriteProperty(Utf8JsonWriter writer, JsonProperty property, out string? unsupported)
    {
        unsupported = null;

        switch (property.Name)
        {
            case "clean":
                return TryWriteClean(writer, property.Value, out unsupported);

            case "ping":
                return TryWritePing(property.Value, out unsupported);

            case "gender":
                return TryWriteGender(writer, property.Value, out unsupported);

            case "pvp_ranking_league":
                return TryWriteLeague(writer, property.Value, out unsupported);

            case "template":
            case "override_location_label":
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    unsupported = $"{property.Name} is not a string";
                    return false;
                }

                property.WriteTo(writer);
                return true;

            case "override_areas":
                return TryWriteOverrideAreas(writer, property.Value, out unsupported);

            default:
                if (!IntegerFields.Contains(property.Name))
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
    /// <c>ping</c> is the mention prepended to the DM — a role or user the alert is meant to notify. It is
    /// a real <c>monsters</c> column and the v1 body carries it, but <c>V2PokemonRule</c> has no field for
    /// it and the handler stores <c>Ping: ""</c> unconditionally ("server-managed"). Verified live against
    /// 5.2.1: a rule holding <c>&lt;@&amp;400027130022592512&gt;</c> came back with an empty ping after one
    /// v2 PUT.
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

    private static bool TryWriteGender(Utf8JsonWriter writer, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var gender)
            || gender < 0
            || gender >= Genders.Length)
        {
            unsupported = "gender is outside 0-3";
            return false;
        }

        writer.WriteString("gender", Genders[gender]);
        return true;
    }

    private static bool TryWriteLeague(Utf8JsonWriter writer, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var league))
        {
            unsupported = "pvp_ranking_league is not a number";
            return false;
        }

        if (!Leagues.Contains(league))
        {
            unsupported = $"pvp_ranking_league {league} is not one of 0, 500, 1500, 2500";
            return false;
        }

        writer.WriteNumber("pvp_ranking_league", league);
        return true;
    }

    /// <summary>
    /// Areas arrive as a real array from the model, but a stored row carried forward by
    /// <see cref="TrackingFieldPreserver"/> can hold the column verbatim, and PoracleNG's own column is a
    /// JSON string. Normalise rather than refuse — this is the one shape difference worth absorbing,
    /// because it is PoracleWeb's own read that produced it.
    /// </summary>
    private static bool TryWriteOverrideAreas(Utf8JsonWriter writer, JsonElement value, out string? unsupported)
    {
        unsupported = null;

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Array:
                writer.WritePropertyName("override_areas");
                value.WriteTo(writer);
                return true;

            case JsonValueKind.String:
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    writer.WriteNull("override_areas");
                    return true;
                }

                try
                {
                    using var parsed = JsonDocument.Parse(raw);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        unsupported = "override_areas is a string that is not a JSON array";
                        return false;
                    }

                    writer.WritePropertyName("override_areas");
                    parsed.RootElement.WriteTo(writer);
                    return true;
                }
                catch (JsonException)
                {
                    unsupported = "override_areas is a string that is not JSON";
                    return false;
                }

            default:
                unsupported = "override_areas is neither an array nor null";
                return false;
        }
    }
}
