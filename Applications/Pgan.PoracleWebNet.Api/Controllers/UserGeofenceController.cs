using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Api.Filters;
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

    /// <summary>Renames a geofence without disturbing which profiles are subscribed to it.</summary>
    /// <remarks>
    /// The page used to edit by deleting and recreating, which re-subscribed only the active profile and
    /// silently switched the geofence off everywhere else. See #543.
    /// </remarks>
    [HttpPut("custom/{id:int}")]
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
    public async Task<IActionResult> RenameGeofence(int id, [FromBody] UserGeofenceRenameRequest request)
    {
        try
        {
            var updated = await this._userGeofenceService.RenameAsync(
                this.UserId, id, request.DisplayName, request.GroupName, request.ParentId);
            return this.Ok(updated);
        }
        catch (GeofenceNotFoundException)
        {
            return this.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            // Not Forbid: a geofence the caller does not own should not be distinguishable from one that
            // does not exist.
            return this.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // The rename status guard (#646) threw straight past these arms as an unhandled 500.
            // CreateGeofence already has this arm; rename was not given it. See #657.
            return this.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return this.BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("custom")]
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
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

    /// <summary>Removes one of the caller's own geofences.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately not behind <see cref="DisableFeatureKeys.UserGeofences"/>, unlike every other
    /// mutation here. Switching the feature off hides the page and refuses new work, but the fences
    /// that already exist keep being served in the geofence feed and keep matching — so gating this
    /// too would leave someone receiving alerts from an area they can neither edit nor remove.
    /// </para>
    /// <para>
    /// This is where geofences differ from the alarm types, whose controllers gate the whole class
    /// and so refuse deletes as well. Those users still have the bot: <c>!untrack</c> removes an
    /// alarm whatever the web says. Geofences are PoracleWeb-only and the bot has no equivalent
    /// command, so this endpoint is the only route a user has to their own data. Production
    /// carries 42 of them, so the stranding is not hypothetical.
    /// </para>
    /// <para>Pinned by a test, because this reads like an oversight and has been reported as one.</para>
    /// </remarks>
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
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
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

    // These write area subscriptions, so they answer to the same switch the Areas page does.
    // Gated per-action because the reads on this controller stay open; disable_areas is
    // enforced in the service, since the attribute does not allow two keys. See #478.
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
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

    // These write area subscriptions, so they answer to the same switch the Areas page does.
    // Gated per-action because the reads on this controller stay open; disable_areas is
    // enforced in the service, since the attribute does not allow two keys. See #478.
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
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
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            LogExportGeoJsonFailed(this._logger, ex, this.UserId);
            return this.StatusCode(500, new
            {
                error = "Failed to export geofences"
            });
        }
    }

    [HttpPost("import/geojson")]
    [RequireFeatureEnabled(DisableFeatureKeys.UserGeofences)]
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
        catch (OperationCanceledException)
        {
            return this.BadRequest(new
            {
                error = "Import operation was canceled"
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

public record UserGeofenceRenameRequest(string DisplayName, string? GroupName, int? ParentId);
