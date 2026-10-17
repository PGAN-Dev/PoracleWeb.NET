using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The v2 linked-accounts proxy against a stubbed handler. Bodies follow PoracleNG's v2_accounts.go.
/// </summary>
public class PoracleAccountProxyTests
{
    private const string ApiAddress = "http://localhost:3030";
    private const string ApiSecret = "test-secret";

    private const string StoredAccount = /*lang=json,strict*/ """
        {"account":{"player_id":"Ab9cD","label":"Main","created_at":"2026-10-01T12:30:00Z"}}
        """;

    private static PoracleAccountProxy CreateSut(RecordingHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = ApiSecret,
            })
            .Build());

    [Fact]
    public async Task ListsAccounts()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"accounts":[]}""");

        await CreateSut(handler).ListAsync("user1");

        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/accounts", handler.LastUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ListUnwrapsTheAccountsProperty()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/ """
            {"accounts":[{"player_id":"Ab9cD","label":"Main","created_at":"2026-10-01T12:30:00Z"},
                         {"player_id":"zzTop1","label":null,"created_at":"2026-10-02T00:00:00Z"}]}
            """);

        var accounts = await CreateSut(handler).ListAsync("user1");

        Assert.Equal(2, accounts.Count);
        Assert.Equal("Ab9cD", accounts[0].PlayerId);
        Assert.Equal("Main", accounts[0].Label);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.Zero), accounts[0].CreatedAt);
        Assert.Equal("zzTop1", accounts[1].PlayerId);
        Assert.Null(accounts[1].Label);
    }

    [Fact]
    public async Task AddSendsPlayerIdAndOmitsAnEmptyLabel()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, StoredAccount);
        var sut = CreateSut(handler);

        await sut.AddAsync("user1", "Ab9cD", null);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/accounts", handler.LastUri?.AbsoluteUri);
        using (var withoutLabel = JsonDocument.Parse(handler.LastBody))
        {
            Assert.Equal("Ab9cD", withoutLabel.RootElement.GetProperty("player_id").GetString());
            Assert.False(withoutLabel.RootElement.TryGetProperty("label", out _));
        }

        await sut.AddAsync("user1", "Ab9cD", "  ");
        using (var blankLabel = JsonDocument.Parse(handler.LastBody))
        {
            Assert.False(blankLabel.RootElement.TryGetProperty("label", out _));
        }

        await sut.AddAsync("user1", "Ab9cD", "Main");
        using var withLabel = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("Main", withLabel.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task AddReturnsTheStoredAccount()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, StoredAccount);

        var account = await CreateSut(handler).AddAsync("user1", "Ab9cD", "Main");

        Assert.Equal("Ab9cD", account.PlayerId);
        Assert.Equal("Main", account.Label);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.Zero), account.CreatedAt);
    }

    [Fact]
    public async Task RelabelSendsLabel()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":"ok"}""");

        await CreateSut(handler).RelabelAsync("user1", "Ab9cD", "Alt");

        Assert.Equal(HttpMethod.Put, handler.LastRequest?.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/accounts/Ab9cD", handler.LastUri?.AbsoluteUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("Alt", body.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task RelabelWithEmptyClears()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":"ok"}""");

        await CreateSut(handler).RelabelAsync("user1", "Ab9cD", string.Empty);

        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("label").ValueKind);
        Assert.Equal(string.Empty, body.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task DeleteCallsTheAccountPath()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":"ok"}""");

        await CreateSut(handler).DeleteAsync("user1", "Ab9cD");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/accounts/Ab9cD", handler.LastUri?.AbsoluteUri);
    }

    /// <summary>Player ids are case-sensitive: the path must carry exactly what was passed.</summary>
    [Fact]
    public async Task KeepsPlayerIdCaseInThePath()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":"ok"}""");

        await CreateSut(handler).DeleteAsync("user1", "Ab9cD");

        Assert.EndsWith("/accounts/Ab9cD", handler.LastUri?.AbsoluteUri);
    }

    /// <summary>A human id can be a full webhook URL, so the segment has to survive encoding.</summary>
    [Fact]
    public async Task EscapesTheHumanIdForWebhooks()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"accounts":[]}""");

        await CreateSut(handler).ListAsync("http://hook/a/b");

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/http%3A%2F%2Fhook%2Fa%2Fb/accounts",
            handler.LastUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ADuplicateIs409WithDetail()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, /*lang=json,strict*/ """
            {"title":"Conflict","status":409,"detail":"player id already linked"}
            """);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).AddAsync("user1", "Ab9cD", "Main"));

        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("player id already linked", ex.Message);
    }

    [Fact]
    public async Task AnInvalidIdIs422WithDetail()
    {
        var handler = new RecordingHandler(HttpStatusCode.UnprocessableEntity, /*lang=json,strict*/ """
            {"title":"Unprocessable Entity","status":422,"detail":"player_id must be alphanumeric"}
            """);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).AddAsync("user1", "bad id!", null));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("player_id must be alphanumeric", ex.Message);
    }

    [Fact]
    public async Task ABadRequestIsAlso422()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, /*lang=json,strict*/ """
            {"title":"Bad Request","status":400,"detail":"malformed body"}
            """);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).AddAsync("user1", "Ab9cD", null));

        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task AMissingRouteIs501()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, "404 page not found");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).ListAsync("user1"));

        Assert.Equal(501, ex.StatusCode);
        Assert.Equal("This Poracle server does not support game accounts.", ex.Message);
    }

    [Fact]
    public async Task AnUnknownAccountIs404()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, /*lang=json,strict*/ """
            {"title":"Not Found","status":404,"detail":"account not found"}
            """);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).DeleteAsync("user1", "Ab9cD"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("account not found", ex.Message);
    }

    [Fact]
    public async Task AnUnexplainedRefusalFallsBackToTheDefaultSentence()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, """{"status":409}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => CreateSut(handler).AddAsync("user1", "Ab9cD", null));

        Assert.Equal("Poracle refused that account change.", ex.Message);
    }

    [Fact]
    public async Task AServerErrorIsNotRelabelledAsARefusal()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateSut(handler).ListAsync("user1"));
    }

    [Fact]
    public async Task SendsTheSecretHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"accounts":[]}""");

        await CreateSut(handler).ListAsync("user1");

        Assert.Equal(ApiSecret, handler.LastRequest?.Headers.GetValues("X-Poracle-Secret").Single());
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
