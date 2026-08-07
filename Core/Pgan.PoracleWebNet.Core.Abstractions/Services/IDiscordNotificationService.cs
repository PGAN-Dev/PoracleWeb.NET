using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IDiscordNotificationService
{
    /// <summary>Opens the review thread. Returns the thread ID, which is also the starter message ID.</summary>
    public Task<string?> CreateGeofenceSubmissionPostAsync(GeofenceSubmissionPost post);

    /// <summary>
    /// Rewrites the opening embed so the thread reflects its outcome, then posts the verdict as a reply and
    /// locks the thread.
    /// </summary>
    public Task PostReviewOutcomeAsync(string threadId, GeofenceSubmissionPost post);

    public Task EnsureForumTagsExistAsync();
}
