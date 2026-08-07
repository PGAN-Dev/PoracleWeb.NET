using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class DiscordNotificationService(
    HttpClient httpClient,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<DiscordNotificationService> logger) : IDiscordNotificationService
{
    /// <summary>
    /// Named HttpClient used to download the static map image. Deliberately separate from the Discord
    /// client so the bot token is never sent to the tileserver.
    /// </summary>
    public const string MapImageHttpClientName = "geofence-map-image";

    private const string MapAttachmentFileName = "geofence-map.png";

    /// <summary>Discord's per-attachment limit on the free tier is 25 MB; a static map is ~100 KB.</summary>
    private const int MaxMapImageBytes = 8 * 1024 * 1024;

    private readonly HttpClient _httpClient = httpClient;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<DiscordNotificationService> _logger = logger;
    private readonly string _forumChannelId = configuration["Discord:GeofenceForumChannelId"] ?? string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Discord payloads are authored with literal wire names (<c>auto_archive_duration</c>), so no naming
    /// policy is applied. Nulls are dropped -- Discord rejects <c>attachments: null</c>.
    /// </summary>
    private static readonly JsonSerializerOptions DiscordPayloadOptions = new()
    {
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
            LogForumChannelNotConfiguredForTags(this._logger);
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
            LogForumTagsInitialized(this._logger, s_pendingTagId, s_approvedTagId, s_rejectedTagId);
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

            // PoracleNG hands back a pregenerated tileserver URL that the tile cache eventually evicts,
            // which leaves the embed with a dead image. Upload the bytes as a real Discord attachment so
            // the map stays with the message. Fall back to the raw URL when the download fails.
            var mapImageBytes = mapImageUrl != null ? await this.TryDownloadMapImageAsync(mapImageUrl) : null;

            object? image = null;
            if (mapImageBytes != null)
            {
                image = new { url = $"attachment://{MapAttachmentFileName}" };
            }
            else if (mapImageUrl != null)
            {
                image = new { url = mapImageUrl };
            }

            var embeds = new List<object>
            {
                new
                {
                    title = $"Geofence: {geofenceName}",
                    color = 2196944, // #2196f3 as decimal
                    fields = new object[]
                    {
                        new { name = "Region", value = string.IsNullOrWhiteSpace(groupName) ? "Unassigned" : groupName, inline = true },
                        new { name = "Points", value = polygonPoints.ToString(System.Globalization.CultureInfo.InvariantCulture), inline = true },
                        new { name = "Submitted By", value = $"<@{userId}>", inline = true },
                    },
                    image,
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
                    attachments = mapImageBytes != null
                        ? new object[] { new { id = "0", filename = MapAttachmentFileName } }
                        : null,
                },
            };

            var payloadJson = JsonSerializer.Serialize(body, DiscordPayloadOptions);
            using HttpContent content = mapImageBytes != null
                ? BuildMultipartBody(payloadJson, mapImageBytes)
                : new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

            var response = await this._httpClient.PostAsync($"channels/{this._forumChannelId}/threads", content);
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

    /// <summary>
    /// Downloads the static map so it can be uploaded to Discord as an attachment. Returns null on any
    /// failure -- the caller falls back to linking the URL directly.
    /// </summary>
    private async Task<byte[]?> TryDownloadMapImageAsync(string mapImageUrl)
    {
        try
        {
            var client = this._httpClientFactory.CreateClient(MapImageHttpClientName);
            using var response = await client.GetAsync(mapImageUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                LogMapImageDownloadFailed(this._logger, mapImageUrl, (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxMapImageBytes)
            {
                LogMapImageTooLarge(this._logger, mapImageUrl, response.Content.Headers.ContentLength ?? 0);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > MaxMapImageBytes)
            {
                LogMapImageTooLarge(this._logger, mapImageUrl, bytes.Length);
                return null;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            LogMapImageDownloadError(this._logger, ex, mapImageUrl);
            return null;
        }
    }

    /// <summary>
    /// Builds the multipart body Discord expects for a message with an uploaded file: the JSON payload
    /// under <c>payload_json</c> and the file under <c>files[0]</c>, matched to <c>attachments[0].id</c>.
    /// </summary>
    private static MultipartFormDataContent BuildMultipartBody(string payloadJson, byte[] mapImageBytes)
    {
        var multipart = new MultipartFormDataContent
        {
            { new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json"), "payload_json" },
        };

        var fileContent = new ByteArrayContent(mapImageBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "files[0]", MapAttachmentFileName);

        return multipart;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord GeofenceForumChannelId is not configured; skipping forum tag setup")]
    private static partial void LogForumChannelNotConfiguredForTags(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discord forum tags initialized: Pending={PendingId}, Approved={ApprovedId}, Rejected={RejectedId}")]
    private static partial void LogForumTagsInitialized(ILogger logger, string? pendingId, string? approvedId, string? rejectedId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Static map download for the geofence embed returned HTTP {StatusCode} for {MapImageUrl}; linking the URL instead")]
    private static partial void LogMapImageDownloadFailed(ILogger logger, string mapImageUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Static map at {MapImageUrl} is {ByteCount} bytes; skipping the attachment upload")]
    private static partial void LogMapImageTooLarge(ILogger logger, string mapImageUrl, long byteCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Static map download for the geofence embed failed for {MapImageUrl}; linking the URL instead")]
    private static partial void LogMapImageDownloadError(ILogger logger, Exception ex, string mapImageUrl);
}
