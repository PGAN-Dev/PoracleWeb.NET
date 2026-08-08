using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// <c>GetGruntsAsync</c> requested <c>/api/config/grunts</c>, a path neither supported backend serves.
/// Every call 404'd, <c>EnsureSuccessStatusCode()</c> threw, and <c>GET /api/masterdata/grunts</c>
/// returned 500 to anonymous callers — while the controller's own "Grunt data not available" branch
/// sat unreachable. See #419.
/// </summary>
public class PoracleApiProxyGruntsTests
{
    private const string ApiAddress = "http://localhost:3030";

    private static PoracleApiProxy CreateSut(MockHttpMessageHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret"
            })
            .Build());

    [Fact]
    public async Task GetGruntsAsyncRequestsTheMasterdataPath()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"10":{"type":"Dark"}}""");
        var sut = CreateSut(handler);

        await sut.GetGruntsAsync();

        Assert.Equal($"{ApiAddress}/api/masterdata/grunts", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetGruntsAsyncReturnsTheBodyOn200()
    {
        const string body = /*lang=json,strict*/ """{"10":{"type":"Dark","gender":2}}""";
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, body));

        Assert.Equal(body, await sut.GetGruntsAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetGruntsAsyncReturnsNullInsteadOfThrowing(HttpStatusCode status)
    {
        // The old EnsureSuccessStatusCode() turned every upstream failure into a 500 from our own API
        // and made the controller's 404 branch dead code.
        var sut = CreateSut(new MockHttpMessageHandler(status, "{}"));

        Assert.Null(await sut.GetGruntsAsync());
    }

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
