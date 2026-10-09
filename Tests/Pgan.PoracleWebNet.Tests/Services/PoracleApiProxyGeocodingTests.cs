using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// <c>GetGeocodeForwardAsync</c>/<c>GetGeocodeReverseAsync</c> call PoracleNG's own <c>/api/geocode</c>
/// endpoints, which resolve via whichever provider the operator configured (Nominatim, Photon or Google)
/// and answer in one shape regardless -- this app never calls a provider URL directly and never parses
/// a provider's payload. A PoracleNG too old for either route falls back to calling its configured
/// provider directly, exactly as this app did before the routes existed. See #845.
/// </summary>
public class PoracleApiProxyGeocodingTests
{
    private const string ApiAddress = "http://localhost:3030";

    private static PoracleApiProxy CreateSut(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret"
            })
            .Build());

    [Fact]
    public async Task GetGeocodeForwardAsyncRequestsThePoracleEndpointWithTheQuery()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var sut = CreateSut(handler);

        await sut.GetGeocodeForwardAsync("1600 Pennsylvania Avenue");

        Assert.Equal(
            $"{ApiAddress}/api/geocode/forward?q=1600%20Pennsylvania%20Avenue",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
        Assert.Equal("test-secret", handler.LastRequest?.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task GetGeocodeForwardAsyncAppendsLanguageWhenGiven()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var sut = CreateSut(handler);

        await sut.GetGeocodeForwardAsync("Toledo", "de");

        Assert.Equal($"{ApiAddress}/api/geocode/forward?q=Toledo&language=de", handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetGeocodeForwardAsyncReturnsTheBodyOn200()
    {
        const string body = /*lang=json,strict*/ """[{"latitude":41.65,"longitude":-83.53,"displayName":"Toledo"}]""";
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, body));

        Assert.Equal(body, await sut.GetGeocodeForwardAsync("Toledo"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)] // No geocoder configured on this PoracleNG.
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetGeocodeForwardAsyncReturnsNullOnNonSuccessWithNoFallback(HttpStatusCode status)
    {
        var handler = new MockHttpMessageHandler(status, "{}");
        var sut = CreateSut(handler);

        Assert.Null(await sut.GetGeocodeForwardAsync("Toledo"));
        // Neither of those statuses means "route absent" -- a second (config) request would mean the
        // fallback fired when it should not have.
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetGeocodeForwardAsyncReturnsNullRatherThanThrowingWhenUnreachable()
    {
        var sut = CreateSut(new ThrowingHandler());

        Assert.Null(await sut.GetGeocodeForwardAsync("Toledo"));
    }

    [Fact]
    public async Task GetGeocodeReverseAsyncRequestsThePoracleEndpointWithCoordinatesInvariantly()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // A comma decimal separator must not leak into the query string.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            await sut.GetGeocodeReverseAsync(41.652813, -83.53721);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        Assert.Equal(
            $"{ApiAddress}/api/geocode/reverse?lat=41.652813&lon=-83.53721",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetGeocodeReverseAsyncAppendsLanguageWhenGiven()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.GetGeocodeReverseAsync(41.65, -83.53, "fr");

        Assert.Equal($"{ApiAddress}/api/geocode/reverse?lat=41.65&lon=-83.53&language=fr", handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetGeocodeReverseAsyncReturnsTheBodyOn200()
    {
        const string body = /*lang=json,strict*/ """{"displayName":"Toledo, Ohio","latitude":41.65,"longitude":-83.53}""";
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, body));

        Assert.Equal(body, await sut.GetGeocodeReverseAsync(41.65, -83.53));
    }

    [Fact]
    public async Task GetGeocodeReverseAsyncReturnsNullOnServiceUnavailableWithNoFallback()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = CreateSut(handler);

        Assert.Null(await sut.GetGeocodeReverseAsync(41.65, -83.53));
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// PoracleNG's reverse answers 404 as problem+json for "nothing at this coordinate" -- including
    /// every reverse call when <c>[geocoding] forward_only</c> is set, which disables reverse deliberately.
    /// That must read as "no address here", not "this PoracleNG is too old", or the fallback would
    /// silently reintroduce reverse geocoding against a provider the operator turned it off for.
    /// </summary>
    [Fact]
    public async Task GetGeocodeReverseAsyncDoesNotFallBackOnASemanticNotFound()
    {
        const string problemJson = /*lang=json,strict*/ """{"title":"Not Found","status":404,"detail":"nothing here"}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, problemJson);
        var sut = CreateSut(handler);

        Assert.Null(await sut.GetGeocodeReverseAsync(41.65, -83.53));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetGeocodeForwardAsyncFallsBackToTheConfiguredProviderWhenTheRouteIsAbsent()
    {
        var handler = new RoutingHandler();
        handler.RespondTo("/api/geocode/forward", HttpStatusCode.NotFound, "404 page not found");
        handler.RespondTo(
            "/api/config/poracleWeb", HttpStatusCode.OK, /*lang=json,strict*/ """{"providerURL":"http://nominatim.example/"}""");
        const string legacyBody = /*lang=json,strict*/ """[{"lat":"41.65","lon":"-83.53","display_name":"Toledo"}]""";
        handler.RespondTo("/search", HttpStatusCode.OK, legacyBody);
        var sut = CreateSut(handler);

        var result = await sut.GetGeocodeForwardAsync("Toledo");

        Assert.Equal(legacyBody, result);
        var legacyRequest = Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath == "/search");
        Assert.Equal(
            "http://nominatim.example/search?addressdetails=1&q=Toledo&format=json&limit=5", legacyRequest.RequestUri!.ToString());
        Assert.False(legacyRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task GetGeocodeReverseAsyncFallsBackToTheConfiguredProviderWhenTheRouteIsAbsent()
    {
        var handler = new RoutingHandler();
        handler.RespondTo("/api/geocode/reverse", HttpStatusCode.NotFound, "404 page not found");
        handler.RespondTo(
            "/api/config/poracleWeb", HttpStatusCode.OK, /*lang=json,strict*/ """{"providerURL":"http://nominatim.example"}""");
        const string legacyBody = /*lang=json,strict*/ """{"display_name":"Toledo, Ohio"}""";
        handler.RespondTo("/reverse", HttpStatusCode.OK, legacyBody);
        var sut = CreateSut(handler);

        var result = await sut.GetGeocodeReverseAsync(41.65, -83.53);

        Assert.Equal(legacyBody, result);
        var legacyRequest = Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath == "/reverse");
        Assert.Equal("http://nominatim.example/reverse?lat=41.65&lon=-83.53&format=json&addressdetails=1", legacyRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task FallbackReturnsNullWhenTheOlderServerHasNoProviderConfiguredEither()
    {
        var handler = new RoutingHandler();
        handler.RespondTo("/api/geocode/forward", HttpStatusCode.NotFound, "404 page not found");
        handler.RespondTo("/api/config/poracleWeb", HttpStatusCode.OK, /*lang=json,strict*/ """{}""");
        var sut = CreateSut(handler);

        Assert.Null(await sut.GetGeocodeForwardAsync("Toledo"));
    }

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        public int RequestCount
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastRequest = request;
            this.RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }

    /// <summary>Answers each request by the first registered path its URI starts with.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly List<(string Path, HttpStatusCode Status, string Body)> _routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public void RespondTo(string pathContains, HttpStatusCode status, string body) => this._routes.Add((pathContains, status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var route = this._routes.FirstOrDefault(r => path.Contains(r.Path, StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(route.Status)
            {
                Content = new StringContent(route.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
