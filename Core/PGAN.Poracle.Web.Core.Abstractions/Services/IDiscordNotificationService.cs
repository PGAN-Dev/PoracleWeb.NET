namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IDiscordNotificationService
{
    Task<string?> CreateGeofenceSubmissionPostAsync(string userId, string userName, string geofenceName, string groupName, int polygonPoints, string? mapImageUrl);
    Task PostApprovalMessageAsync(string threadId, string geofenceName, string promotedName);
    Task PostRejectionMessageAsync(string threadId, string geofenceName, string reason);
    Task EnsureForumTagsExistAsync();
}
