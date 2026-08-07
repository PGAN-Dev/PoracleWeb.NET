using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Covers the geofence review post. Two things are load-bearing: the map is uploaded as an attachment
/// (PoracleNG's tileserver URL is evicted after a while, so linking it leaves a dead image), and the
/// opening embed is rewritten on approval/rejection so the thread reflects its own outcome.
/// </summary>
public class DiscordNotificationServiceTests
{
    private const string ForumChannelId = "1234567890";
    private const string ThreadId = "999";
    private const string MapUrl = "https://tiles.example.test/staticmap/pregenerated/abc123.png";

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static GeofenceSubmissionPost Post(
        GeofenceReviewState state = GeofenceReviewState.Pending,
        string groupName = "US - MD",
        string? mapUrl = MapUrl,
        string? overlaps = null,
        string? reviewNotes = null,
        string? reviewUrl = null,
        double areaSqKm = 4.2) => new()
        {
            UserId = "user1",
            UserName = "Tester",
            DisplayName = "My Park",
            PublicName = "my park",
            GroupName = groupName,
            AreaSqKm = areaSqKm,
            CentroidLat = 38.9412,
            CentroidLon = -76.7305,
            OverlapsArea = overlaps,
            MapImageUrl = mapUrl,
            State = state,
            ReviewNotes = reviewNotes,
            ReviewUrl = reviewUrl,
        };

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discord:GeofenceForumChannelId"] = ForumChannelId
        })
        .Build();

    private static DiscordNotificationService CreateSut(DiscordHandler discord, HttpMessageHandler mapHandler)
    {
        var discordClient = new HttpClient(discord) { BaseAddress = new Uri("https://discordapp.com/api/v9/") };

        return new DiscordNotificationService(
            discordClient,
            new StubHttpClientFactory(new HttpClient(mapHandler)),
            CreateConfig(),
            NullLogger<DiscordNotificationService>.Instance);
    }

    // ── Submission post ────────────────────────────────────────────

    [Fact]
    public async Task CreateUploadsMapAsAttachment()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        var threadId = await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.Equal(ThreadId, threadId);
        Assert.True(discord.WasMultipart);
        Assert.Equal("geofence-map.png", discord.UploadedFileName);
        Assert.Equal(PngBytes, discord.UploadedBytes);
        Assert.Equal("attachment://geofence-map.png", Embed(discord).GetProperty("image").GetProperty("url").GetString());
    }

    [Fact]
    public async Task CreateFallsBackToLinkingUrlWhenDownloadFails()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.NotFound, []));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.False(discord.WasMultipart);
        Assert.Equal(MapUrl, Embed(discord).GetProperty("image").GetProperty("url").GetString());
    }

    [Fact]
    public async Task CreateOmitsImageWhenNoMapUrl()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post(mapUrl: null));

        Assert.False(Embed(discord).TryGetProperty("image", out _));
    }

    [Fact]
    public async Task CreateDoesNotSendTheBotTokenToTheTileserver()
    {
        var discord = new DiscordHandler();
        var mapHandler = new MapHandler(HttpStatusCode.OK, PngBytes);
        var discordClient = new HttpClient(discord) { BaseAddress = new Uri("https://discordapp.com/api/v9/") };
        discordClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", "super-secret");

        var sut = new DiscordNotificationService(
            discordClient,
            new StubHttpClientFactory(new HttpClient(mapHandler)),
            CreateConfig(),
            NullLogger<DiscordNotificationService>.Instance);

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.Null(mapHandler.SeenAuthorization);
    }

    [Fact]
    public async Task CreateSkipsUploadWhenMapIsOversized()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, new byte[(8 * 1024 * 1024) + 1]));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.False(discord.WasMultipart);
        Assert.Equal(MapUrl, Embed(discord).GetProperty("image").GetProperty("url").GetString());
    }

    // ── Review card contents ───────────────────────────────────────

    [Fact]
    public async Task EmbedLeadsWithSizeRegionAndPublicName()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        var fields = Embed(discord).GetProperty("fields");
        Assert.Equal("Size", fields[0].GetProperty("name").GetString());
        Assert.Contains("4.2 km²", fields[0].GetProperty("value").GetString(), StringComparison.Ordinal);
        Assert.Contains("neighbourhood", fields[0].GetProperty("value").GetString(), StringComparison.Ordinal);
        Assert.Equal("Region", fields[1].GetProperty("name").GetString());
        Assert.Equal("Publishes as", fields[2].GetProperty("name").GetString());
        Assert.Equal("`my park`", fields[2].GetProperty("value").GetString());
    }

    [Fact]
    public async Task EmbedDropsVertexCountAndMovesSubmitterToAuthor()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        var embed = Embed(discord);
        var names = embed.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()).ToList();

        Assert.DoesNotContain("Points", names);
        Assert.DoesNotContain("Submitted By", names);
        Assert.Equal("Tester", embed.GetProperty("author").GetProperty("name").GetString());

        // The mention moves to the message content so it stays clickable.
        Assert.Contains("<@user1>", Message(discord).GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedOmitsTheAuthorBlockRatherThanShowingARawId()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        var post = Post() with { UserName = null };
        await sut.CreateGeofenceSubmissionPostAsync(post);

        Assert.False(Embed(discord).TryGetProperty("author", out _));

        // The mention still identifies the submitter.
        Assert.Contains("<@user1>", Message(discord).GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedLinksTheCentroidToAMap()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        var location = Field(discord, "Location").GetProperty("value").GetString()!;
        Assert.Contains("38.9412, -76.7305", location, StringComparison.Ordinal);
        Assert.Contains("[Open in maps](https://www.google.com/maps/search/?api=1&query=38.9412,-76.7305)", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedSaysRegionNotDetectedWhenGroupIsBlank()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post(groupName: "   "));

        Assert.Equal("Not detected", Field(discord, "Region").GetProperty("value").GetString());
    }

    [Fact]
    public async Task EmbedFlagsAnOverlappingPublicArea()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post(overlaps: "US - MD - Bowie"));

        Assert.Contains("US - MD - Bowie", Field(discord, "Already covered by").GetProperty("value").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedOmitsOverlapFieldWhenThereIsNone()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.Null(TryField(discord, "Already covered by"));
    }

    [Fact]
    public async Task EmbedLinksTitleToTheReviewPageOnlyWhenConfigured()
    {
        var withUrl = new DiscordHandler();
        await CreateSut(withUrl, new MapHandler(HttpStatusCode.OK, PngBytes))
            .CreateGeofenceSubmissionPostAsync(Post(reviewUrl: "https://alerts.example.test/admin/geofence-submissions"));
        Assert.Equal("https://alerts.example.test/admin/geofence-submissions", Embed(withUrl).GetProperty("url").GetString());

        var without = new DiscordHandler();
        await CreateSut(without, new MapHandler(HttpStatusCode.OK, PngBytes))
            .CreateGeofenceSubmissionPostAsync(Post());
        Assert.False(Embed(without).TryGetProperty("url", out _));
    }

    [Theory]
    [InlineData(0.4, "a block or two")]
    [InlineData(4.2, "neighbourhood")]
    [InlineData(30, "district")]
    [InlineData(120, "city-sized")]
    [InlineData(900, "very large")]
    public async Task EmbedBandsTheAreaInPlainLanguage(double areaSqKm, string expected)
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post(areaSqKm: areaSqKm));

        Assert.Contains(expected, Field(discord, "Size").GetProperty("value").GetString()!, StringComparison.Ordinal);
    }

    // ── Status colour and outcome edit ─────────────────────────────

    [Fact]
    public async Task PendingPostIsAmber()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync(Post());

        Assert.Equal(16096779, Embed(discord).GetProperty("color").GetInt32()); // #f59e0b
    }

    [Fact]
    public async Task ApprovalRewritesTheOpeningEmbedGreenAndKeepsTheMap()
    {
        var discord = new DiscordHandler { ExistingAttachmentId = "attach-1" };
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.PostReviewOutcomeAsync(ThreadId, Post(GeofenceReviewState.Approved));

        Assert.Equal($"channels/{ThreadId}/messages/{ThreadId}", discord.PatchedMessagePath);

        var edited = JsonDocument.Parse(discord.PatchedMessageJson!).RootElement;
        var embed = edited.GetProperty("embeds")[0];
        Assert.Equal(2278750, embed.GetProperty("color").GetInt32()); // #22c55e
        Assert.Equal("Published as", embed.GetProperty("fields")[2].GetProperty("name").GetString());
        Assert.Equal("Approved", embed.GetProperty("footer").GetProperty("text").GetString());

        // The map is re-referenced by attachment ID rather than re-uploaded or pinned to a signed CDN URL.
        Assert.Equal("attachment://geofence-map.png", embed.GetProperty("image").GetProperty("url").GetString());
        Assert.Equal("attach-1", edited.GetProperty("attachments")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task RejectionRewritesTheOpeningEmbedRedAndCarriesTheReason()
    {
        var discord = new DiscordHandler { ExistingAttachmentId = "attach-1" };
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.PostReviewOutcomeAsync(ThreadId, Post(GeofenceReviewState.Rejected, reviewNotes: "Too large"));

        var embed = JsonDocument.Parse(discord.PatchedMessageJson!).RootElement.GetProperty("embeds")[0];
        Assert.Equal(15680580, embed.GetProperty("color").GetInt32()); // #ef4444
        Assert.Equal("Rejected", embed.GetProperty("footer").GetProperty("text").GetString());

        var reason = embed.GetProperty("fields").EnumerateArray()
            .First(f => f.GetProperty("name").GetString() == "Reason");
        Assert.Equal("Too large", reason.GetProperty("value").GetString());
    }

    [Fact]
    public async Task OutcomeStillPostsTheVerdictWhenTheEmbedEditFails()
    {
        var discord = new DiscordHandler { FailMessageEdit = true };
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.PostReviewOutcomeAsync(ThreadId, Post(GeofenceReviewState.Approved));

        Assert.Contains("Approved", discord.PostedMessageContent!, StringComparison.Ordinal);
        Assert.True(discord.ThreadArchived);
    }

    [Fact]
    public async Task OutcomeLocksAndArchivesTheThread()
    {
        var discord = new DiscordHandler { ExistingAttachmentId = "attach-1" };
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.PostReviewOutcomeAsync(ThreadId, Post(GeofenceReviewState.Approved));

        Assert.True(discord.ThreadLocked);
        Assert.True(discord.ThreadArchived);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static JsonElement Message(DiscordHandler discord) =>
        JsonDocument.Parse(discord.PayloadJson!).RootElement.GetProperty("message");

    private static JsonElement Embed(DiscordHandler discord) => Message(discord).GetProperty("embeds")[0];

    private static JsonElement Field(DiscordHandler discord, string name) =>
        TryField(discord, name) ?? throw new InvalidOperationException($"No '{name}' field on the embed.");

    private static JsonElement? TryField(DiscordHandler discord, string name)
    {
        foreach (var field in Embed(discord).GetProperty("fields").EnumerateArray())
        {
            if (field.GetProperty("name").GetString() == name)
            {
                return field;
            }
        }

        return null;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>
    /// Answers the forum-tag lookup with all three tags already present (so no tag PATCH is issued) and
    /// records every request the service makes.
    /// </summary>
    private sealed class DiscordHandler : HttpMessageHandler
    {
        public string? ExistingAttachmentId { get; init; }

        public bool FailMessageEdit { get; init; }

        public string? PayloadJson { get; private set; }

        public bool WasMultipart { get; private set; }

        public string? UploadedFileName { get; private set; }

        public byte[]? UploadedBytes { get; private set; }

        public string? PatchedMessagePath { get; private set; }

        public string? PatchedMessageJson { get; private set; }

        public string? PostedMessageContent { get; private set; }

        public bool ThreadLocked { get; private set; }

        public bool ThreadArchived { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.Replace("/api/v9/", string.Empty, StringComparison.Ordinal);

            if (request.Method == HttpMethod.Get && path.Contains("/messages/", StringComparison.Ordinal))
            {
                var attachments = this.ExistingAttachmentId != null
                    ? $$"""[{"id":"{{this.ExistingAttachmentId}}","filename":"geofence-map.png"}]"""
                    : "[]";
                return Json($$"""{"id":"{{ThreadId}}","attachments":{{attachments}}}""");
            }

            if (request.Method == HttpMethod.Get)
            {
                return Json("""
                    {"available_tags":[{"id":"1","name":"Geofence - Pending"},{"id":"2","name":"Geofence - Approved"},{"id":"3","name":"Geofence - Rejected"}]}
                    """);
            }

            if (request.Method == HttpMethod.Patch && path.Contains("/messages/", StringComparison.Ordinal))
            {
                if (this.FailMessageEdit)
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
                }

                this.PatchedMessagePath = path;
                this.PatchedMessageJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("{}");
            }

            if (request.Method == HttpMethod.Patch)
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                this.ThreadLocked = body.TryGetProperty("locked", out var l) && l.GetBoolean();
                this.ThreadArchived = body.TryGetProperty("archived", out var a) && a.GetBoolean();
                return Json("{}");
            }

            if (path.EndsWith("/messages", StringComparison.Ordinal))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                this.PostedMessageContent = body.GetProperty("content").GetString();
                return Json("""{"id":"reply-1"}""");
            }

            if (request.Content is MultipartFormDataContent multipart)
            {
                this.WasMultipart = true;
                var parts = multipart.ToList();
                this.PayloadJson = await parts[0].ReadAsStringAsync(cancellationToken);
                this.UploadedBytes = await parts[1].ReadAsByteArrayAsync(cancellationToken);
                this.UploadedFileName = parts[1].Headers.ContentDisposition?.FileName?.Trim('"');
            }
            else
            {
                this.PayloadJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            return Json($$"""{"id":"{{ThreadId}}"}""");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class MapHandler(HttpStatusCode statusCode, byte[] body) : HttpMessageHandler
    {
        public string? SeenAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.SeenAuthorization = request.Headers.Authorization?.ToString();

            var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

            return Task.FromResult(response);
        }
    }
}
