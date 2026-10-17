using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The two personal-shiny settings read out of <c>/api/config/values</c>: <c>shiny_rates.url</c>
/// (whether a rate feed is configured) and <c>player_accounts.self_service</c> (whether trainers may
/// link their own game accounts). PoracleNG PR #255.
/// </summary>
public class PoracleApiProxyPersonalConfigTests
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

    [Fact]
    public async Task ReadsTheShinyRatesUrl()
    {
        var sut = CreateSut(HttpStatusCode.OK,
            /*lang=json,strict*/ """{"values":{"shiny_rates":{"url":"https://example.test/rates.json","token":"****"}}}""");

        Assert.Equal("https://example.test/rates.json", await sut.GetShinyRatesUrlAsync());
    }

    [Fact]
    public async Task ReadsSelfService()
    {
        var sut = CreateSut(HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"player_accounts":{"self_service":true}}}""");

        Assert.True(await sut.GetPlayerAccountsSelfServiceAsync());
    }

    [Fact]
    public async Task AnAbsentKeyIsNull()
    {
        var sut = CreateSut(HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"shiny_rates":{},"player_accounts":{}}}""");

        Assert.Null(await sut.GetShinyRatesUrlAsync());
        Assert.Null(await sut.GetPlayerAccountsSelfServiceAsync());
    }

    [Fact]
    public async Task ANonStringUrlIsNull()
    {
        var sut = CreateSut(HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"shiny_rates":{"url":42}}}""");

        Assert.Null(await sut.GetShinyRatesUrlAsync());
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
