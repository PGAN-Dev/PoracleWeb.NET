using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
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
            Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    // ──────────────────────────────────────────────────────────────
    // GetByUserAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByUserAsyncExtractsArrayByTypeKey()
    {
        var responseBody = /*lang=json,strict*/ """{"pokemon":[{"uid":1},{"uid":2}]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetByUserAsync("pokemon", "user1");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(2, result.GetArrayLength());
        Assert.Equal(1, result[0].GetProperty("uid").GetInt32());
    }

    [Fact]
    public async Task GetByUserAsyncReturnsEmptyArrayWhenKeyMissing()
    {
        var responseBody = /*lang=json,strict*/ """{"other":[{"uid":1}]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetByUserAsync("pokemon", "user1");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(0, result.GetArrayLength());
    }

    [Fact]
    public async Task GetByUserAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"raid":[]}""");
        var sut = CreateSut(handler);

        await sut.GetByUserAsync("raid", "user42");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/raid/user42", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetByUserAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetByUserAsync("pokemon", "user1"));
    }

    [Theory]
    [InlineData("pokemon", "pokemon")]
    [InlineData("raid", "raid")]
    [InlineData("egg", "egg")]
    [InlineData("quest", "quest")]
    [InlineData("invasion", "invasion")]
    [InlineData("lure", "lure")]
    [InlineData("nest", "nest")]
    [InlineData("gym", "gym")]
    [InlineData("fort", "fort")]
    [InlineData("maxbattle", "maxbattle")]
    [InlineData("unknown_type", "unknown_type")]
    public async Task GetByUserAsyncResolvesCorrectResponseKey(string type, string expectedKey)
    {
        var responseBody = $$"""{"{{expectedKey}}":[{"uid":99}]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetByUserAsync(type, "user1");

        Assert.Equal(1, result.GetArrayLength());
    }

    // ──────────────────────────────────────────────────────────────
    // CreateAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsyncSendsCorrectUrlWithSilentParam()
    {
        var responseBody = /*lang=json,strict*/ """{"newUids":[10,11],"alreadyPresent":0,"updates":0,"insert":2}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("""[{"pokemon_id":25}]""").RootElement;
        await sut.CreateAsync("pokemon", "user1", body);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1?silent=true", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task CreateAsyncSendsJsonBody()
    {
        var responseBody = /*lang=json,strict*/ """{"newUids":[],"alreadyPresent":1,"updates":0,"insert":0}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("""[{"pokemon_id":25,"min_iv":90}]""").RootElement;
        await sut.CreateAsync("pokemon", "user1", body);

        Assert.NotNull(handler.LastRequest?.Content);
        var sentBody = await handler.LastRequest.Content.ReadAsStringAsync();
        Assert.Contains("pokemon_id", sentBody);
    }

    [Fact]
    public async Task CreateAsyncParsesNewUids()
    {
        var responseBody = /*lang=json,strict*/ """{"newUids":[100,200,300],"alreadyPresent":1,"updates":2,"insert":3}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        var result = await sut.CreateAsync("pokemon", "user1", body);

        Assert.Equal(3, result.NewUids.Count);
        Assert.Equal(100L, result.NewUids[0]);
        Assert.Equal(200L, result.NewUids[1]);
        Assert.Equal(300L, result.NewUids[2]);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(2, result.Updates);
        Assert.Equal(3, result.Inserts);
    }

    [Fact]
    public async Task CreateAsyncHandlesResponseWithoutOptionalFields()
    {
        var responseBody = /*lang=json,strict*/ """{"newUids":[5]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        var result = await sut.CreateAsync("pokemon", "user1", body);

        Assert.Single(result.NewUids);
        Assert.Equal(5L, result.NewUids[0]);
        Assert.Equal(0, result.AlreadyPresent);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Inserts);
    }

    [Fact]
    public async Task CreateAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.BadRequest, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.CreateAsync("pokemon", "user1", body));
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteByUidAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteByUidAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteByUidAsync("raid", "user1", 42);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/raid/user1/byUid/42", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteByUidAsyncHandles404Gracefully()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        // Should not throw
        await sut.DeleteByUidAsync("raid", "user1", 999);
    }

    [Fact]
    public async Task DeleteByUidAsyncThrowsOnOtherErrors()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DeleteByUidAsync("raid", "user1", 42));
    }

    // ──────────────────────────────────────────────────────────────
    // BulkDeleteByUidsAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkDeleteByUidsAsyncSendsUidArray()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", [1, 2, 3]);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/pokemon/user1/delete", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        var uids = JsonSerializer.Deserialize<List<long>>(sentBody);
        Assert.Equal(3, uids!.Count);
        Assert.Contains(1L, uids);
        Assert.Contains(2L, uids);
        Assert.Contains(3L, uids);
    }

    [Fact]
    public async Task BulkDeleteByUidsAsyncSkipsRequestWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.BulkDeleteByUidsAsync("pokemon", "user1", []);

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task BulkDeleteByUidsAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.BulkDeleteByUidsAsync("pokemon", "user1", [1]));
    }

    // ──────────────────────────────────────────────────────────────
    // GetAllTrackingAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTrackingAsyncReturnsFullResponse()
    {
        var responseBody = /*lang=json,strict*/ """{"pokemon":[{"uid":1}],"raid":[{"uid":2}]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetAllTrackingAsync("user1");

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.True(result.TryGetProperty("pokemon", out _));
        Assert.True(result.TryGetProperty("raid", out _));
    }

    [Fact]
    public async Task GetAllTrackingAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.GetAllTrackingAsync("user99");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/tracking/all/user99", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetAllTrackingAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetAllTrackingAsync("user1"));
    }

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
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"pokemon":[]}""");
        var sut = CreateSut(handler);

        await sut.GetByUserAsync("pokemon", "user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
        Assert.Equal(ApiSecret, handler.LastRequest.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task AllRequestsOmitSecretHeaderWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"pokemon":[]}""");
        var sut = CreateSut(handler, CreateConfigNoSecret());

        await sut.GetByUserAsync("pokemon", "user1");

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task CreateAsyncIncludesPoracleSecretHeader()
    {
        var responseBody = /*lang=json,strict*/ """{"newUids":[1]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        await sut.CreateAsync("pokemon", "user1", body);

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
