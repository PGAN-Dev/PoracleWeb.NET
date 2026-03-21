using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/admin/geofences")]
public class AdminGeofenceController(IUserGeofenceService userGeofenceService, ILogger<AdminGeofenceController> logger) : BaseApiController
{
    private readonly IUserGeofenceService _userGeofenceService = userGeofenceService;
    private readonly ILogger<AdminGeofenceController> _logger = logger;

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
            this._logger.LogWarning(ex, "Failed to approve geofence submission {Id}", id);
            return this.NotFound(new { error = ex.Message });
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
            this._logger.LogWarning(ex, "Failed to reject geofence submission {Id}", id);
            return this.NotFound(new { error = ex.Message });
        }
    }

    public class ApproveRequest
    {
        public string? PromotedName { get; set; }
    }

    public class RejectRequest
    {
        public string ReviewNotes { get; set; } = string.Empty;
    }
}
