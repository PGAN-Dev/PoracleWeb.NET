using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// <c>POST /api/deliverMessages</c> takes a JSON array of pre-rendered jobs, <c>{target, type, message}</c>,
/// and answers <c>{"status":"ok","queued":N}</c>. Discord reads <c>message.content</c>; Telegram reads the
/// same field. The shape was read from PoracleNG's <c>apiDeliverJob</c>, and nothing here calls a live server.
/// </summary>
public class PoracleApiProxyDeliverTextTests
{
    private const string ApiAddress = "http://localhost:3030";

    private static PoracleApiProxy CreateSut(RecordingHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret"
            })
            .Build());

    [Fact]
    public async Task DeliverTextAsyncPostsOneJobToDeliverMessagesWithTheSecret()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"status":"ok","queued":1}""");

        await CreateSut(handler).DeliverTextAsync("1234567890", "discord:user", "Hello there");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{ApiAddress}/api/deliverMessages", request.Uri);
        Assert.Equal("test-secret", request.Secret);

        using var doc = JsonDocument.Parse(request.Body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var job = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("1234567890", job.GetProperty("target").GetString());
        Assert.Equal("discord:user", job.GetProperty("type").GetString());
        Assert.Equal("Hello there", job.GetProperty("message").GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("https://hooks.example.test/webhooks/1/token", "webhook")]
    [InlineData("-1001234567890:7", "telegram:topic")]
    [InlineData("42", "telegram:user")]
    public async Task DeliverTextAsyncSendsTheTargetAndTypeAsGiven(string target, string type)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"status":"ok","queued":1}""");

        await CreateSut(handler).DeliverTextAsync(target, type, "note");

        using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var job = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(target, job.GetProperty("target").GetString());
        Assert.Equal(type, job.GetProperty("type").GetString());
    }

    [Fact]
    public async Task DeliverTextAsyncKeepsApostrophesAndNonAsciiAsWritten()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"status":"ok","queued":1}""");

        await CreateSut(handler).DeliverTextAsync("1", "discord:user", "It isn't a Pokémon");

        using var doc = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var job = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("It isn't a Pokémon", job.GetProperty("message").GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task DeliverTextAsyncThrowsWhenPoracleNgRefuses(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status, /*lang=json,strict*/ """{"title":"nope","status":503}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateSut(handler).DeliverTextAsync("1", "discord:user", "note"));
    }

    [Fact]
    public async Task DeliverTextAsyncRefusalDoesNotCarryTheTargetInItsMessage()
    {
        // A webhook's id is its URL and the URL carries the token; whatever surfaces from a failure must not.
        const string webhook = "https://hooks.example.test/webhooks/1/secret-token";
        var handler = new RecordingHandler(HttpStatusCode.BadGateway, string.Empty);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => CreateSut(handler).DeliverTextAsync(webhook, "webhook", "note"));

        Assert.DoesNotContain("secret-token", ex.ToString(), StringComparison.Ordinal);
    }

    private sealed record Recorded(HttpMethod Method, string Uri, string? Secret, string Body);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var secret = request.Headers.TryGetValues("X-Poracle-Secret", out var values) ? values.FirstOrDefault() : null;
            this.Requests.Add(new Recorded(request.Method, request.RequestUri!.ToString(), secret, body));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
