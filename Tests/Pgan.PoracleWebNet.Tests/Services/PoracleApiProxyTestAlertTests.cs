using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG explains a failed <c>/api/test</c> in its body ("message delivery not configured - check
/// Discord/Telegram token settings"). <c>EnsureSuccessStatusCode()</c> threw that away, so the user was
/// told to try again later about something retrying cannot fix.
/// </summary>
public class PoracleApiProxyTestAlertTests
{
    private const string ApiAddress = "http://localhost:3030";

    private static PoracleApiProxy CreateSut(HttpStatusCode status, string body) => new(
        new HttpClient(new StubHandler(status, body)),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret"
            })
            .Build());

    private static TestAlertRequest Request() => new()
    {
        Type = "pokemon",
        Target = new TestAlertTarget
        {
            Id = "1",
            Name = "n",
            Type = "discord:user"
        },
        Webhook = new Dictionary<string, object?>()
    };

    [Fact]
    public async Task SendTestAlertAsyncSucceedsOn200()
    {
        var sut = CreateSut(HttpStatusCode.OK, "{}");

        await sut.SendTestAlertAsync(Request());
    }

    [Fact]
    public async Task SendTestAlertAsyncCarriesPoracleNgsDetailWhenItRefuses()
    {
        var sut = CreateSut(
            HttpStatusCode.InternalServerError,
            /*lang=json,strict*/ """{"title":"Internal Server Error","status":500,"detail":"message delivery not configured - check Discord/Telegram token settings"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.SendTestAlertAsync(Request()));

        Assert.Equal("message delivery not configured - check Discord/Telegram token settings", ex.Message);
        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task SendTestAlertAsyncReadsV1ErrorBodies()
    {
        var sut = CreateSut(HttpStatusCode.BadRequest, /*lang=json,strict*/ """{"status":"error","message":"no such target"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.SendTestAlertAsync(Request()));

        Assert.Equal("no such target", ex.Message);
    }

    [Fact]
    public async Task SendTestAlertAsyncSaysSomethingWhenTheBodyExplainsNothing()
    {
        var sut = CreateSut(HttpStatusCode.InternalServerError, string.Empty);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => sut.SendTestAlertAsync(Request()));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.DoesNotContain("alarm", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
