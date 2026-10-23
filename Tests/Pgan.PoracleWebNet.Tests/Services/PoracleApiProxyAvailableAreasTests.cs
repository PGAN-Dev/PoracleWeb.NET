using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The Areas page's list of areas a user may choose from: <c>GET /api/v2/humans/{id}/areas</c>, and
/// nothing else. A server older than 5.4.0 is told so rather than served v1's <c>GET /api/humans/{id}</c>.
/// </summary>
/// <remarks>
/// The bodies are trimmed from what a live 5.3.0 answered on the dev-01 test bed for the same probe human
/// on both routes: 1,236 areas each, the same names in the same order, every item carrying exactly
/// <c>name</c>, <c>group</c>, <c>description</c> and <c>userSelectable</c>. Only the wrapper differs.
/// </remarks>
public class PoracleApiProxyAvailableAreasTests
{
    private const string ApiAddress = "http://localhost:3030";

    private const string Items = """
        [{"name":"Aberdeen","group":"US - VA - Hampton Roads North","description":"","userSelectable":true},
         {"name":"Animal Kingdom","group":"US - FL - Orlando","description":"","userSelectable":false}]
        """;

    [Fact]
    public async Task ReadsV2AndAnswersTheArray()
    {
        var handler = new Scripted(new Reply(HttpStatusCode.OK, $$"""{"areas":{{Items}}}"""));

        var areas = await CreateSut(handler).GetAreasWithGroupsAsync("user1");

        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas", Assert.Single(handler.Urls));
        AssertSameItems(areas);
    }

    [Fact]
    public async Task AServerWithoutTheRouteIsAnUnsupportedServerNotAV1Retry()
    {
        var handler = new Scripted(
            new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"),
            new Reply(HttpStatusCode.OK, $$"""{"areas":{{Items}},"status":"ok"}"""));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(handler).GetAreasWithGroupsAsync("user1"));

        Assert.Contains("5.4.0", ex.Message, StringComparison.Ordinal);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/areas", Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task NeverRequestsTheV1HumanRoute()
    {
        var handler = new Scripted(new Reply(HttpStatusCode.OK, """{"areas":[]}"""));

        await CreateSut(handler).GetAreasWithGroupsAsync("user1");

        Assert.DoesNotContain(handler.Urls, u => u.Contains("/api/humans/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingHumanIsNullAsOnV1RatherThanARetry()
    {
        // AreaController answers an empty list for null, which is what v1's 404 "User not found" gave it.
        var handler = new Scripted(new Reply(
            HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"human not found"}""",
            "application/problem+json"));

        Assert.Null(await CreateSut(handler).GetAreasWithGroupsAsync("nobody"));
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task ANullListIsAnEmptyOne()
    {
        // V2AreasOutputBody declares areas as array-or-null.
        var handler = new Scripted(new Reply(HttpStatusCode.OK, """{"areas":null}"""));

        Assert.Equal("[]", await CreateSut(handler).GetAreasWithGroupsAsync("user1"));
    }

    [Fact]
    public async Task AWebhookIdIsEncodedIntoThePath()
    {
        var handler = new Scripted(new Reply(HttpStatusCode.OK, """{"areas":[]}"""));

        await CreateSut(handler).GetAreasWithGroupsAsync("https://discordapp.com/api/webhooks/1/tok");

        Assert.Equal(
            $"{ApiAddress}/api/v2/humans/https%3A%2F%2Fdiscordapp.com%2Fapi%2Fwebhooks%2F1%2Ftok/areas",
            Assert.Single(handler.Urls));
    }

    private static void AssertSameItems(string? areas)
    {
        Assert.NotNull(areas);
        using var actual = JsonDocument.Parse(areas);
        using var expected = JsonDocument.Parse(Items);
        Assert.Equal(expected.RootElement.GetArrayLength(), actual.RootElement.GetArrayLength());

        for (var i = 0; i < expected.RootElement.GetArrayLength(); i++)
        {
            Assert.Equal(
                expected.RootElement[i].EnumerateObject().Select(p => (p.Name, p.Value.ToString())),
                actual.RootElement[i].EnumerateObject().Select(p => (p.Name, p.Value.ToString())));
        }
    }

    private static PoracleApiProxy CreateSut(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build());

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json");

    private sealed class Scripted(params Reply[] replies) : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Urls.Add(request.RequestUri!.ToString());
            var reply = replies[Math.Min(this._next++, replies.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.ContentType),
            });
        }
    }
}
