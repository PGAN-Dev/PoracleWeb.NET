using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Covers the geofence submission forum post. PoracleNG hands back a pregenerated tileserver URL that the
/// tile cache evicts after a while, so the map is uploaded to Discord as an attachment instead of linked.
/// </summary>
public class DiscordNotificationServiceTests
{
    private const string ForumChannelId = "1234567890";
    private const string MapUrl = "https://tiles.example.test/staticmap/pregenerated/abc123.png";

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discord:GeofenceForumChannelId"] = ForumChannelId
        })
        .Build();

    private static DiscordNotificationService CreateSut(DiscordHandler discord, HttpMessageHandler mapHandler)
    {
        var discordClient = new HttpClient(discord) { BaseAddress = new Uri("https://discordapp.com/api/v9/") };
        var factory = new StubHttpClientFactory(new HttpClient(mapHandler));

        return new DiscordNotificationService(
            discordClient,
            factory,
            CreateConfig(),
            NullLogger<DiscordNotificationService>.Instance);
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostUploadsMapAsAttachment()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        var threadId = await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "US - MD", 6, MapUrl);

        Assert.Equal("999", threadId);
        Assert.True(discord.WasMultipart);
        Assert.Equal("geofence-map.png", discord.UploadedFileName);
        Assert.Equal(PngBytes, discord.UploadedBytes);

        var image = ImageOf(discord.PayloadJson!);
        Assert.Equal("attachment://geofence-map.png", image.GetProperty("url").GetString());

        var attachments = MessageOf(discord.PayloadJson!).GetProperty("attachments");
        Assert.Equal(1, attachments.GetArrayLength());
        Assert.Equal("0", attachments[0].GetProperty("id").GetString());
        Assert.Equal("geofence-map.png", attachments[0].GetProperty("filename").GetString());
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostFallsBackToLinkingUrlWhenDownloadFails()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.NotFound, []));

        var threadId = await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "US - MD", 6, MapUrl);

        Assert.Equal("999", threadId);
        Assert.False(discord.WasMultipart);
        Assert.Equal(MapUrl, ImageOf(discord.PayloadJson!).GetProperty("url").GetString());
        Assert.False(MessageOf(discord.PayloadJson!).TryGetProperty("attachments", out _));
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostOmitsImageWhenNoMapUrl()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "US - MD", 6, null);

        Assert.False(discord.WasMultipart);

        var embed = EmbedOf(discord.PayloadJson!);
        Assert.False(embed.TryGetProperty("image", out _));
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostDoesNotSendTheBotTokenToTheTileserver()
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

        await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "US - MD", 6, MapUrl);

        Assert.Null(mapHandler.SeenAuthorization);
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostFallsBackToUnassignedRegion()
    {
        var discord = new DiscordHandler();
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, PngBytes));

        await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "   ", 6, MapUrl);

        var region = EmbedOf(discord.PayloadJson!).GetProperty("fields")[0];
        Assert.Equal("Region", region.GetProperty("name").GetString());
        Assert.Equal("Unassigned", region.GetProperty("value").GetString());
    }

    [Fact]
    public async Task CreateGeofenceSubmissionPostSkipsUploadWhenMapIsOversized()
    {
        var discord = new DiscordHandler();
        var oversized = new byte[(8 * 1024 * 1024) + 1];
        var sut = CreateSut(discord, new MapHandler(HttpStatusCode.OK, oversized));

        await sut.CreateGeofenceSubmissionPostAsync("user1", "Tester", "My Park", "US - MD", 6, MapUrl);

        Assert.False(discord.WasMultipart);
        Assert.Equal(MapUrl, ImageOf(discord.PayloadJson!).GetProperty("url").GetString());
    }

    private static JsonElement MessageOf(string payloadJson) =>
        JsonDocument.Parse(payloadJson).RootElement.GetProperty("message");

    private static JsonElement EmbedOf(string payloadJson) =>
        MessageOf(payloadJson).GetProperty("embeds")[0];

    private static JsonElement ImageOf(string payloadJson) =>
        EmbedOf(payloadJson).GetProperty("image");

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>
    /// Answers the forum-tag lookup with all three tags already present (so no PATCH is issued) and
    /// captures the thread-creation request.
    /// </summary>
    private sealed class DiscordHandler : HttpMessageHandler
    {
        public string? PayloadJson { get; private set; }

        public bool WasMultipart { get; private set; }

        public string? UploadedFileName { get; private set; }

        public byte[]? UploadedBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                const string channel = /*lang=json,strict*/ """
                    {"available_tags":[{"id":"1","name":"Geofence - Pending"},{"id":"2","name":"Geofence - Approved"},{"id":"3","name":"Geofence - Rejected"}]}
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(channel, Encoding.UTF8, "application/json"),
                };
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

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(/*lang=json,strict*/ """{"id":"999"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MapHandler(HttpStatusCode statusCode, byte[] body) : HttpMessageHandler
    {
        public string? SeenAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.SeenAuthorization = request.Headers.Authorization?.ToString();

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(body),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

            return Task.FromResult(response);
        }
    }
}
