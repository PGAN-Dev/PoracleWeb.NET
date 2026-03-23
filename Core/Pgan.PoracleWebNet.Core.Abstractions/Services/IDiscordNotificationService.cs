namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IDiscordNotificationService
{
    public Task<string?> CreateGeofenceSubmissionPostAsync(string userId, string userName, string geofenceName, string groupName, int polygonPoints, string? mapImageUrl);
    public Task PostApprovalMessageAsync(string threadId, string geofenceName, string promotedName);
    public Task PostRejectionMessageAsync(string threadId, string geofenceName, string reason);
    public Task EnsureForumTagsExistAsync();
}
