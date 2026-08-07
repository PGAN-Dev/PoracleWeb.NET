using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

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

    public async Task<string?> CreateGeofenceSubmissionPostAsync(GeofenceSubmissionPost post)
    {
        if (string.IsNullOrEmpty(this._forumChannelId))
        {
            LogForumChannelNotConfigured(this._logger);
            return null;
        }

        await this.EnsureForumTagsExistAsync();

        try
        {
            // PoracleNG hands back a pregenerated tileserver URL that the tile cache eventually evicts,
            // which leaves the embed with a dead image. Upload the bytes as a real Discord attachment so
            // the map stays with the message. Fall back to the raw URL when the download fails.
            var mapImageBytes = post.MapImageUrl != null ? await this.TryDownloadMapImageAsync(post.MapImageUrl) : null;

            var body = new
            {
                name = $"Geofence Request: {post.DisplayName}",
                auto_archive_duration = 10080,
                applied_tags = TagsFor(post.State),
                message = new
                {
                    content = $"<@{post.UserId}> submitted an area for review.\n\n"
                        + "Please share any context about it (community day spot, park, popular route, etc.)",
                    embeds = new object[] { BuildEmbed(post, mapImageBytes != null) },
                    attachments = AttachmentsFor(mapImageBytes),
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

            LogForumPostCreated(this._logger, post.DisplayName, threadId);

            return threadId;
        }
        catch (Exception ex)
        {
            LogForumPostFailed(this._logger, ex, post.DisplayName);
            return null;
        }
    }

    public async Task PostReviewOutcomeAsync(string threadId, GeofenceSubmissionPost post)
    {
        await this.EnsureForumTagsExistAsync();

        // A forum post's starter message shares the thread's ID, so the opening embed can be rewritten
        // without storing a separate message ID. Best-effort: a failed edit must not stop the verdict reply.
        try
        {
            await this.UpdateOpeningEmbedAsync(threadId, post);
        }
        catch (Exception ex)
        {
            LogOpeningEmbedUpdateFailed(this._logger, ex, threadId);
        }

        try
        {
            var messageBody = new { content = VerdictText(post) };
            var messageResponse = await this._httpClient.PostAsJsonAsync($"channels/{threadId}/messages", messageBody);
            messageResponse.EnsureSuccessStatusCode();

            var patchBody = new
            {
                applied_tags = TagsFor(post.State),
                locked = true,
                archived = true,
            };
            var patchResponse = await this._httpClient.PatchAsJsonAsync($"channels/{threadId}", patchBody);
            patchResponse.EnsureSuccessStatusCode();

            LogOutcomePosted(this._logger, threadId, post.DisplayName, post.State.ToString());
        }
        catch (Exception ex)
        {
            LogOutcomeFailed(this._logger, ex, threadId);
        }
    }

    /// <summary>
    /// Rewrites the starter message's embed in place. The map is re-referenced as <c>attachment://</c> and
    /// the existing attachment is retained by ID, so Discord re-resolves it rather than us re-uploading
    /// (or worse, persisting the expiring signed CDN URL as an external link).
    /// </summary>
    private async Task UpdateOpeningEmbedAsync(string threadId, GeofenceSubmissionPost post)
    {
        var existing = await this._httpClient.GetAsync($"channels/{threadId}/messages/{threadId}");
        existing.EnsureSuccessStatusCode();

        var message = await existing.Content.ReadFromJsonAsync<JsonElement>();

        string? attachmentId = null;
        if (message.TryGetProperty("attachments", out var attachments) && attachments.GetArrayLength() > 0)
        {
            attachmentId = attachments[0].GetProperty("id").GetString();
        }

        var body = new
        {
            embeds = new object[] { BuildEmbed(post, attachmentId != null) },
            attachments = attachmentId != null ? new object[] { new { id = attachmentId } } : Array.Empty<object>(),
        };

        var payloadJson = JsonSerializer.Serialize(body, DiscordPayloadOptions);
        using var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

        var response = await this._httpClient.PatchAsync($"channels/{threadId}/messages/{threadId}", content);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Builds the review card. Field order is the reading order a reviewer needs: how big, where, what it
    /// would be called. Vertex count is deliberately absent -- it never changes an approve/reject decision.
    /// </summary>
    private static object BuildEmbed(GeofenceSubmissionPost post, bool hasAttachment)
    {
        var fields = new List<object>
        {
            new
            {
                name = "Size",
                value = $"{post.AreaSqKm.ToString("0.##", CultureInfo.InvariantCulture)} km²\n{GeoMath.DescribeArea(post.AreaSqKm)}",
                inline = true,
            },
            new
            {
                name = "Region",
                value = string.IsNullOrWhiteSpace(post.GroupName) ? "Not detected" : post.GroupName,
                inline = true,
            },
            new
            {
                name = post.State == GeofenceReviewState.Approved ? "Published as" : "Publishes as",
                value = $"`{post.PublicName}`",
                inline = true,
            },
        };

        var lat = post.CentroidLat.ToString("0.0000", CultureInfo.InvariantCulture);
        var lon = post.CentroidLon.ToString("0.0000", CultureInfo.InvariantCulture);
        fields.Add(new
        {
            name = "Location",
            value = $"{lat}, {lon} · [Open in maps](https://www.google.com/maps/search/?api=1&query={lat},{lon})",
            inline = false,
        });

        if (!string.IsNullOrWhiteSpace(post.OverlapsArea))
        {
            fields.Add(new
            {
                name = "Already covered by",
                value = $"{post.OverlapsArea} — this area's centre already falls inside a public area.",
                inline = false,
            });
        }

        if (post.State == GeofenceReviewState.Rejected && !string.IsNullOrWhiteSpace(post.ReviewNotes))
        {
            fields.Add(new { name = "Reason", value = Truncate(post.ReviewNotes, 1024), inline = false });
        }

        object? image = hasAttachment ? new { url = $"attachment://{MapAttachmentFileName}" }
            : post.MapImageUrl != null ? new { url = post.MapImageUrl }
            : null;

        return new
        {
            title = post.DisplayName,
            url = post.ReviewUrl,
            color = ColorFor(post.State),
            author = new { name = post.UserName },
            fields = fields.ToArray(),
            image,
            footer = new { text = FooterFor(post.State) },
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
    }

    private static int ColorFor(GeofenceReviewState state) => state switch
    {
        GeofenceReviewState.Approved => 2278750,  // #22c55e
        GeofenceReviewState.Rejected => 15680580, // #ef4444
        _ => 16096779,                            // #f59e0b
    };

    private static string FooterFor(GeofenceReviewState state) => state switch
    {
        GeofenceReviewState.Approved => "Approved",
        GeofenceReviewState.Rejected => "Rejected",
        _ => "Awaiting review",
    };

    private static string[] TagsFor(GeofenceReviewState state)
    {
        var tagId = state switch
        {
            GeofenceReviewState.Approved => s_approvedTagId,
            GeofenceReviewState.Rejected => s_rejectedTagId,
            _ => s_pendingTagId,
        };

        return tagId != null ? [tagId] : [];
    }

    private static object[]? AttachmentsFor(byte[]? mapImageBytes) =>
        mapImageBytes != null ? [new { id = "0", filename = MapAttachmentFileName }] : null;

    private static string VerdictText(GeofenceSubmissionPost post) => post.State switch
    {
        GeofenceReviewState.Approved =>
            $"✅ **Approved.** Published as **{post.PublicName}** and now selectable by everyone on the Areas page.",
        GeofenceReviewState.Rejected =>
            $"❌ **Rejected.** {post.ReviewNotes}\n\nThe area keeps working privately for your own alerts.",
        _ => "This submission is awaiting review.",
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialize Discord forum tags")]
    private static partial void LogForumTagInitFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discord GeofenceForumChannelId is not configured; skipping submission post")]
    private static partial void LogForumChannelNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Discord forum post for geofence '{GeofenceName}', threadId={ThreadId}")]
    private static partial void LogForumPostCreated(ILogger logger, string geofenceName, string? threadId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Discord forum post for geofence '{GeofenceName}'")]
    private static partial void LogForumPostFailed(ILogger logger, Exception ex, string geofenceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted {State} outcome to Discord thread {ThreadId} for geofence '{GeofenceName}'")]
    private static partial void LogOutcomePosted(ILogger logger, string threadId, string geofenceName, string state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post review outcome to Discord thread {ThreadId}")]
    private static partial void LogOutcomeFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not rewrite the opening embed on Discord thread {ThreadId}; posting the verdict anyway")]
    private static partial void LogOpeningEmbedUpdateFailed(ILogger logger, Exception ex, string threadId);

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
