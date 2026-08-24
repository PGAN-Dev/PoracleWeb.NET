using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The v2 mute proxy against a stubbed handler. Every body here is one that PoracleNG 5.2.1 actually
/// returned when called on the live 5.2.1 instance, not one invented from the source.
/// </summary>
public class PoracleMuteProxyTests
{
    private const string ApiAddress = "http://localhost:3030";
    private const string ApiSecret = "test-secret";

    private static PoracleMuteProxy CreateSut(RecordingHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = ApiSecret,
            })
            .Build());

    // ──────────────────────────────────────────────────────────────
    // Path, version and auth
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListCallsTheV2PathAndSendsTheSecret()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"mutes":[]}""");

        await CreateSut(handler).ListAsync("user1");

        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/mutes", handler.LastUri?.ToString());
        Assert.Equal(ApiSecret, handler.LastRequest?.Headers.GetValues("X-Poracle-Secret").Single());
    }

    /// <summary>A human id can be a full webhook URL, so the segment has to survive encoding.</summary>
    [Fact]
    public async Task ListEncodesAWebhookUrlIdIntoOneSegment()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"mutes":[]}""");

        await CreateSut(handler).ListAsync("http://hook/a/b");

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/http%3A%2F%2Fhook%2Fa%2Fb/mutes",
            handler.LastUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // The three wrappers
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListUnwrapsTheMutesArray()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/ """
            {"mutes":[{"scope":"gym","value":"abc123","expires_at":1787575463,"remaining_secs":1800},
                      {"scope":"everything","value":null,"expires_at":1787577263,"remaining_secs":3600}]}
            """);

        var mutes = await CreateSut(handler).ListAsync("user1");

        Assert.Equal(2, mutes.Count);
        Assert.Equal(MuteScopes.Gym, mutes[0].Scope);
        Assert.Equal("abc123", mutes[0].Value);
        Assert.Equal(1787575463, mutes[0].ExpiresAt);
        Assert.Equal(1800, mutes[0].RemainingSecs);

        // value is present-but-null for 'everything'. Reading it as an empty string would make the
        // delete path send ?value= and get a 422.
        Assert.Null(mutes[1].Value);
    }

    [Fact]
    public async Task CreateUnwrapsTheMuteObjectAndTheReplacedFlag()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/
            """{"mute":{"scope":"area","value":"Aberdeen","expires_at":1787577263,"remaining_secs":3600},"replaced":true}""");

        var (mute, replaced) = await CreateSut(handler).CreateAsync("user1", MuteScopes.Area, "aberdeen", 60);

        Assert.True(replaced);
        Assert.Equal(MuteScopes.Area, mute.Scope);

        // Upstream canonicalises the area to the geofence's own casing. Anything that later deletes it
        // must use THIS value, because the delete lookup is an exact string match.
        Assert.Equal("Aberdeen", mute.Value);
    }

    [Fact]
    public async Task CreateReportsReplacedFalseForANewMute()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/
            """{"mute":{"scope":"gym","value":"abc123","expires_at":1787574563,"remaining_secs":900},"replaced":false}""");

        var (_, replaced) = await CreateSut(handler).CreateAsync("user1", MuteScopes.Gym, "abc123", 15);

        Assert.False(replaced);
    }

    [Fact]
    public async Task DeleteUnwrapsTheDeletedArray()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/
            """{"deleted":[{"scope":"gym","value":"abc123","expires_at":1787575463,"remaining_secs":1799}]}""");

        var deleted = await CreateSut(handler).DeleteAsync("user1", MuteScopes.Gym, "abc123");

        var mute = Assert.Single(deleted);
        Assert.Equal("abc123", mute.Value);
        Assert.Contains("scope=gym", handler.LastUri?.Query, StringComparison.Ordinal);
        Assert.Contains("value=abc123", handler.LastUri?.Query, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────
    // Request shape
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSendsScopeValueAndDuration()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/
            """{"mute":{"scope":"pokemon","value":"25","expires_at":1,"remaining_secs":1},"replaced":false}""");

        await CreateSut(handler).CreateAsync("user1", MuteScopes.Pokemon, "25", 240);

        Assert.Contains("\"scope\":\"pokemon\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"25\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"duration_min\":240", handler.LastBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// 'everything' takes no value at all — upstream 422s on an empty string as readily as on a real one.
    /// </summary>
    [Fact]
    public async Task CreateOmitsValueEntirelyWhenItIsNull()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/
            """{"mute":{"scope":"everything","value":null,"expires_at":1,"remaining_secs":1},"replaced":false}""");

        await CreateSut(handler).CreateAsync("user1", MuteScopes.Everything, null, 60);

        Assert.DoesNotContain("value", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAllSendsNoQueryParameters()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"deleted":[]}""");

        await CreateSut(handler).DeleteAllAsync("user1");

        Assert.Equal(string.Empty, handler.LastUri?.Query);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
    }

    // ──────────────────────────────────────────────────────────────
    // Failure handling
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The store expires entries by itself, so resuming a quiet period a moment after it lapsed is the
    /// ordinary case, not a fault. It must not throw and must not surface as an error.
    /// </summary>
    [Fact]
    public async Task DeleteTreatsAnAlreadyLapsedMuteAsSuccess()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"mute not found"}""");

        var deleted = await CreateSut(handler).DeleteAsync("user1", MuteScopes.Area, "Aberdeen");

        Assert.Empty(deleted);
    }

    /// <summary>huma's readable sentence lives in <c>detail</c>, not v1's <c>error</c>.</summary>
    [Fact]
    public async Task CreateSurfacesHumasDetailOn422()
    {
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"unknown area: Narnia"}""");

        var ex = await Assert.ThrowsAsync<MuteRejectedException>(
            () => CreateSut(handler).CreateAsync("user1", MuteScopes.Area, "Narnia", 60));

        Assert.Equal("unknown area: Narnia", ex.Reason);
    }

    /// <summary>
    /// A schema violation leaves detail as "validation failed" and puts the useful complaint in
    /// errors[0].message, so that one wins.
    /// </summary>
    [Fact]
    public async Task CreatePrefersTheSchemaErrorMessageOverTheGenericDetail()
    {
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity, /*lang=json,strict*/ """
            {"title":"Unprocessable Entity","status":422,"detail":"validation failed",
             "errors":[{"message":"expected number <= 10080","location":"body.duration_min","value":10081}]}
            """);

        var ex = await Assert.ThrowsAsync<MuteRejectedException>(
            () => CreateSut(handler).CreateAsync("user1", MuteScopes.Pokemon, "25", 10081));

        Assert.Equal("expected number <= 10080", ex.Reason);
    }

    /// <summary>
    /// The list is read on every alarm page. A server that is down or too old must leave the page
    /// working, not throw through it.
    /// </summary>
    [Fact]
    public async Task ListReturnsEmptyRatherThanThrowingOn404()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "404 page not found");

        Assert.Empty(await CreateSut(handler).ListAsync("user1"));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        public Uri? LastUri => this.LastRequest?.RequestUri;

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastRequest = request;
            this.LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
