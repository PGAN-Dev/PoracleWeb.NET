using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/geofences")]
public partial class UserGeofenceController(
    IUserGeofenceService userGeofenceService,
    IGeoJsonService geoJsonService,
    ILogger<UserGeofenceController> logger) : BaseApiController
{
    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
    private readonly IGeoJsonService _geoJsonService = geoJsonService;
    private readonly ILogger<UserGeofenceController> _logger = logger;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [HttpGet("custom")]
    public async Task<IActionResult> GetCustomGeofences()
    {
        var geofences = await this._userGeofenceService.GetByUserAsync(this.UserId);
        return this.Ok(geofences);
    }

    [HttpPost("custom")]
    public async Task<IActionResult> CreateGeofence([FromBody] UserGeofenceCreate model)
    {
        try
        {
            var result = await this._userGeofenceService.CreateAsync(this.UserId, this.ProfileNo, model);
            return this.CreatedAtAction(nameof(GetCustomGeofences), result);
        }
        catch (InvalidOperationException ex)
        {
            LogCreateGeofenceFailed(this._logger, ex, this.UserId);
            return this.BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    [HttpDelete("custom/{id:int}")]
    public async Task<IActionResult> DeleteGeofence(int id)
    {
        try
        {
            await this._userGeofenceService.DeleteAsync(this.UserId, this.ProfileNo, id);
            return this.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            LogDeleteGeofenceUnauthorized(this._logger, ex, this.UserId, id);
            return this.Forbid();
        }
    }

    [HttpPost("custom/{kojiName}/submit")]
    public async Task<IActionResult> SubmitForReview(string kojiName)
    {
        try
        {
            var result = await this._userGeofenceService.SubmitForReviewAsync(this.UserId, kojiName);
            return this.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            LogSubmitGeofenceUnauthorized(this._logger, ex, this.UserId, kojiName);
            return this.Forbid();
        }
    }

    [HttpPost("custom/{id:int}/activate")]
    public async Task<IActionResult> ActivateGeofence(int id)
    {
        try
        {
            await this._userGeofenceService.AddToProfileAsync(this.UserId, this.ProfileNo, id);
            return this.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            LogActivateGeofenceUnauthorized(this._logger, ex, this.UserId, id);
            return this.Forbid();
        }
    }

    [HttpPost("custom/{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateGeofence(int id)
    {
        try
        {
            await this._userGeofenceService.RemoveFromProfileAsync(this.UserId, this.ProfileNo, id);
            return this.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            LogDeactivateGeofenceUnauthorized(this._logger, ex, this.UserId, id);
            return this.Forbid();
        }
    }

    [HttpGet("regions")]
    public async Task<IActionResult> GetRegions()
    {
        try
        {
            var regions = await this._userGeofenceService.GetRegionsAsync();
            return this.Ok(regions);
        }
        catch (Exception ex)
        {
            LogFetchRegionsFailed(this._logger, ex);
            return this.Ok(Array.Empty<object>());
        }
    }

    [HttpGet("export/geojson")]
    public async Task<IActionResult> ExportGeoJson()
    {
        try
        {
            var featureCollection = await this._geoJsonService.ExportAsync(this.UserId);
            var json = JsonSerializer.Serialize(featureCollection, ExportJsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return this.File(bytes, "application/geo+json", $"geofences-{this.UserId}.geojson");
        }
        catch (Exception ex)
        {
            LogExportGeoJsonFailed(this._logger, ex, this.UserId);
            return this.StatusCode(500, new
            {
                error = "Failed to export geofences"
            });
        }
    }

    [HttpPost("import/geojson")]
    [EnableRateLimiting("geojson-import")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> ImportGeoJson(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return this.BadRequest(new
            {
                error = "No file provided"
            });
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return this.BadRequest(new
            {
                error = "File size exceeds 5MB limit"
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await this._geoJsonService.ImportAsync(this.UserId, this.ProfileNo, stream);
            return this.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (JsonException)
        {
            return this.BadRequest(new
            {
                error = "Invalid GeoJSON file"
            });
        }
        catch (Exception ex)
        {
            LogImportGeoJsonFailed(this._logger, ex, this.UserId);
            return this.StatusCode(500, new
            {
                error = "Failed to import geofences"
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create custom geofence for user {UserId}")]
    private static partial void LogCreateGeofenceFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to delete geofence ID {Id} they don't own")]
    private static partial void LogDeleteGeofenceUnauthorized(ILogger logger, Exception ex, string userId, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to submit geofence '{KojiName}' they don't own")]
    private static partial void LogSubmitGeofenceUnauthorized(ILogger logger, Exception ex, string userId, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to activate geofence ID {Id} they don't own")]
    private static partial void LogActivateGeofenceUnauthorized(ILogger logger, Exception ex, string userId, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to deactivate geofence ID {Id} they don't own")]
    private static partial void LogDeactivateGeofenceUnauthorized(ILogger logger, Exception ex, string userId, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch geofence regions from Koji")]
    private static partial void LogFetchRegionsFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to export GeoJSON for user {UserId}")]
    private static partial void LogExportGeoJsonFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to import GeoJSON for user {UserId}")]
    private static partial void LogImportGeoJsonFailed(ILogger logger, Exception ex, string userId);
}
