using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The only place in PoracleWeb that says <c>incident</c> or <c>display_type</c>.
/// </summary>
/// <inheritdoc cref="IPoracleIncidentProxy"/>
public sealed partial class PoracleIncidentProxy(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PoracleIncidentProxy> logger) : IPoracleIncidentProxy
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly ILogger<PoracleIncidentProxy> _logger = logger;

    public async Task<IReadOnlyList<PokestopEvent>> GetByUserAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, this.Route(userId), body: null);
        var root = await ReadRootAsync(response);
        return ReadRules(root, "rules");
    }

    public async Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules)
    {
        var payload = "[" + string.Join(",", rules.Select(RuleJson)) + "]";
        var response = await this.SendAsync(HttpMethod.Post, this.Route(userId) + "?silent=true", payload);
        return await ReadWriteResultAsync(response);
    }

    public async Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule)
    {
        var response = await this.SendAsync(
            HttpMethod.Put, $"{this.Route(userId)}/{uid}?silent=true", RuleJson(rule));
        return await ReadWriteResultAsync(response);
    }

    public async Task DeleteByUidAsync(string userId, int uid)
    {
        var response = await this.SendAsync(
            HttpMethod.Delete, $"{this.Route(userId)}/{uid}?silent=true", body: null, allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone, or belongs to the invasion half of the shared table. Either way the row
            // the caller asked about is not there, which is what they wanted.
            LogDeleteNotFound(this._logger, uid);
        }
    }

    public async Task<int> BulkDeleteByUidsAsync(string userId, IEnumerable<int> uids)
    {
        var list = uids.Distinct().ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var query = string.Join(",", list);
        var response = await this.SendAsync(
            HttpMethod.Delete, $"{this.Route(userId)}?uid={query}&silent=true", body: null);
        var root = await ReadRootAsync(response);
        return ReadRules(root, "deleted").Count;
    }

    private string Route(string userId) =>
        $"{this._apiAddress}/api/v2/humans/{Uri.EscapeDataString(userId)}/tracking/incident";

    /// <summary>
    /// The strict v2 rule object. <c>uid</c> is deliberately never sent: on a create the rule's
    /// identity is its <c>display_type</c>, and on a replace the uid is in the path.
    /// </summary>
    private static string RuleJson(PokestopEvent rule)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("display_type", rule.DisplayType);
            writer.WriteNumber("distance", rule.Distance);
            writer.WriteString("template", rule.Template ?? string.Empty);

            // The shared invasion table carries the clean bitmask; v2 splits it into three booleans.
            // Round-tripping through CleanFlags keeps bits set elsewhere (the bot, another page) alive.
            writer.WriteBoolean("clean", CleanFlags.IsAutoDelete(rule.Clean));
            writer.WriteBoolean("edit", CleanFlags.IsEdit(rule.Clean));
            writer.WriteBoolean("summary", CleanFlags.IsSummary(rule.Clean));

            writer.WriteString("override_location_label", rule.OverrideLocationLabel ?? string.Empty);
            writer.WritePropertyName("override_areas");
            writer.WriteStartArray();
            foreach (var area in rule.OverrideAreas ?? [])
            {
                writer.WriteStringValue(area);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IReadOnlyList<PokestopEvent> ReadRules(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<PokestopEvent>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            list.Add(ReadRule(element));
        }

        return list;
    }

    private static PokestopEvent ReadRule(JsonElement element) => new()
    {
        Uid = Int(element, "uid"),
        DisplayType = Int(element, "display_type"),
        Distance = Int(element, "distance"),
        Template = Str(element, "template"),
        Clean = CleanFlags.Compose(
            Bool(element, "clean"), Bool(element, "edit"), Bool(element, "summary")),
        OverrideLocationLabel = Str(element, "override_location_label"),
        OverrideAreas = Areas(element),
    };

    // v2 nulls every field sitting at its wildcard, so every read has to be null-tolerant by design.
    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string>? Areas(JsonElement e)
    {
        if (!e.TryGetProperty("override_areas", out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)];
    }

    private static async Task<PokestopEventWriteResult> ReadWriteResultAsync(HttpResponseMessage response)
    {
        var root = await ReadRootAsync(response);
        return new PokestopEventWriteResult(
            ReadRules(root, "created"),
            ReadRules(root, "updated"),
            ReadRules(root, "unchanged"));
    }

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return PoracleJsonHelper.EmptyArray;
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? body, bool allowNotFound = false)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(this._apiSecret))
        {
            request.Headers.Add("X-Poracle-Secret", this._apiSecret);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await this._httpClient.SendAsync(request);

        // 422 is PoracleNG naming what is wrong with the submission -- "unknown display_type" for an
        // event this server's game data does not have. EnsureSuccessStatusCode would flatten that
        // into a 500 telling the user the server broke. See #539 for the same fix on the v1 proxy.
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            throw new AlarmValidationException(await ExtractDetailAsync(response));
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new TrackingConflictException("incident", await ExtractDetailAsync(response));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (allowNotFound)
            {
                return response;
            }

            // The route itself 404s on a PoracleNG older than 5.2.0, but the feature gate is meant to
            // have kept us off it entirely by then, so a 404 here means the human is gone -- the same
            // dead-session case the v1 proxy maps. See #595.
            throw new AccountGoneException();
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>Pulls the message out of an RFC7807 body: huma puts it in <c>detail</c>.</summary>
    private static async Task<string> ExtractDetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            foreach (var name in new[] { "detail", "message", "title" })
            {
                if (root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw body.
        }

        return string.IsNullOrWhiteSpace(body) ? "Poracle rejected the request." : body;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delete pokestop-event uid={Uid} returned 404 (already deleted)")]
    private static partial void LogDeleteNotFound(ILogger logger, int uid);
}
