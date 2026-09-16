using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Asks PoracleNG what its <c>/api/v2</c> surface carries, by reading the OpenAPI document it publishes.
/// </summary>
/// <remarks>
/// <para>
/// One probe, many answers — the same shape as <see cref="PoracleServerProfileService"/>, which reads
/// <c>/health</c>. Five features gate on PoracleNG PR #217 and none of them can be told apart by version:
/// the branch reports <c>5.3.0</c>, no release carries it, and which release it eventually lands in is
/// not knowable from here. The document is.
/// </para>
/// <para>
/// Cached ten minutes, matching the bounds read in <see cref="PoracleTrackingProxy"/> that already pulls
/// this same document. Two readers of one URL is a wart; folding the bounds into this service is the
/// obvious follow-up, and is deliberately not done here so that a change to the write path and a new
/// read of the same file do not land in one commit.
/// </para>
/// <para>
/// Fails closed, and quietly. A refused or unparseable document is the ordinary state of every server
/// running a release, so it is logged at debug rather than warning — this is not a fault, it is the
/// answer.
/// </para>
/// </remarks>
public partial class PoracleV2SchemaService(
    HttpClient httpClient,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<PoracleV2SchemaService> logger) : IPoracleV2SchemaService
{
    private const string CacheKey = "poracle:v2-schema-capabilities";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient = httpClient;
    private readonly IMemoryCache _cache = cache;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly ILogger<PoracleV2SchemaService> _logger = logger;

    /// <inheritdoc />
    public async Task<PoracleV2Capabilities> GetAsync(CancellationToken cancellationToken = default)
    {
        if (this._cache.TryGetValue<PoracleV2Capabilities>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var resolved = Parse(await this.FetchAsync(cancellationToken));

        // A degraded answer is cached too. The alternative is re-probing a dead server on every gate
        // check, and every alarm write consults one of these.
        this._cache.Set(CacheKey, resolved, CacheFor);

        return resolved;
    }

    /// <inheritdoc />
    public void Invalidate() => this._cache.Remove(CacheKey);

    /// <summary>
    /// Reads the capabilities out of an OpenAPI document. Anything unreadable answers
    /// <see cref="PoracleV2Capabilities.None"/> rather than throwing.
    /// </summary>
    internal static PoracleV2Capabilities Parse(string? openApiJson)
    {
        if (string.IsNullOrWhiteSpace(openApiJson))
        {
            return PoracleV2Capabilities.None;
        }

        try
        {
            using var document = JsonDocument.Parse(openApiJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return PoracleV2Capabilities.None;
            }

            // Each step re-checks the kind. A missing key leaves an Undefined element behind, and
            // TryGetProperty on one of those throws rather than answering false.
            var components = Property(root, "components");
            var schemas = Property(components, "schemas");
            var paths = Property(root, "paths");

            return new PoracleV2Capabilities
            {
                Read = true,
                TrustedSetAreas = HasProperty(schemas, "V2SetAreasBody", "trusted"),
                ProfileRename = HasProperty(schemas, "V2UpdateProfileBody", "name"),
                ProfileCreateReturnsNumber = CreateReturnsNumber(paths, schemas),
                AdminHumanRoutes = HasOperation(paths, "/v2/humans", "get")
                    && HasOperation(paths, "/v2/humans/{id}", "delete"),
                InvasionGruntType = HasProperty(schemas, "V2InvasionRule", "grunt_type"),
            };
        }
        catch (JsonException)
        {
            return PoracleV2Capabilities.None;
        }
    }

    /// <summary>
    /// The named child of an object, or an undefined element. The one safe way to walk this document:
    /// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> throws on anything that is not
    /// an object, including the undefined element a previous miss leaves behind.
    /// </summary>
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : default;

    /// <summary>True when the named schema declares the named property.</summary>
    private static bool HasProperty(JsonElement schemas, string schema, string property) =>
        Property(Property(Property(schemas, schema), "properties"), property).ValueKind != JsonValueKind.Undefined;

    /// <summary>True when the path declares the method.</summary>
    private static bool HasOperation(JsonElement paths, string path, string method) =>
        Property(Property(paths, path), method).ValueKind != JsonValueKind.Undefined;

    /// <summary>
    /// True when creating a profile answers with the assigned number.
    /// </summary>
    /// <remarks>
    /// Read as "the 200 body has a <c>profile_no</c>", not as "the response is not
    /// <c>StatusOKOutputBody</c>". Naming the old type would make this a test for one specific shape
    /// being absent, which a rename upstream would silently turn into a false positive — and a false
    /// positive here means reading a number out of a body that does not carry one.
    /// </remarks>
    private static bool CreateReturnsNumber(JsonElement paths, JsonElement schemas)
    {
        var ok = Property(
            Property(Property(Property(paths, "/v2/humans/{id}/profiles"), "post"), "responses"),
            "200");

        var refValue = Property(
            Property(Property(Property(ok, "content"), "application/json"), "schema"),
            "$ref");

        var reference = refValue.ValueKind == JsonValueKind.String ? refValue.GetString() : null;

        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        var name = reference[(reference.LastIndexOf('/') + 1)..];

        return HasProperty(schemas, name, "profile_no");
    }

    private async Task<string?> FetchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(this._apiAddress))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{this._apiAddress}/openapi.json");

            if (!string.IsNullOrEmpty(this._apiSecret))
            {
                request.Headers.Add("X-Poracle-Secret", this._apiSecret);
            }

            using var response = await this._httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(this._logger, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogUnavailable(this._logger, exception.Message);
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read PoracleNG's /openapi.json ({Reason}); treating its v2 surface as carrying nothing new, which is how every released version behaves.")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
