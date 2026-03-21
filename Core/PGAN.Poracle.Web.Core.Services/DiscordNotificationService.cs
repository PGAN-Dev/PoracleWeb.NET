using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Core.Services;

public class DiscordNotificationService(
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

    // Cached tag IDs
    private string? _pendingTagId;
    private string? _approvedTagId;
    private string? _rejectedTagId;
    private bool _tagsInitialized;

    public async Task EnsureForumTagsExistAsync()
    {
        if (_tagsInitialized)
        {
            return;
        }

        if (string.IsNullOrEmpty(_forumChannelId))
        {
            _logger.LogWarning("Discord GeofenceForumChannelId is not configured; skipping forum tag setup");
            return;
        }

        try
        {
            // GET the forum channel to read existing tags
            var response = await _httpClient.GetAsync($"channels/{_forumChannelId}");
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
                        _pendingTagId = id;
                        break;
                    case "Geofence - Approved":
                        _approvedTagId = id;
                        break;
                    case "Geofence - Rejected":
                        _rejectedTagId = id;
                        break;
                }
            }

            // Build new tags list if any are missing
            if (_pendingTagId == null || _approvedTagId == null || _rejectedTagId == null)
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

                if (_pendingTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Pending",
                        ["emoji_name"] = "\U0001F4CB",
                    });
                }

                if (_approvedTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Approved",
                        ["emoji_name"] = "\u2705",
                    });
                }

                if (_rejectedTagId == null)
                {
                    tagsToKeep.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "Geofence - Rejected",
                        ["emoji_name"] = "\u274C",
                    });
                }

                // PATCH the channel with updated tags
                var patchBody = new { available_tags = tagsToKeep };
                var patchResponse = await _httpClient.PatchAsJsonAsync($"channels/{_forumChannelId}", patchBody);
                patchResponse.EnsureSuccessStatusCode();

                // Re-read the channel to get the newly assigned tag IDs
                var refreshResponse = await _httpClient.GetAsync($"channels/{_forumChannelId}");
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
                                _pendingTagId = id;
                                break;
                            case "Geofence - Approved":
                                _approvedTagId = id;
                                break;
                            case "Geofence - Rejected":
                                _rejectedTagId = id;
                                break;
                        }
                    }
                }
            }

            _tagsInitialized = true;
            _logger.LogInformation("Discord forum tags initialized: Pending={PendingId}, Approved={ApprovedId}, Rejected={RejectedId}",
                _pendingTagId, _approvedTagId, _rejectedTagId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Discord forum tags");
        }
    }

    public async Task<string?> CreateGeofenceSubmissionPostAsync(string userId, string userName, string geofenceName, string groupName, int polygonPoints, string? mapImageUrl)
    {
        if (string.IsNullOrEmpty(_forumChannelId))
        {
            _logger.LogDebug("Discord GeofenceForumChannelId is not configured; skipping submission post");
            return null;
        }

        await EnsureForumTagsExistAsync();

        try
        {
            var appliedTags = _pendingTagId != null ? new[] { _pendingTagId } : Array.Empty<string>();

            var embeds = new List<object>
            {
                new
                {
                    title = $"Geofence: {geofenceName}",
                    color = 2196944, // #2196f3 as decimal
                    fields = new object[]
                    {
                        new { name = "Region", value = groupName, inline = true },
                        new { name = "Points", value = polygonPoints.ToString(), inline = true },
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

            var response = await _httpClient.PostAsJsonAsync($"channels/{_forumChannelId}/threads", body);
            response.EnsureSuccessStatusCode();

            var threadJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var threadId = threadJson.GetProperty("id").GetString();

            _logger.LogInformation("Created Discord forum post for geofence '{GeofenceName}', threadId={ThreadId}", geofenceName, threadId);

            return threadId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Discord forum post for geofence '{GeofenceName}'", geofenceName);
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
            var messageResponse = await _httpClient.PostAsJsonAsync($"channels/{threadId}/messages", messageBody);
            messageResponse.EnsureSuccessStatusCode();

            // Update tags and lock/archive the thread
            await EnsureForumTagsExistAsync();
            var appliedTags = _approvedTagId != null ? new[] { _approvedTagId } : Array.Empty<string>();
            var patchBody = new
            {
                applied_tags = appliedTags,
                locked = true,
                archived = true,
            };
            var patchResponse = await _httpClient.PatchAsJsonAsync($"channels/{threadId}", patchBody);
            patchResponse.EnsureSuccessStatusCode();

            _logger.LogInformation("Posted approval to Discord thread {ThreadId} for geofence '{GeofenceName}'", threadId, geofenceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post approval to Discord thread {ThreadId}", threadId);
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
            var messageResponse = await _httpClient.PostAsJsonAsync($"channels/{threadId}/messages", messageBody);
            messageResponse.EnsureSuccessStatusCode();

            // Update tags and lock/archive the thread
            await EnsureForumTagsExistAsync();
            var appliedTags = _rejectedTagId != null ? new[] { _rejectedTagId } : Array.Empty<string>();
            var patchBody = new
            {
                applied_tags = appliedTags,
                locked = true,
                archived = true,
            };
            var patchResponse = await _httpClient.PatchAsJsonAsync($"channels/{threadId}", patchBody);
            patchResponse.EnsureSuccessStatusCode();

            _logger.LogInformation("Posted rejection to Discord thread {ThreadId} for geofence '{GeofenceName}'", threadId, geofenceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post rejection to Discord thread {ThreadId}", threadId);
        }
    }
}
