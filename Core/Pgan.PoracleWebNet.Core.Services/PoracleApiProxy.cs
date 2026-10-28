using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class PoracleApiProxy(
    HttpClient httpClient,
    IConfiguration configuration) : IPoracleApiProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PoracleConfig?> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/poracleWeb");
        var response = await this._httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var config = new PoracleConfig();

        if (root.TryGetProperty("locale", out var locale))
        {
            config.Locale = locale.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("providerURL", out var providerUrl))
        {
            config.ProviderUrl = providerUrl.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("staticKey", out var staticKey))
        {
            if (staticKey.ValueKind == JsonValueKind.Array && staticKey.GetArrayLength() > 0)
            {
                config.StaticKey = staticKey[0].GetString() ?? string.Empty;
            }
            else if (staticKey.ValueKind == JsonValueKind.String)
            {
                config.StaticKey = staticKey.GetString() ?? string.Empty;
            }
        }

        if (root.TryGetProperty("version", out var version))
        {
            config.PoracleVersion = version.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("pvpFilterMaxRank", out var pvpMaxRank))
        {
            config.PvpFilterMaxRank = pvpMaxRank.GetInt32();
        }

        if (root.TryGetProperty("pvpFilterLittleMinCP", out var pvpLittleMinCp))
        {
            config.PvpFilterLittleMinCp = pvpLittleMinCp.GetInt32();
        }

        if (root.TryGetProperty("pvpFilterGreatMinCP", out var pvpGreatMinCp))
        {
            config.PvpFilterGreatMinCp = pvpGreatMinCp.GetInt32();
        }

        if (root.TryGetProperty("pvpFilterUltraMinCP", out var pvpUltraMinCp))
        {
            config.PvpFilterUltraMinCp = pvpUltraMinCp.GetInt32();
        }

        if (root.TryGetProperty("pvpLittleLeagueAllowed", out var pvpLittle))
        {
            config.PvpLittleLeagueAllowed = pvpLittle.GetBoolean();
        }

        if (root.TryGetProperty("pvpCaps", out var pvpCaps) && pvpCaps.ValueKind == JsonValueKind.Array)
        {
            foreach (var cap in pvpCaps.EnumerateArray())
            {
                if (cap.ValueKind == JsonValueKind.Number && cap.TryGetInt32(out var capInt))
                {
                    config.PvpCaps.Add(capInt);
                }
                else if (cap.ValueKind == JsonValueKind.String && int.TryParse(cap.GetString(), out var capStr))
                {
                    config.PvpCaps.Add(capStr);
                }
            }
        }

        if (root.TryGetProperty("defaultPvpCap", out var defaultPvpCap))
        {
            if (defaultPvpCap.ValueKind == JsonValueKind.Number && defaultPvpCap.TryGetInt32(out var defInt))
            {
                config.DefaultPvpCap = defInt;
            }
            else if (defaultPvpCap.ValueKind == JsonValueKind.String && int.TryParse(defaultPvpCap.GetString(), out var defStr))
            {
                config.DefaultPvpCap = defStr;
            }
        }

        if (root.TryGetProperty("defaultTemplateName", out var templateName))
        {
            config.DefaultTemplateName = templateName.ValueKind == JsonValueKind.String
                ? templateName.GetString() ?? string.Empty
                : templateName.GetRawText();
        }

        if (root.TryGetProperty("everythingFlagPermissions", out var efp))
        {
            config.EverythingFlagPermissions = efp.ValueKind == JsonValueKind.String
                ? efp.GetString() ?? string.Empty
                : efp.GetRawText();
        }

        if (root.TryGetProperty("maxDistance", out var maxDist))
        {
            config.MaxDistance = maxDist.GetInt32();
        }

        if (root.TryGetProperty("disabledHooks", out var disabledHooks) && disabledHooks.ValueKind == JsonValueKind.Array)
        {
            // Absent (older Poracle, PoracleJS) stays null so callers can tell "upstream has no
            // opinion" apart from "upstream disables nothing". Only the former may be inferred from.
            config.DisabledHooks = [];
            foreach (var hook in disabledHooks.EnumerateArray())
            {
                if (hook.ValueKind == JsonValueKind.String && hook.GetString() is { Length: > 0 } name)
                {
                    config.DisabledHooks.Add(name);
                }
            }
        }

        if (root.TryGetProperty("availableLanguages", out var availableLanguages))
        {
            // The value is the restriction. Absent (a PoracleNG older than 5.2.1) and present-and-null
            // both mean unrestricted, which upstream reports for an unset and an empty map alike.
            if (availableLanguages.ValueKind == JsonValueKind.Array)
            {
                config.AvailableLanguages = [];
                foreach (var code in availableLanguages.EnumerateArray())
                {
                    if (code.ValueKind == JsonValueKind.String && code.GetString() is { Length: > 0 } value)
                    {
                        config.AvailableLanguages.Add(value);
                    }
                }
            }
        }

        if (root.TryGetProperty("admins", out var admins))
        {
            config.Admins = new PoracleAdmins();
            if (admins.TryGetProperty("discord", out var discordAdmins) &&
                discordAdmins.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in discordAdmins.EnumerateArray())
                {
                    config.Admins.Discord.Add(id.GetString() ?? string.Empty);
                }
            }

            if (admins.TryGetProperty("telegram", out var telegramAdmins) &&
                telegramAdmins.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in telegramAdmins.EnumerateArray())
                {
                    config.Admins.Telegram.Add(id.GetString() ?? string.Empty);
                }
            }
        }

        if (root.TryGetProperty("delegateAdministration", out var delegateAdmin) &&
            delegateAdmin.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in delegateAdmin.EnumerateArray())
            {
                // Support both {id, admins} and {webhookId, discordIds} key conventions
                var webhookId =
                    (entry.TryGetProperty("webhookId", out var wh) ? wh.GetString() : null) ??
                    (entry.TryGetProperty("id", out var id) ? id.GetString() : null);

                if (string.IsNullOrEmpty(webhookId))
                {
                    continue;
                }

                var users = new List<string>();
                var usersArray =
                    entry.TryGetProperty("discordIds", out var dIds) ? dIds :
                    entry.TryGetProperty("admins", out var adm) ? adm :
                    default;

                if (usersArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in usersArray.EnumerateArray())
                    {
                        if (u.GetString() is { } uid)
                        {
                            users.Add(uid);
                        }
                    }
                }

                config.DelegateAdministration.Add(new PoracleDelegateEntry
                {
                    WebhookId = webhookId,
                    DiscordIds = users
                });
            }
        }

        return config;
    }

    /// <summary>
    /// Reads the effective <c>tracking.quest_summary_enabled</c> flag from PoracleNG's config-values
    /// endpoint (<c>/api/config/values</c>, which exposes the merged-with-defaults config — the
    /// <c>poracleWeb</c> config view does not include the tracking section). Returns <c>null</c> when
    /// the value cannot be determined (endpoint shape changed, feature unknown), so the caller can
    /// degrade safely.
    /// </summary>
    public Task<bool?> GetQuestSummaryEnabledAsync() =>
        this.ReadConfigValueBoolAsync("tracking", "quest_summary_enabled");

    /// <inheritdoc />
    public Task<bool?> GetShowcaseDisabledAsync() =>
        this.ReadConfigValueBoolAsync("general", "disable_showcase");

    /// <inheritdoc />
    public Task<bool?> GetAreaSecurityEnabledAsync() =>
        this.ReadConfigValueBoolAsync("area_security", "enabled");

    /// <inheritdoc />
    public async Task<TelegramRegistrationGroups> GetTelegramRegistrationGroupsAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/values");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var values = doc.RootElement.TryGetProperty("values", out var v) ? v : default;

        static List<string> Strings(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
                : [];

        var telegram = values.ValueKind == JsonValueKind.Object && values.TryGetProperty("telegram", out var t) ? t : default;
        var security = values.ValueKind == JsonValueKind.Object && values.TryGetProperty("area_security", out var a) ? a : default;
        var enabled = security.ValueKind == JsonValueKind.Object
            && security.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;

        var communities = new List<TelegramCommunityGroups>();
        if (security.ValueKind == JsonValueKind.Object && security.TryGetProperty("communities", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    // PoracleNG records community names lowercased (reconcileAreaSecurity).
                    communities.Add(new TelegramCommunityGroups(name.GetString()!.ToLowerInvariant(), Strings(row, "telegram_channels")));
                }
            }
        }

        return new TelegramRegistrationGroups(enabled, Strings(telegram, "channels"), communities);
    }

    /// <inheritdoc />
    public Task<bool?> GetPokemonEditEnabledAsync() =>
        this.ReadConfigValueBoolAsync("tracking", "pokemon_edit");

    /// <summary>
    /// Reads a single boolean out of <c>GET /api/config/values</c>, whose body is shaped
    /// <c>{ "values": { "&lt;section&gt;": { "&lt;key&gt;": true } } }</c>. Returns <c>null</c> when the
    /// section or key is missing, or the value is not a boolean — an answer of "cannot determine",
    /// distinct from <c>false</c>.
    /// </summary>
    private async Task<bool?> ReadConfigValueBoolAsync(string section, string key)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/values");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("values", out var values)
            && values.TryGetProperty(section, out var sectionElement)
            && sectionElement.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return null;
    }

    public async Task<string?> GetTemplatesAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/templates");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Invasion grunt master data. The path is <c>/api/masterdata/grunts</c> — <c>/api/config/grunts</c>
    /// exists in neither supported backend, so this call could only ever 404 and throw.
    /// </summary>
    /// <returns>The raw JSON, or <c>null</c> when upstream is unreachable or does not serve it.</returns>
    public async Task<string?> GetGruntsAsync(string? locale = null)
    {
        // A PoracleNG too old for translated grunt names ignores the parameter and answers its English
        // shape, which is the same answer it gives with no parameter at all -- so this is safe to send
        // unconditionally and needs no capability check of its own. See #840.
        var query = string.IsNullOrEmpty(locale) ? string.Empty : $"?locale={Uri.EscapeDataString(locale)}";
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/masterdata/grunts{query}");
        var response = await this._httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Localized monster master data from <c>/api/masterdata/monsters</c>. Older PoracleJS builds do
    /// not serve this route, so a 404 is a normal outcome and yields <c>null</c> rather than throwing.
    /// </summary>
    public async Task<string?> GetMonstersAsync(string locale)
    {
        var request = this.CreateRequest(HttpMethod.Get,
            $"{this._apiAddress}/api/masterdata/monsters?locale={Uri.EscapeDataString(locale)}");
        var response = await this._httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetGeofenceAsync()
    {
        // Use any user ID to get the full area list with groups
        // The Poracle API returns all available areas for any valid user
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/geofence/all/hash");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <remarks>
    /// <c>GET /api/v2/humans/{id}/areas</c> only. The items -- <c>name</c>, <c>group</c>, <c>description</c>,
    /// <c>userSelectable</c> -- sit under an <c>areas</c> key, so the array is handed on as it came. gin's
    /// plaintext <c>404 page not found</c> is a server older than 5.4.0; problem+json at 404 is a human
    /// PoracleNG does not have, answered as null.
    /// </remarks>
    public async Task<string?> GetAreasWithGroupsAsync(string userId)
    {
        var request = this.CreateRequest(
            HttpMethod.Get, $"{this._apiAddress}/api/v2/humans/{Uri.EscapeDataString(userId)}/areas");
        var response = await this._httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.NotFound && !PoracleProblemDetails.IsProblemJson(json))
        {
            throw new PoracleUnsupportedException("listing areas", "PoracleNG 5.4.0");
        }

        return response.IsSuccessStatusCode ? AreasArray(json) : null;
    }

    /// <summary>The <c>areas</c> array out of the answer; a JSON null is the empty list.</summary>
    private static string? AreasArray(string json)
    {
        // Extract just the areas array from the response
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("areas", out var areas))
        {
            // V2AreasOutputBody declares the list as array-or-null.
            return areas.ValueKind == JsonValueKind.Null ? "[]" : areas.GetRawText();
        }

        return null;
    }

    public async Task<string?> GetAreaMapUrlAsync(string areaName)
    {
        var encoded = Uri.EscapeDataString(areaName);
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/geofence/{encoded}/map");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("url", out var url))
        {
            return url.GetString();
        }

        return null;
    }

    public async Task<string?> GetAllGeofenceDataAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/geofence/all");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetDistanceMapUrlAsync(double lat, double lon, int distance)
    {
        var request = this.CreateRequest(HttpMethod.Get,
            $"{this._apiAddress}/api/geofence/distanceMap/{lat}/{lon}/{distance}");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("url", out var url))
        {
            return url.GetString();
        }

        return null;
    }

    public async Task<string?> GetLocationMapUrlAsync(double lat, double lon)
    {
        var request = this.CreateRequest(HttpMethod.Get,
            $"{this._apiAddress}/api/geofence/locationMap/{lat}/{lon}");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("url", out var url))
        {
            return url.GetString();
        }

        return null;
    }

    public async Task ReloadGeofencesAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/geofence/reload");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendTestAlertAsync(TestAlertRequest request)
    {
        var httpRequest = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/test");
        var json = JsonSerializer.Serialize(request, JsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await this._httpClient.SendAsync(httpRequest);
        if (!response.IsSuccessStatusCode)
        {
            // PoracleNG says why ("message delivery not configured - check Discord/Telegram token
            // settings"), and that reason is the only thing telling the user a retry won't help. 502:
            // the failure is PoracleNG's, not the caller's request.
            var body = await response.Content.ReadAsStringAsync();
            throw new PoracleRequestRefusedException(
                PoracleProblemDetails.Describe(body, "no reason given"),
                (int)HttpStatusCode.BadGateway);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The body is the array <c>[{"target", "type", "message": {"content"}}]</c> that PoracleNG's
    /// <c>apiDeliverJob</c> binds. Discord posts <c>content</c> as the message text and Telegram sends it
    /// as the text step of its default send order. PoracleNG answers <c>{"status":"ok","queued":N}</c>
    /// once the job is queued, not once it is delivered. The target can be a webhook URL, so it is never
    /// part of an exception message from here.
    /// </remarks>
    public async Task DeliverTextAsync(string targetId, string targetType, string content)
    {
        var job = new Dictionary<string, object?>
        {
            ["target"] = targetId,
            ["type"] = targetType,
            ["message"] = new Dictionary<string, object?> { ["content"] = content },
        };
        var httpRequest = this.CreateRequest(HttpMethod.Post, $"{this._apiAddress}/api/deliverMessages");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(new[] { job }, JsonOptions), Encoding.UTF8, "application/json");
        var response = await this._httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> GetGeofencesGeoJsonAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/geofence/all/geojson");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }

    /// <inheritdoc />
    public async Task<string?> GetGeocodeForwardAsync(string query, string? language = null)
    {
        return await this.GetGeocodeAsync(
            $"{this._apiAddress}/api/geocode/forward?q={Uri.EscapeDataString(query)}{LanguageParam(language)}");
    }

    /// <inheritdoc />
    public async Task<string?> GetGeocodeReverseAsync(double lat, double lon, string? language = null)
    {
        return await this.GetGeocodeAsync(
            $"{this._apiAddress}/api/geocode/reverse?lat={lat.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={lon.ToString(CultureInfo.InvariantCulture)}{LanguageParam(language)}");
    }

    private static string LanguageParam(string? language) =>
        string.IsNullOrWhiteSpace(language) ? string.Empty : $"&language={Uri.EscapeDataString(language)}";

    /// <summary>
    /// The address line on the dashboard and the Areas page, and every result in the location picker, is
    /// decoration -- a geocoder being unreachable or slow must leave the coordinates showing, not surface
    /// an error. A 10-second timeout, independent of the shared client's default, keeps a stalled geocoder
    /// from holding up the picker the way it used to with a direct provider call.
    /// </summary>
    /// <returns>
    /// The body on success, otherwise <c>null</c>. A 404 that is gin's plaintext "route does not exist"
    /// means a PoracleNG below 5.4.0 and throws <see cref="PoracleUnsupportedException"/>; PoracleNG's own
    /// "nothing at this coordinate" -- <c>reverse</c> answers that as JSON, and always does under
    /// <c>[geocoding] forward_only</c> -- stays <c>null</c>. See <see cref="PoracleProblemDetails.IsProblemJson"/>.
    /// </returns>
    private async Task<string?> GetGeocodeAsync(string url)
    {
        var request = this.CreateRequest(HttpMethod.Get, url);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var response = await this._httpClient.SendAsync(request, cts.Token);
            var payload = await response.Content.ReadAsStringAsync(cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return payload;
            }

            if (response.StatusCode == HttpStatusCode.NotFound && !PoracleProblemDetails.IsProblemJson(payload))
            {
                throw new PoracleUnsupportedException("Address search", "PoracleNG 5.4.0");
            }

            return null;
        }
        catch (PoracleUnsupportedException)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreachable PoracleNG, a timeout, a malformed response -- all the same "no address this
            // time" answer to the caller, which is decoration and must not surface an error.
            return null;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }
        return request;
    }
}
