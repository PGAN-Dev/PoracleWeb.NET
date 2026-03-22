using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/geofences")]
public partial class UserGeofenceController(IUserGeofenceService userGeofenceService, ILogger<UserGeofenceController> logger) : BaseApiController
{
    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
    private readonly ILogger<UserGeofenceController> _logger = logger;

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create custom geofence for user {UserId}")]
    private static partial void LogCreateGeofenceFailed(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to delete geofence ID {Id} they don't own")]
    private static partial void LogDeleteGeofenceUnauthorized(ILogger logger, Exception ex, string userId, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} attempted to submit geofence '{KojiName}' they don't own")]
    private static partial void LogSubmitGeofenceUnauthorized(ILogger logger, Exception ex, string userId, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch geofence regions from Koji")]
    private static partial void LogFetchRegionsFailed(ILogger logger, Exception ex);
}
