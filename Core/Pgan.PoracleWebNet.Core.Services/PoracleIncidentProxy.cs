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
    IUserAreaPolicy areaPolicy,
    ILogger<PoracleIncidentProxy> logger) : IPoracleIncidentProxy
{
    private const string RequiredVersion = "PoracleNG 5.4.0";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly IUserAreaPolicy _areaPolicy = areaPolicy;
    private readonly ILogger<PoracleIncidentProxy> _logger = logger;

    public async Task<IReadOnlyList<PokestopEvent>> GetByUserAsync(string userId)
    {
        var response = await this.SendAsync(HttpMethod.Get, this.Route(userId), body: null);
        var root = await ReadRootAsync(response);
        return ReadRules(root, "rules");
    }

    public Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules, bool dryRun = false) =>
        this.CreateCoreAsync(userId, rules, profileNo: null, dryRun);

    public Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules, int profileNo, bool dryRun) =>
        this.CreateCoreAsync(userId, rules, profileNo, dryRun);

    public Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule) =>
        this.ReplaceCoreAsync(userId, uid, rule, profileNo: null);

    public Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule, int profileNo) =>
        this.ReplaceCoreAsync(userId, uid, rule, profileNo);

    private async Task<PokestopEventWriteResult> CreateCoreAsync(
        string userId, IEnumerable<PokestopEvent> rules, int? profileNo, bool dryRun)
    {
        var list = rules.ToList();
        var trusted = await this.AuthoriseOverrideAreasAsync(userId, list, storedOnRule: null);
        if (trusted && !dryRun)
        {
            // The commit carries only the rules the dry run found new (IncidentCreateProtocol), and each is an insert.
            foreach (var rule in list.Where(r => r.OverrideAreas is { Count: > 0 }))
            {
                await this._areaPolicy.EnsureOverrideAreasWithinLimitAsync(userId, rule.OverrideAreas!, storedOnRule: null);
            }
        }

        var payload = "[" + string.Join(",", list.Select(RuleJson)) + "]";
        var url = $"{this.Route(userId)}?silent=true{ProfileQuery(profileNo)}{TrustedQuery(trusted)}"
            + (dryRun ? "&dry_run=true" : string.Empty);
        var response = await this.SendAsync(HttpMethod.Post, url, payload, writeFeature: "saving alarms");
        return await ReadWriteResultAsync(response);
    }

    private async Task<PokestopEventWriteResult> ReplaceCoreAsync(string userId, int uid, PokestopEvent rule, int? profileNo)
    {
        // uid 0 is an insert, which v2's uid-addressed PUT cannot express.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uid);

        var trusted = await this.AuthoriseOverrideAreasAsync(
            userId, [rule], () => this.StoredOverrideAreasAsync(userId, uid, profileNo));
        if (trusted)
        {
            await this._areaPolicy.EnsureOverrideAreasWithinLimitAsync(
                userId, rule.OverrideAreas!, () => this.StoredOverrideAreasAsync(userId, uid, profileNo));
        }

        var response = await this.SendAsync(
            HttpMethod.Put,
            $"{this.Route(userId)}/{uid}?silent=true{ProfileQuery(profileNo)}{TrustedQuery(trusted)}",
            RuleJson(rule),
            writeFeature: "saving alarms");
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

        // 5.4.0 takes the uids in the body, as the tracking proxy's deletes send them; ?uid= is the older form.
        var response = await this.SendAsync(
            HttpMethod.Delete,
            $"{this.Route(userId)}?silent=true",
            JsonSerializer.Serialize(new { uids = list }),
            writeFeature: "deleting alarms");
        var root = await ReadRootAsync(response);
        return ReadRules(root, "deleted").Count;
    }

    /// <inheritdoc />
    public async Task<TrackingPatchResult> PatchAsync(string userId, IReadOnlyList<int> uids, TrackingPatchSet settings)
    {
        if (uids.Count == 0)
        {
            return TrackingPatchResult.None;
        }

        // Always trusted, for the reason the tracking proxy's PatchAsync gives: PoracleNG re-validates the stored
        // rule's areas, untrusted it refuses a user's own drawn fence, and a PATCH set cannot name a new area.
        var response = await this.SendAsync(
            HttpMethod.Patch,
            $"{this.Route(userId)}?silent=true{TrustedQuery(trusted: true)}",
            TrackingPatchWire.Body(uids, settings),
            writeFeature: "saving alarms");
        return TrackingPatchWire.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Authorises every override area the rules name, through the one area policy.</summary>
    /// <returns>
    /// True when any rule names one, so the write is sent <c>trusted</c>: PoracleNG's own check allows only
    /// <c>userSelectable</c> areas, which a user's own drawn fence is not.
    /// </returns>
    private async Task<bool> AuthoriseOverrideAreasAsync(
        string userId, IReadOnlyList<PokestopEvent> rules, Func<Task<IReadOnlyCollection<string>>>? storedOnRule)
    {
        var requested = rules
            .SelectMany(r => r.OverrideAreas ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Count == 0)
        {
            return false;
        }

        await this._areaPolicy.EnsureOverrideAreasAllowedAsync(userId, requested, storedOnRule);
        return true;
    }

    /// <summary>The override areas the stored rule holds, read through v2's single-rule route.</summary>
    private async Task<IReadOnlyCollection<string>> StoredOverrideAreasAsync(string userId, int uid, int? profileNo)
    {
        var query = profileNo is { } no ? $"?profile={no}" : string.Empty;
        var response = await this.SendAsync(HttpMethod.Get, $"{this.Route(userId)}/{uid}{query}", body: null);
        var root = await ReadRootAsync(response);
        return [.. ReadRules(root, "rules").SelectMany(r => r.OverrideAreas ?? [])];
    }

    private static string TrustedQuery(bool trusted) => trusted ? "&trusted=true" : string.Empty;

    private static string ProfileQuery(int? profileNo) => profileNo is { } no ? $"&profile={no}" : string.Empty;

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
                // A blank name names no fence and is never put to the area policy, so it does not ride a
                // trusted write either.
                if (string.IsNullOrWhiteSpace(area))
                {
                    continue;
                }

                writer.WriteStringValue(area);
            }

            writer.WriteEndArray();

            // v2's replace is a full replace, so a mention left out is a mention erased.
            if (!string.IsNullOrEmpty(rule.Ping))
            {
                writer.WriteString("ping", rule.Ping);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<PokestopEvent> ReadRules(JsonElement root, string property)
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
        Ping = Str(element, "ping"),
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
            ReadRules(root, "unchanged"),
            ReadPrevious(root));
    }

    /// <summary>What each <c>updated</c> rule held before the write, index for index; null where none was reported.</summary>
    private static List<PokestopEvent?>? ReadPrevious(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("updated", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<PokestopEvent?>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("previous", out var previous)
                && previous.ValueKind == JsonValueKind.Object)
            {
                var rule = ReadRule(previous);
                if (rule.Uid <= 0)
                {
                    // The previous state is the same rule, so it lives under the updated rule's uid.
                    rule.Uid = Int(element, "uid");
                }

                list.Add(rule);
            }
            else
            {
                list.Add(null);
            }
        }

        return list;
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

    /// <param name="writeFeature">
    /// What a write is for, in the user's words. Set on writes, where gin's plaintext 404 means the route is
    /// absent and the server too old, rather than the account being gone.
    /// </param>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? body, bool allowNotFound = false, string? writeFeature = null)
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
            var refusal = await response.Content.ReadAsStringAsync();
            if (writeFeature is not null
                && method == HttpMethod.Delete
                && response.StatusCode == HttpStatusCode.UnprocessableEntity
                && refusal.Contains("\"query.uid\"", StringComparison.Ordinal))
            {
                // Before 5.4.0 the bulk route required ?uid and had no body form, so it refuses the body by
                // naming the missing query parameter. That is a server too old, not a bad request.
                throw new PoracleUnsupportedException(writeFeature, RequiredVersion);
            }

            throw new AlarmValidationException(ExtractDetail(refusal));
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // v2 refuses a replacement that would duplicate another rule, exactly or one updatable field away.
            // To the user that is the same thing as a takeover: another alarm already holds those settings.
            throw new TrackingConflictException("incident", PoracleTrackingProxy.TakeoverMessage);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var notFound = await response.Content.ReadAsStringAsync();
            var problem = PoracleProblemDetails.IsProblemJson(notFound);

            if (writeFeature is not null && !problem)
            {
                // gin's plaintext "404 page not found": the route is absent, so this is not 5.4.0.
                throw new PoracleUnsupportedException(writeFeature, RequiredVersion);
            }

            if (allowNotFound)
            {
                return response;
            }

            // A problem+json 404 that is not about the human is about the rule: an edit racing a delete, or a
            // scheduler profile switch moving the rule out of view. Signing the user out for that was wrong.
            if (problem && !notFound.Contains("human not found", StringComparison.Ordinal))
            {
                throw new TrackingRuleNotFoundException("incident", PoracleProblemDetails.Describe(notFound));
            }

            // The human is gone: the same dead-session case the tracking proxy maps. See #595.
            throw new AccountGoneException();
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>Pulls the message out of an RFC7807 body: huma puts it in <c>detail</c>.</summary>
    private static string ExtractDetail(string body)
    {
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
