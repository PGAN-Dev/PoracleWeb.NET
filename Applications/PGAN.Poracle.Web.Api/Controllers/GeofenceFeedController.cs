using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;

namespace PGAN.Poracle.Web.Api.Controllers;

[ApiController]
[Route("api/geofence-feed")]
public class GeofenceFeedController(IUserGeofenceRepository repository, ILogger<GeofenceFeedController> logger) : ControllerBase
{
    private readonly IUserGeofenceRepository _repository = repository;
    private readonly ILogger<GeofenceFeedController> _logger = logger;

    /// <summary>
    /// Returns all active/pending_review user geofences in Poracle-compatible format.
    /// PoracleJS loads from this URL alongside Koji as a geofence.path source.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPoracleFeed()
    {
        var geofences = await this._repository.GetAllActiveAsync();

        var poracleFormat = geofences
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
                    this._logger.LogWarning(ex, "Failed to deserialize polygon for geofence '{KojiName}' (ID {Id})", g.KojiName, g.Id);
                }

                if (polygon == null || polygon.Length < 3)
                {
                    return null;
                }

                return new
                {
                    id = g.Id,
                    name = g.KojiName,
                    path = polygon,
                    userSelectable = false,
                    displayInMatches = false,
                };
            })
            .Where(g => g != null)
            .ToList();

        return this.Ok(new
        {
            status = "ok",
            data = poracleFormat
        });
    }
}
