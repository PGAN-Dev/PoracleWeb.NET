using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Controllers;

[ApiController]
[Route("api/geofence-feed")]
public partial class GeofenceFeedController(
    IUserGeofenceRepository repository,
    IKojiService kojiService,
    IConfiguration configuration,
    ILogger<GeofenceFeedController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Poracle-Secret";

    private readonly IUserGeofenceRepository _repository = repository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly string _apiSecret = configuration["Poracle:ApiSecret"] ?? string.Empty;
    private readonly ILogger<GeofenceFeedController> _logger = logger;

    /// <summary>
    /// Drops the cached Koji collection so the next feed read re-fetches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provisioning writes a fence into Koji and then tells PoracleNG to reload. PoracleNG reads this
    /// feed, so a reload against a cached collection re-reads the old list and still answers
    /// <c>{"status":"ok"}</c> -- the run reports success and the area is unsubscribable. The cache does
    /// expire on its own after five minutes, but a caller that wants to assert the area is pickable
    /// cannot wait an unknown fraction of that, which is why this is explicit invalidation rather than a
    /// shorter TTL. See #844.
    /// </para>
    /// <para>
    /// Authenticated with the same shared secret this site sends to PoracleNG, which is the only secret
    /// an internal caller already holds. It fails closed: with no secret configured there is nothing to
    /// check against, so every request is refused rather than every request allowed.
    /// </para>
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("geofence-feed-refresh")]
    public IActionResult RefreshKojiCache()
    {
        if (!this.SecretIsValid())
        {
            LogRefreshRefused(this._logger);
            return this.Unauthorized();
        }

        this._kojiService.InvalidateAdminGeofenceCache();
        LogRefreshAccepted(this._logger);

        return this.Ok(new { status = "ok" });
    }

    private bool SecretIsValid()
    {
        if (string.IsNullOrEmpty(this._apiSecret))
        {
            return false;
        }

        if (!this.Request.Headers.TryGetValue(SecretHeader, out var supplied))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(this._apiSecret);
        var actual = Encoding.UTF8.GetBytes(supplied.ToString());

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// Returns all geofences in Poracle-compatible format: admin geofences from Koji (with groups resolved)
    /// plus user geofences from the local DB. This is the single geofence source for PoracleJS.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPoracleFeed()
    {
        var combined = new List<object>();
        var kojiRead = true;
        var userRead = true;

        // Admin geofences from Koji (cached, with groups resolved from parent chain)
        try
        {
            var adminGeofences = await this._kojiService.GetAdminGeofencesAsync();
            combined.AddRange(adminGeofences.Select(g => new
            {
                id = g.Id,
                name = g.Name,
                group = g.Group,
                path = g.Path,
                userSelectable = g.UserSelectable,
                displayInMatches = g.DisplayInMatches,
                description = g.Description,
                color = g.Color,
            }));
        }
        catch (Exception ex)
        {
            LogFetchAdminGeofencesFailed(this._logger, ex);
            kojiRead = false;
        }

        // User geofences from local DB (not user-selectable, not displayed in matches)
        IEnumerable<UserGeofence> userGeofences = [];

        try
        {
            userGeofences = await this._repository.GetAllActiveAsync();
        }
        catch (Exception ex)
        {
            // The Koji half has always degraded; this half did not, so a database blip answered 500 and
            // took down the only geofence source PoracleJS has. See #844's follow-up.
            LogFetchUserGeofencesFailed(this._logger, ex);
            userRead = false;
        }

        var userPoracleFormat = userGeofences
            .Where(g => !string.IsNullOrEmpty(g.PolygonJson))
            .Select(g =>
            {
                double[][]? polygon = null;
                try
                {
                    polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson!);
                }
                catch (JsonException ex)
                {
                    LogDeserializePolygonFailed(this._logger, ex, g.KojiName, g.Id);
                }

                // This feed is the single geofence source for PoracleJS, so a malformed polygon from one
                // user is everyone's problem. Skip anything that is not a well-formed ring. See #410.
                if (!PolygonValidation.IsWellFormed(polygon))
                {
                    LogSkippedMalformedPolygon(this._logger, g.KojiName, g.Id);
                    return null;
                }

                return (object?)new
                {
                    id = g.Id,
                    name = g.KojiName,
                    path = polygon,
                    userSelectable = false,
                    displayInMatches = false,
                };
            })
            .Where(g => g != null);

        combined.AddRange(userPoracleFormat!);

        if (!kojiRead && !userRead)
        {
            // Never answer 200 with an empty list when BOTH sources failed. PoracleJS treats this feed as
            // authoritative and caches the last good response, so an empty success is not a degraded
            // answer -- it is an instruction to drop every geofence every user has. An error leaves the
            // cache in place. Emptiness is only legitimate when we genuinely read nothing.
            LogFeedUnavailable(this._logger);
            return this.StatusCode(503, new { status = "error", message = "Geofence sources unavailable" });
        }

        return this.Ok(new
        {
            status = "ok",
            data = combined,
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Koji geofence cache dropped on request; the next feed read will re-fetch")]
    private static partial void LogRefreshAccepted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a Koji geofence cache refresh: the shared secret did not match, or none is configured")]
    private static partial void LogRefreshRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read user geofences — serving Koji geofences only")]
    private static partial void LogFetchUserGeofencesFailed(ILogger logger, Exception ex);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Both geofence sources failed; answering 503 so PoracleJS keeps its cached list rather than dropping every area")]
    private static partial void LogFeedUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to fetch admin geofences from Koji — serving user geofences only")]
    private static partial void LogFetchAdminGeofencesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize polygon for geofence '{KojiName}' (ID {Id})")]
    private static partial void LogDeserializePolygonFailed(ILogger logger, Exception ex, string? kojiName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped malformed polygon for geofence '{KojiName}' (id {Id}) when building the feed")]
    private static partial void LogSkippedMalformedPolygon(ILogger logger, string? kojiName, int id);
}
