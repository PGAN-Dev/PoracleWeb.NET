using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Speaks to a <see href="https://github.com/komoot/photon">Photon</see> geocoder and answers in the
/// Nominatim shape the SPA already reads (<c>display_name</c>, <c>address.*</c>, <c>lat</c>/<c>lon</c>),
/// so choosing Photon changes nothing on the client. Photon rejects Nominatim's <c>format</c> and
/// <c>addressdetails</c> parameters outright, which is why it cannot simply be pointed at as a Nominatim URL.
/// </summary>
public static class PhotonGeocoding
{
    public static string BuildSearchUrl(string baseUrl, string query, int limit) =>
        $"{baseUrl.TrimEnd('/')}/api?q={Uri.EscapeDataString(query)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";

    public static string BuildReverseUrl(string baseUrl, double lat, double lon) =>
        $"{baseUrl.TrimEnd('/')}/reverse?lat={lat.ToString(CultureInfo.InvariantCulture)}" +
        $"&lon={lon.ToString(CultureInfo.InvariantCulture)}&limit=1";

    /// <summary>
    /// Photon search results as a Nominatim <c>/search</c> array. Features without coordinates are skipped.
    /// </summary>
    public static string ToNominatimSearch(string photonJson)
    {
        var results = new JsonArray();
        foreach (var feature in ReadFeatures(photonJson))
        {
            if (ToNominatimPlace(feature, includeCoordinates: true) is { } place)
            {
                results.Add(place);
            }
        }

        return results.ToJsonString();
    }

    /// <summary>
    /// The nearest Photon feature as a Nominatim <c>/reverse</c> object, or Nominatim's own
    /// <c>{"error":"Unable to geocode"}</c> when Photon found nothing.
    /// </summary>
    public static string ToNominatimReverse(string photonJson)
    {
        foreach (var feature in ReadFeatures(photonJson))
        {
            if (ToNominatimPlace(feature, includeCoordinates: false) is { } place)
            {
                return place.ToJsonString();
            }
        }

        return /*lang=json,strict*/ """{"error":"Unable to geocode"}""";
    }

    private static List<JsonElement> ReadFeatures(string photonJson)
    {
        using var doc = JsonDocument.Parse(photonJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        // Clone so the elements outlive the document.
        return [.. features.EnumerateArray().Select(f => f.Clone())];
    }

    private static JsonObject? ToNominatimPlace(JsonElement feature, bool includeCoordinates)
    {
        // TryGetProperty throws on anything but an object, so a null feature or geometry is checked first.
        if (feature.ValueKind != JsonValueKind.Object ||
            !feature.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = Str(props, "name");
        var type = Str(props, "type");
        var houseNumber = Str(props, "housenumber");
        // A feature that is itself a street carries its name in "name", not "street".
        var road = Str(props, "street") ?? (type == "street" ? name : null);
        var suburb = Str(props, "district") ?? Str(props, "locality");
        var city = Str(props, "city") ?? (type == "city" ? name : null);
        var county = Str(props, "county");
        var state = Str(props, "state");
        var postcode = Str(props, "postcode");
        var country = Str(props, "country");
        var countryCode = Str(props, "countrycode")?.ToLowerInvariant();

        var address = new JsonObject();
        Add(address, "house_number", houseNumber);
        Add(address, "road", road);
        Add(address, "suburb", suburb);
        Add(address, "city", city);
        Add(address, "county", county);
        Add(address, "state", state);
        Add(address, "postcode", postcode);
        Add(address, "country", country);
        Add(address, "country_code", countryCode);

        // Nominatim's display_name: most specific first, comma-separated, no repeats.
        var parts = new List<string>();
        foreach (var part in new[] { name, houseNumber, road, suburb, city, county, state, postcode, country })
        {
            if (!string.IsNullOrWhiteSpace(part) && !parts.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(part);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var place = new JsonObject
        {
            ["display_name"] = string.Join(", ", parts),
            ["address"] = address,
        };
        Add(place, "name", name);

        if (includeCoordinates)
        {
            // GeoJSON order is [lon, lat]; Nominatim sends both as strings.
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object ||
                !geometry.TryGetProperty("coordinates", out var coords) ||
                coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() < 2 ||
                coords[0].ValueKind != JsonValueKind.Number || coords[1].ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            place["lat"] = coords[1].GetDouble().ToString(CultureInfo.InvariantCulture);
            place["lon"] = coords[0].GetDouble().ToString(CultureInfo.InvariantCulture);
        }

        return place;
    }

    private static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static void Add(JsonObject obj, string key, string? value)
    {
        if (value != null)
        {
            obj[key] = value;
        }
    }
}
