using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/geofences")]
public class UserGeofenceController(IUserGeofenceService userGeofenceService, ILogger<UserGeofenceController> logger) : BaseApiController
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
            this._logger.LogWarning(ex, "Failed to create custom geofence for user {UserId}", this.UserId);
            return this.BadRequest(new { error = ex.Message });
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
            return this.NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogWarning(ex, "User {UserId} attempted to delete geofence ID {Id} they don't own", this.UserId, id);
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
            return this.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogWarning(ex, "User {UserId} attempted to submit geofence '{KojiName}' they don't own", this.UserId, kojiName);
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
            this._logger.LogWarning(ex, "Failed to fetch geofence regions from Koji");
            return this.Ok(Array.Empty<object>());
        }
    }
}
