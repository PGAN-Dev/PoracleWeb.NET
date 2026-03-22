using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[ApiController]
[Route("api/geofence-feed")]
public partial class GeofenceFeedController(
    IUserGeofenceRepository repository,
    IKojiService kojiService,
    ILogger<GeofenceFeedController> logger) : ControllerBase
{
    private readonly IUserGeofenceRepository _repository = repository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly ILogger<GeofenceFeedController> _logger = logger;

    /// <summary>
    /// Returns all geofences in Poracle-compatible format: admin geofences from Koji (with groups resolved)
    /// plus user geofences from the local DB. This is the single geofence source for PoracleJS.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPoracleFeed()
    {
        var combined = new List<object>();

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
            LogFetchAdminGeofencesFailed(_logger, ex);
        }

        // User geofences from local DB (not user-selectable, not displayed in matches)
        var userGeofences = await this._repository.GetAllActiveAsync();
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
                    LogDeserializePolygonFailed(_logger, ex, g.KojiName, g.Id);
                }

                if (polygon == null || polygon.Length < 3)
                {
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

        return this.Ok(new
        {
            status = "ok",
            data = combined,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to fetch admin geofences from Koji — serving user geofences only")]
    private static partial void LogFetchAdminGeofencesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize polygon for geofence '{KojiName}' (ID {Id})")]
    private static partial void LogDeserializePolygonFailed(ILogger logger, Exception ex, string? kojiName, int id);
}
