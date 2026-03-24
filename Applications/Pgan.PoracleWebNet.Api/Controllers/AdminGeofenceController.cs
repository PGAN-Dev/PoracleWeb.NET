using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/admin/geofences")]
public partial class AdminGeofenceController(IUserGeofenceService userGeofenceService, ILogger<AdminGeofenceController> logger) : BaseApiController
{
    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
    private readonly ILogger<AdminGeofenceController> _logger = logger;

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var geofences = await this._userGeofenceService.GetAllWithDetailsAsync();

        foreach (var geofence in geofences)
        {
            geofence.OwnerAvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(geofence.HumanId);

            if (!string.IsNullOrEmpty(geofence.ReviewedBy))
            {
                geofence.ReviewedByAvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(geofence.ReviewedBy);
            }
        }

        return this.Ok(geofences);
    }

    [HttpGet("submissions")]
    public async Task<IActionResult> GetSubmissions()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var submissions = await this._userGeofenceService.GetPendingSubmissionsAsync();
        return this.Ok(submissions);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> AdminDelete(int id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        try
        {
            await this._userGeofenceService.AdminDeleteAsync(this.UserId, id);
            return this.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            LogAdminDeleteFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("submissions/{id:int}/approve")]
    public async Task<IActionResult> ApproveSubmission(int id, [FromBody] ApproveRequest? request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        try
        {
            var result = await this._userGeofenceService.ApproveSubmissionAsync(this.UserId, id, request?.PromotedName);
            return this.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            LogApproveSubmissionFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("submissions/{id:int}/reject")]
    public async Task<IActionResult> RejectSubmission(int id, [FromBody] RejectRequest request)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        try
        {
            var result = await this._userGeofenceService.RejectSubmissionAsync(this.UserId, id, request.ReviewNotes);
            return this.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            LogRejectSubmissionFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
    }

    public class ApproveRequest
    {
        public string? PromotedName
        {
            get; set;
        }
    }

    public class RejectRequest
    {
        public string ReviewNotes { get; set; } = string.Empty;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to admin delete geofence {Id}")]
    private static partial void LogAdminDeleteFailed(ILogger logger, Exception ex, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to approve geofence submission {Id}")]
    private static partial void LogApproveSubmissionFailed(ILogger logger, Exception ex, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reject geofence submission {Id}")]
    private static partial void LogRejectSubmissionFailed(ILogger logger, Exception ex, int id);
}
