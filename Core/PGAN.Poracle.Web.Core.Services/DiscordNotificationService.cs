using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Core.Services;

public partial class DiscordNotificationService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<DiscordNotificationService> logger) : IDiscordNotificationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<DiscordNotificationService> _logger = logger;
    private readonly string _forumChannelId = configuration["Discord:GeofenceForumChannelId"] ?? string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Cached tag IDs (static so they persist across transient HttpClient instances)
    private static string? s_pendingTagId;
    private static string? s_approvedTagId;
    private static string? s_rejectedTagId;
    private static bool s_tagsInitialized;

    public async Task EnsureForumTagsExistAsync()
    {
        if (s_tagsInitialized)
        {
            return;
        }

        if (string.IsNullOrEmpty(this._forumChannelId))
        {
            this._logger.LogWarning("Discord GeofenceForumChannelId is not configured; skipping forum tag setup");
            return;
        }

        try
        {
            // GET the forum channel to read existing tags
            var response = await this._httpClient.GetAsync($"channels/{this._forumChannelId}");
            response.EnsureSuccessStatusCode();

            var channelJson = await response.Content.ReadFromJsonAsync<JsonElement>();

            var existingTags = new List<JsonElement>();
            if (channelJson.TryGetProperty("available_tags", out var tagsElement))
            {
                foreach (var tag in tagsElement.EnumerateArray())
                {
                    existingTags.Add(tag);
                }
            }

            // Check which tags already exist
            foreach (var tag in existingTags)
            {
                var name = tag.GetProperty("name").GetString();
                var id = tag.GetProperty("id").GetString();
                switch (name)
                {
                    case "Geofence - Pending":
                        s_pendingTagId = id;
                        break;
                    case "Geofence - Approved":
                        s_approvedTagId = id;
                        break;
                    case "Geofence - Rejected":
                        s_rejectedTagId = id;
                        break;
                    default:
                        break;
                }
            }

            // Build new tags list if any are missing
            if (s_pendingTagId == null || s_approvedTagId == null || s_rejectedTagId == null)
            {
                var tagsToKeep = new List<object>();

                // Keep existing tags as raw dictionaries
                foreach (var tag in existingTags)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["id"] = tag.GetProperty("id").GetString(),
                        ["name"] = tag.GetProperty("name").GetString(),
                    });
                }

                if (s_pendingTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Pending",
                        ["emoji_name"] = "\U0001F4CB",
                    });
                }

                if (s_approvedTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Approved",
                        ["emoji_name"] = "\u2705",
                    });
                }

                if (s_rejectedTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Rejected",
                        ["emoji_name"] = "\u274C",
                    });
                }

                // PATCH the channel with updated tags
                var patchBody = new
                {
                    available_tags = tagsToKeep
                };
                var patchResponse = await this._httpClient.PatchAsJsonAsync($"channels/{this._forumChannelId}", patchBody);
                patchResponse.EnsureSuccessStatusCode();

                // Re-read the channel to get the newly assigned tag IDs
                var refreshResponse = await this._httpClient.GetAsync($"channels/{this._forumChannelId}");
                refreshResponse.EnsureSuccessStatusCode();

                var refreshedChannel = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
                if (refreshedChannel.TryGetProperty("available_tags", out var refreshedTags))
                {
                    foreach (var tag in refreshedTags.EnumerateArray())
                    {
                        var name = tag.GetProperty("name").GetString();
                        var id = tag.GetProperty("id").GetString();
                        switch (name)
                        {
                            case "Geofence - Pending":
                                s_pendingTagId = id;
                                break;
                            case "Geofence - Approved":
                                s_approvedTagId = id;
                                break;
                            case "Geofence - Rejected":
                                s_rejectedTagId = id;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }

            s_tagsInitialized = true;
            this._logger.LogInformation("Discord forum tags initialized: Pending={PendingId}, Approved={ApprovedId}, Rejected={RejectedId}",
                s_pendingTagId, s_approvedTagId, s_rejectedTagId);
        }
        catch (Exception ex)
        {
            LogForumTagInitFailed(this._logger, ex);
        }
    }

    public async Task<string?> CreateGeofenceSubmissionPostAsync(string userId, string userName, string geofenceName, string groupName, int polygonPoints, string? mapImageUrl)
    {
        if (string.IsNullOrEmpty(this._forumChannelId))
        {
            LogForumChannelNotConfigured(this._logger);
            return null;
        }

        await this.EnsureForumTagsExistAsync();

        try
        {
            var appliedTags = s_pendingTagId != null ? [s_pendingTagId] : Array.Empty<string>();

            var embeds = new List<object>
            {
                new
                {
                    title = $"Geofence: {geofenceName}",
                    color = 2196944, // #2196f3 as decimal
                    fields = new object[]
                    {
                        new { name = "Region", value = groupName, inline = true },
                        new { name = "Points", value = polygonPoints.ToString(System.Globalization.CultureInfo.InvariantCulture), inline = true },
                        new { name = "Submitted By", value = $"<@{userId}>", inline = true },
                    },
                    image = mapImageUrl != null ? new { url = mapImageUrl } : null,
                },
            };

            var body = new
            {
                name = $"Geofence Request: {geofenceName}",
                auto_archive_duration = 10080,
                applied_tags = appliedTags,
                message = new
                {
                    content = "A custom geofence has been submitted for review.\n\n"
                        + "Please share any context about this area (community day spot, park, popular route, etc.)",
                    embeds,
                },
            };

            var response = await this._httpClient.PostAsJsonAsync($"channels/{this._forumChannelId}/threads", body);
            response.EnsureSuccessStatusCode();

            var threadJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var threadId = threadJson.GetProperty("id").GetString();

            LogForumPostCreated(this._logger, geofenceName, threadId);

            return threadId;
        }
        catch (Exception ex)
        {
            LogForumPostFailed(this._logger, ex, geofenceName);
            return null;
        }
    }

    public async Task PostApprovalMessageAsync(string threadId, string geofenceName, string promotedName)
    {
        try
        {
            // Post approval message
            var messageBody = new
            {
                content = $"\u2705 **Approved!** This geofence has been published as **{promotedName}** and is now available to all users on the Areas page.",
            };
            var messageResponse = await this._httpClient.PostAsJsonAsync($"channels/{threadId}/messages", messageBody);
            messageResponse.EnsureSuccessStatusCode();

            // Update tags and lock/archive the thread
            await this.EnsureForumTagsExistAsync();
            var appliedTags = s_approvedTagId != null ? [s_approvedTagId] : Array.Empty<string>();
            var patchBody = new
            {
                applied_tags = appliedTags,
                locked = true,
                archived = true,
            };
            var patchResponse = await this._httpClient.PatchAsJsonAsync($"channels/{threadId}", patchBody);
            patchResponse.EnsureSuccessStatusCode();

            LogApprovalPosted(this._logger, threadId, geofenceName);
        }
        catch (Exception ex)
        {
            LogApprovalFailed(this._logger, ex, threadId);
        }
    }

    public async Task PostRejectionMessageAsync(string threadId, string geofenceName, string reason)
    {
        try
        {
            // Post rejection message
            var messageBody = new
            {
                content = $"\u274C **Rejected.** {reason}\n\nYour geofence will continue to work privately for your own alerts.",
            };
            var messageResponse = await this._httpClient.PostAsJsonAsync($"channels/{threadId}/messages", messageBody);
            messageResponse.EnsureSuccessStatusCode();

            // Update tags and lock/archive the thread
            await this.EnsureForumTagsExistAsync();
            var appliedTags = s_rejectedTagId != null ? [s_rejectedTagId] : Array.Empty<string>();
            var patchBody = new
            {
                applied_tags = appliedTags,
                locked = true,
                archived = true,
            };
            var patchResponse = await this._httpClient.PatchAsJsonAsync($"channels/{threadId}", patchBody);
            patchResponse.EnsureSuccessStatusCode();

            LogRejectionPosted(this._logger, threadId, geofenceName);
        }
        catch (Exception ex)
        {
            LogRejectionFailed(this._logger, ex, threadId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialize Discord forum tags")]
    private static partial void LogForumTagInitFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discord GeofenceForumChannelId is not configured; skipping submission post")]
    private static partial void LogForumChannelNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Discord forum post for geofence '{GeofenceName}', threadId={ThreadId}")]
    private static partial void LogForumPostCreated(ILogger logger, string geofenceName, string? threadId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Discord forum post for geofence '{GeofenceName}'")]
    private static partial void LogForumPostFailed(ILogger logger, Exception ex, string geofenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted approval to Discord thread {ThreadId} for geofence '{GeofenceName}'")]
    private static partial void LogApprovalPosted(ILogger logger, string threadId, string geofenceName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post approval to Discord thread {ThreadId}")]
    private static partial void LogApprovalFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted rejection to Discord thread {ThreadId} for geofence '{GeofenceName}'")]
    private static partial void LogRejectionPosted(ILogger logger, string threadId, string geofenceName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post rejection to Discord thread {ThreadId}")]
    private static partial void LogRejectionFailed(ILogger logger, Exception ex, string threadId);
}
