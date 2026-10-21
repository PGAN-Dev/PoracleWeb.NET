using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

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

    /// <summary>Adds the cached Discord avatars the admin list renders. See #618.</summary>
    private static void AddAvatars(UserGeofence geofence)
    {
        geofence.OwnerAvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(geofence.HumanId);

        if (!string.IsNullOrEmpty(geofence.ReviewedBy))
        {
            geofence.ReviewedByAvatarUrl = Services.AvatarCacheService.GetAvatarOrDefault(geofence.ReviewedBy);
        }
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

    /// <summary>Deletes any user's geofence. <c>alarms</c> works as on the owner's own delete.</summary>
    /// <remarks>
    /// <see cref="PoracleUnreachableExceptionFilter"/> is on this action and <see cref="GetDeleteImpact"/>,
    /// not the class: approve calls Koji's save without catching a connection failure, and a class-level
    /// filter would report that as PoracleNG being unreachable. Here Koji's cleanup and the reload catch
    /// their own failures, so what can escape is PoracleNG's.
    /// </remarks>
    [HttpDelete("{id:int}")]
    [TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
    public async Task<IActionResult> AdminDelete(int id, [FromQuery] string? alarms)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        if (!UserGeofenceController.TryParseAlarmsChoice(alarms, out var deleteWidened))
        {
            return this.BadRequest(new
            {
                error = UserGeofenceController.AlarmsChoiceError
            });
        }

        try
        {
            await this._userGeofenceService.AdminDeleteAsync(this.UserId, id, deleteWidened);
            return this.NoContent();
        }
        catch (KojiOperationException ex)
        {
            // Koji down, or a region deleted between the region list loading and the approve click. This
            // used to reach the admin as 500 "An unexpected error occurred." See #422.
            LogKojiFailure(this._logger, ex, id);
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The geofence server rejected the request. It may be unavailable, or the region may no longer exist."
            });
        }
        catch (GeofenceNotFoundException ex)
        {
            LogAdminDeleteFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            // Validation and state-machine failures are the caller's input, not a missing record. These
            // all used to come back as 404 carrying a validation message, so the SPA toasted
            // "Not found" for a submission sitting visible in the admin list. See #421.
            LogAdminDeleteFailed(this._logger, ex, id);
            return this.BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>How many of the owner's alarms are limited to this fence alone. Writes nothing.</summary>
    [HttpGet("{id:int}/delete-impact")]
    [TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
    public async Task<IActionResult> GetDeleteImpact(int id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        try
        {
            var impact = await this._userGeofenceService.AdminGetDeleteImpactAsync(id);
            return this.Ok(UserGeofenceController.DeleteImpactBody(impact));
        }
        catch (GeofenceNotFoundException ex)
        {
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
            var result = await this._userGeofenceService.ApproveSubmissionAsync(
                this.UserId, id, request?.PromotedName, request?.ParentId, request?.GroupName);
            // The SPA swaps the list row for this response, so it needs the same avatars the list
            // projection adds -- without them the card fell back to raw snowflakes. See #618.
            AddAvatars(result);
            return this.Ok(result);
        }
        catch (KojiOperationException ex)
        {
            // Koji down, or a region deleted between the region list loading and the approve click. This
            // used to reach the admin as 500 "An unexpected error occurred." See #422.
            LogKojiFailure(this._logger, ex, id);
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The geofence server rejected the request. It may be unavailable, or the region may no longer exist."
            });
        }
        catch (GeofenceNotFoundException ex)
        {
            LogApproveSubmissionFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            // Validation and state-machine failures are the caller's input, not a missing record. These
            // all used to come back as 404 carrying a validation message, so the SPA toasted
            // "Not found" for a submission sitting visible in the admin list. See #421.
            LogApproveSubmissionFailed(this._logger, ex, id);
            return this.BadRequest(new
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
            // The SPA swaps the list row for this response, so it needs the same avatars the list
            // projection adds -- without them the card fell back to raw snowflakes. See #618.
            AddAvatars(result);
            return this.Ok(result);
        }
        catch (KojiOperationException ex)
        {
            // Koji down, or a region deleted between the region list loading and the approve click. This
            // used to reach the admin as 500 "An unexpected error occurred." See #422.
            LogKojiFailure(this._logger, ex, id);
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The geofence server rejected the request. It may be unavailable, or the region may no longer exist."
            });
        }
        catch (GeofenceNotFoundException ex)
        {
            LogRejectSubmissionFailed(this._logger, ex, id);
            return this.NotFound(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            // Validation and state-machine failures are the caller's input, not a missing record. These
            // all used to come back as 404 carrying a validation message, so the SPA toasted
            // "Not found" for a submission sitting visible in the admin list. See #421.
            LogRejectSubmissionFailed(this._logger, ex, id);
            return this.BadRequest(new
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

        /// <summary>Optional Koji parent id to assign on promotion. Null keeps the submission's existing region.</summary>
        public int? ParentId
        {
            get; set;
        }

        /// <summary>Optional Koji group/region display name to assign on promotion. Null keeps the existing value.</summary>
        public string? GroupName
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Koji rejected an operation while handling geofence {GeofenceId}")]
    private static partial void LogKojiFailure(ILogger logger, Exception ex, int geofenceId);
}
