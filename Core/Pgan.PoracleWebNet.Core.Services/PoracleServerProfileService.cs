using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Asks PoracleNG what it is and what it can store.
/// </summary>
/// <remarks>
/// <para>
/// Two reads, because they answer different questions. <c>/health</c> gives the release number and
/// PoracleNG's own capability map — which covers bot and template-editor features, and nothing about
/// alarm columns. The applied migration number covers the columns.
/// </para>
/// <para>
/// <c>/health</c> is unauthenticated, so this carries no secret and works even when the API key is
/// wrong — which is itself worth knowing, since "reachable but every write 401s" and "not running at
/// all" look identical from the dashboard otherwise.
/// </para>
/// </remarks>
public partial class PoracleServerProfileService(
    HttpClient httpClient,
    IPoracleSchemaVersionReader schemaReader,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<PoracleServerProfileService> logger) : IPoracleServerProfileService
{
    private const string CacheKey = "poracle:server-profile";

    /// <summary>
    /// Long enough that a dashboard load costs nothing, short enough that an upgrade shows up without a
    /// restart. An admin who wants it sooner has the refresh button.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient = httpClient;
    private readonly IPoracleSchemaVersionReader _schemaReader = schemaReader;
    private readonly IMemoryCache _cache = cache;
    private readonly string _apiAddress = configuration["Poracle:ApiAddress"] ?? string.Empty;
    private readonly ILogger<PoracleServerProfileService> _logger = logger;

    /// <inheritdoc />
    public async Task<PoracleServerProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        if (this._cache.TryGetValue(CacheKey, out PoracleServerProfile? cached) && cached is not null)
        {
            return cached;
        }

        var profile = await this.ProbeAsync(cancellationToken);
        this._cache.Set(CacheKey, profile, CacheFor);

        return profile;
    }

    /// <inheritdoc />
    public void Invalidate() => this._cache.Remove(CacheKey);

    private async Task<PoracleServerProfile> ProbeAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(this._apiAddress))
        {
            return PoracleServerProfile.Unknown(now);
        }

        // The schema read is independent of whether PoracleNG answers, and is worth having either way:
        // a stopped process still leaves a migrated database behind.
        var schemaVersion = await this._schemaReader.GetAppliedMigrationAsync(cancellationToken);

        try
        {
            using var response = await this._httpClient.GetAsync(
                $"{this._apiAddress.TrimEnd('/')}/health", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var (version, capabilities) = ParseHealth(json);

            return new PoracleServerProfile
            {
                Version = version,
                Capabilities = capabilities,
                SchemaVersion = schemaVersion,
                Reachable = true,
                CheckedAt = now,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogProbeFailed(this._logger, this._apiAddress, ex);

            return new PoracleServerProfile
            {
                SchemaVersion = schemaVersion,
                Reachable = false,
                CheckedAt = now,
            };
        }
    }

    /// <summary>
    /// Pulls the version and the capability map out of the health payload.
    /// </summary>
    /// <remarks>
    /// Every key is read as it comes rather than into a fixed type, because the map is upstream's and it
    /// grows: <c>derivedDtsTypes</c> exists on their develop branch and not in any release. A fixed set
    /// would silently discard whatever lands next, which is the opposite of what this is for.
    /// </remarks>
    private static (string? Version, Dictionary<string, bool> Capabilities) ParseHealth(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

        var capabilities = new Dictionary<string, bool>(StringComparer.Ordinal);

        if (root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            foreach (var capability in caps.EnumerateObject())
            {
                // Only booleans. Upstream states the map is booleans-only and that anything with shape
                // to it lives on its own endpoint, so a non-boolean here is a payload we do not
                // understand rather than a feature to guess at.
                if (capability.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    capabilities[capability.Name] = capability.Value.GetBoolean();
                }
            }
        }

        return (version, capabilities);
    }

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "Could not read PoracleNG's health at {ApiAddress}. Version-gated features stay off until it answers.")]
    private static partial void LogProbeFailed(ILogger logger, string apiAddress, Exception exception);
}
