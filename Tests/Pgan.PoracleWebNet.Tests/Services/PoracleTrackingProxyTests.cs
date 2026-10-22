using Pgan.PoracleWebNet.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PoracleTrackingProxyTests
{
    private const string ApiAddress = "http://localhost:3030";
    private const string ApiSecret = "test-secret";

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Poracle:ApiAddress"] = ApiAddress,
            ["Poracle:ApiSecret"] = ApiSecret
        })
        .Build();

    private static IConfiguration CreateConfigNoSecret() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Poracle:ApiAddress"] = ApiAddress,
            ["Poracle:ApiSecret"] = ""
        })
        .Build();

    private static PoracleTrackingProxy CreateSut(MockHttpMessageHandler handler, IConfiguration? config = null)
    {
        var client = new HttpClient(handler);
        return new PoracleTrackingProxy(
            client,
            config ?? CreateConfig(),
            AnyAreaPolicy(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    /// <summary>An area policy that allows every name, for suites that are not about override areas.</summary>
    internal static IUserAreaPolicy AnyAreaPolicy() => Mock.Of<IUserAreaPolicy>();

    // ──────────────────────────────────────────────────────────────
    // PatchAsync (v2 PATCH; bulk distance and cleaning)
    // ──────────────────────────────────────────────────────────────

    private const string PatchOk = /*lang=json,strict*/ """{"dry_run":false,"updated":[],"unchanged":[],"refused":null}""";

    [Fact]
    public async Task PatchSendsUidsAndOnlyTheNamedSettings()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var sut = CreateSut(handler);

        await sut.PatchAsync("pokemon", "user1", [1, 2], new TrackingPatchSet(Distance: 500));

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Patch, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/pokemon?silent=true&trusted=true", handler.LastRequest.RequestUri?.ToString());
        Assert.Equal("""{"uids":[1,2],"set":{"distance":500}}""", handler.LastBody);
    }

    [Fact]
    public async Task PatchForACleanToggleSendsOnlyTheCleanBit()
    {
        // Fields the set does not name are left as stored upstream, which is how the edit and summary bits
        // survive the toggle.
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var sut = CreateSut(handler);

        await sut.PatchAsync("raid", "user1", [3], new TrackingPatchSet(Clean: false));

        Assert.Equal("""{"uids":[3],"set":{"clean":false}}""", handler.LastBody);
    }

    [Fact]
    public async Task PatchEncodesAWebhookId()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var sut = CreateSut(handler);

        await sut.PatchAsync("gym", "http://host/hook", [3], new TrackingPatchSet(Distance: 0));

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/http%3A%2F%2Fhost%2Fhook/tracking/gym?silent=true&trusted=true",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task PatchOfARuleLimitedToTheUsersOwnFenceIsSentTrusted()
    {
        // PoracleNG re-validates the whole stored rule on a PATCH. Untrusted, its area check allows only
        // userSelectable fences, and a user's own drawn fence never is one, so a clean toggle on a rule
        // limited to it came back refused as invalid. A PATCH cannot name an area, so nothing is authorised
        // here: the areas being re-checked are the ones already on the rule.
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var policy = new Mock<IUserAreaPolicy>(MockBehavior.Strict);
        var sut = new PoracleTrackingProxy(
            new HttpClient(handler),
            CreateConfig(),
            policy.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleTrackingProxy>>());

        await sut.PatchAsync("pokemon", "user1", [7], new TrackingPatchSet(Clean: true));

        Assert.Contains("trusted=true", handler.LastRequest?.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Equal("""{"uids":[7],"set":{"clean":true}}""", handler.LastBody);
    }

    [Fact]
    public async Task PatchMapsRefusals()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """
            {"dry_run":false,
             "updated":[{"uid":1,"distance":500,"previous":{"uid":1,"distance":100}}],
             "unchanged":[{"uid":2,"distance":500}],
             "refused":[
               {"uid":3,"reason":"conflict","detail":"would duplicate rule 9","conflicts_with":9},
               {"uid":4,"reason":"invalid","detail":"an area-scoped rule cannot take a radius"},
               {"uid":5,"reason":"not_found","detail":"no such rule"}]}
            """);
        var sut = CreateSut(handler);

        var result = await sut.PatchAsync("pokemon", "user1", [1, 2, 3, 4, 5], new TrackingPatchSet(Distance: 500));

        Assert.Equal([1], result.Updated);
        Assert.Equal([2], result.Unchanged);
        Assert.Equal(
            [
                new PatchRefusal(3, PatchRefusalReasons.Conflict, "would duplicate rule 9", 9),
                new PatchRefusal(4, PatchRefusalReasons.Invalid, "an area-scoped rule cannot take a radius", null),
                new PatchRefusal(5, PatchRefusalReasons.NotFound, "no such rule", null),
            ],
            result.Refused);
    }

    [Fact]
    public async Task PatchPlaintext404IsUnsupported()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "404 page not found");
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.PatchAsync("pokemon", "user1", [1], new TrackingPatchSet(Distance: 500)));

        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task PatchForAHumanPoracleNgDoesNotHoldIsAccountGone()
    {
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.NotFound, /*lang=json,strict*/ """{"title":"Not Found","status":404,"detail":"human not found"}""");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<AccountGoneException>(
            () => sut.PatchAsync("pokemon", "user1", [1], new TrackingPatchSet(Distance: 500)));
    }

    [Fact]
    public async Task PatchRefusedAsAWholeIsAValidationError()
    {
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.UnprocessableEntity,
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"edit is not supported for gym"}""");
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => sut.PatchAsync("gym", "user1", [1], new TrackingPatchSet(Clean: true)));

        Assert.Contains("edit is not supported for gym", ex.Message);
    }

    [Fact]
    public async Task PatchWithNoUidsSendsNothing()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var sut = CreateSut(handler);

        var result = await sut.PatchAsync("pokemon", "user1", [], new TrackingPatchSet(Distance: 500));

        Assert.Null(handler.LastRequest);
        Assert.Empty(result.Updated);
    }

    // Deletes are v2-only; PoracleTrackingProxyV2Tests covers them.

    // ──────────────────────────────────────────────────────────────
    // ReloadStateAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReloadStateAsyncCallsReloadEndpoint()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.ReloadStateAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/reload", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ReloadStateAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(sut.ReloadStateAsync);
    }

    // ──────────────────────────────────────────────────────────────
    // Auth header
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllRequestsIncludePoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"rules":[]}""");
        var sut = CreateSut(handler);

        await sut.GetByUserAsync("pokemon", "user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
        Assert.Equal(ApiSecret, handler.LastRequest.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task AllRequestsOmitSecretHeaderWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"rules":[]}""");
        var sut = CreateSut(handler, CreateConfigNoSecret());

        await sut.GetByUserAsync("pokemon", "user1");

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task PatchIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, PatchOk);
        var sut = CreateSut(handler);

        await sut.PatchAsync("pokemon", "user1", [1], new TrackingPatchSet(Distance: 1));

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task DeleteByUidAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteByUidAsync("pokemon", "user1", 1);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task BulkDeleteByUidsAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task ReloadStateAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.ReloadStateAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    // ──────────────────────────────────────────────────────────────
    // Mock handler
    // ──────────────────────────────────────────────────────────────

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        public string? LastBody
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastRequest = request;
            this.LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
