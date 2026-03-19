using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class PoracleApiProxy(HttpClient httpClient, IConfiguration configuration) : IPoracleApiProxy
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

    public async Task<PoracleConfig?> GetConfigAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/poracleWeb");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
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

    public async Task<string?> GetAreasAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/humans/{userId}");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetTemplatesAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/templates");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetAdminRolesAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get,
            $"{this._apiAddress}/api/humans/{userId}/getAdministrationRoles");
        var response = await this._httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetGruntsAsync()
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/config/grunts");
        var response = await this._httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
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

    public async Task<string?> GetAreasWithGroupsAsync(string userId)
    {
        var request = this.CreateRequest(HttpMethod.Get, $"{this._apiAddress}/api/humans/{Uri.EscapeDataString(userId)}");
        var response = await this._httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        // Extract just the areas array from the response
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("areas", out var areas))
        {
            return areas.GetRawText();
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
