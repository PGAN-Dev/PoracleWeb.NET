using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Asks GitHub what the newest PoracleNG and PoracleWeb are, so an admin finds out they are behind here
/// rather than from a bug report.
/// </summary>
/// <remarks>
/// <para>
/// The two are read differently because the two projects publish differently. PoracleWeb cuts GitHub
/// releases, so its latest is a tag name. PoracleNG has no releases and no tags at all — its version is
/// a constant in <c>processor/version.go</c>, bumped by hand each cycle, so the released number is read
/// from that file on <c>main</c>.
/// </para>
/// <para>
/// That file is also what makes a development build identifiable: <c>main</c> holds 5.1.0 while
/// <c>develop</c> already holds 5.2.0, so a binary reporting more than <c>main</c> cannot have come from
/// a release. It is a better signal than the branch name, which the binary knows and never publishes.
/// </para>
/// <para>
/// This is the only part of PoracleWeb that talks to anything outside the deployment, so it is
/// switchable off with <c>disable_update_check</c> and fails silently. Nothing is sent: two anonymous
/// GETs, no identifiers, no payload.
/// </para>
/// </remarks>
public partial class UpdateCheckService(
    HttpClient httpClient,
    ISiteSettingService siteSettings,
    IMemoryCache cache,
    ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    /// <summary>Site setting that switches the outbound check off entirely.</summary>
    public const string DisableKey = "disable_update_check";

    private const string CacheKey = "poracle:update-check";

    /// <summary>
    /// Releases happen weekly at most, and the unauthenticated GitHub allowance is 60 calls an hour for
    /// the whole host. Six hours keeps this far away from both.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

    private const string PoracleWebReleaseUrl = "https://api.github.com/repos/PGAN-Dev/PoracleWeb.NET/releases/latest";
    private const string PoracleNgVersionUrl = "https://raw.githubusercontent.com/jfberry/PoracleNG/main/processor/version.go";

    private readonly HttpClient _httpClient = httpClient;
    private readonly ISiteSettingService _siteSettings = siteSettings;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<UpdateCheckService> _logger = logger;

    /// <inheritdoc />
    public async Task<(UpdateStatus PoracleWeb, UpdateStatus PoracleNg)> CheckAsync(
        string? runningPoracleWeb,
        string? runningPoracleNg,
        CancellationToken cancellationToken = default)
    {
        if (await this._siteSettings.GetBoolAsync(DisableKey))
        {
            return (UpdateStatus.Unknown(runningPoracleWeb), UpdateStatus.Unknown(runningPoracleNg));
        }

        var (latestWeb, latestNg) = await this.GetLatestAsync(cancellationToken);

        return (
            UpdateStatus.Compare(runningPoracleWeb, latestWeb),
            UpdateStatus.Compare(runningPoracleNg, latestNg));
    }

    /// <inheritdoc />
    public void Invalidate() => this._cache.Remove(CacheKey);

    private async Task<(string? Web, string? Ng)> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (this._cache.TryGetValue(CacheKey, out (string? Web, string? Ng) cached))
        {
            return cached;
        }

        // Independently: one project being unreachable should not hide the other's answer.
        var web = await this.ReadLatestPoracleWebAsync(cancellationToken);
        var ng = await this.ReadLatestPoracleNgAsync(cancellationToken);

        this._cache.Set(CacheKey, (web, ng), CacheFor);

        return (web, ng);
    }

    private async Task<string?> ReadLatestPoracleWebAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await this._httpClient.GetStringAsync(PoracleWebReleaseUrl, cancellationToken);
            using var document = JsonDocument.Parse(json);

            return document.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
                ? tag.GetString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogCheckFailed(this._logger, "PoracleWeb", ex);
            return null;
        }
    }

    private async Task<string?> ReadLatestPoracleNgAsync(CancellationToken cancellationToken)
    {
        try
        {
            // No releases and no tags on that repository, so the released number is the constant on main.
            var source = await this._httpClient.GetStringAsync(PoracleNgVersionUrl, cancellationToken);
            var match = VersionConstant().Match(source);

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogCheckFailed(this._logger, "PoracleNG", ex);
            return null;
        }
    }

    [GeneratedRegex(@"const\s+Version\s*=\s*""([^""]+)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionConstant();

    [LoggerMessage(
        EventId = 6120,
        Level = LogLevel.Debug,
        Message = "Could not read the latest published {Component} version. The update line is left blank.")]
    private static partial void LogCheckFailed(ILogger logger, string component, Exception exception);
}
