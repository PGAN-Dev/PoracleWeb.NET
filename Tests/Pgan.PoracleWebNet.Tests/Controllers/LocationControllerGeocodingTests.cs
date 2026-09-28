using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public sealed class LocationControllerGeocodingTests : ControllerTestBase, IDisposable
{
    private const string PhotonReverse = /*lang=json,strict*/ """
        {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"type":"house","housenumber":"420",
          "street":"Madison Avenue","city":"Toledo","country":"United States"},
          "geometry":{"type":"Point","coordinates":[-83.53721,41.652813]}}]}
        """;

    private const string NominatimReverse = /*lang=json,strict*/ """{"display_name":"420, Madison Avenue, Toledo"}""";

    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly CapturingHandler _handler = new();

    public void Dispose() => this._handler.Dispose();

    private LocationController CreateSut(GeocodingSettings? settings)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(this._handler, disposeHandler: false));
        var sut = new LocationController(
            Mock.Of<IHumanService>(),
            Mock.Of<IProfileService>(),
            Mock.Of<IPoracleHumanProxy>(),
            this._proxy.Object,
            factory.Object,
            Mock.Of<IPlaceUpdateCapabilityService>(),
            scannerService: null,
            geocodingSettings: settings is null ? null : Options.Create(settings));
        SetupUser(sut);
        return sut;
    }

    // What every instance did before these settings existed must still work unchanged: PoracleNG's
    // providerURL, spoken to as Nominatim, with Nominatim's answer passed straight through.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutSettingsReverseUsesPoracleUrlAsNominatim(bool registerEmptySettings)
    {
        this._handler.Body = NominatimReverse;
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { ProviderUrl = "http://nominatim.example/" });
        var sut = this.CreateSut(registerEmptySettings ? new GeocodingSettings() : null);

        var result = Assert.IsType<ContentResult>(await sut.ReverseGeocode(41.6, -83.5));

        Assert.StartsWith("http://nominatim.example/reverse?lat=", this._handler.LastUrl);
        Assert.Contains("format=json&addressdetails=1", this._handler.LastUrl);
        Assert.Equal(NominatimReverse, result.Content);
    }

    [Fact]
    public async Task WithoutSettingsSearchUsesPoracleUrlAsNominatim()
    {
        this._handler.Body = "[]";
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { ProviderUrl = "http://nominatim.example" });
        var sut = this.CreateSut(new GeocodingSettings());

        Assert.IsType<ContentResult>(await sut.Geocode("Toledo"));

        Assert.StartsWith("http://nominatim.example/search?", this._handler.LastUrl);
        Assert.Contains("format=json", this._handler.LastUrl);
    }

    [Fact]
    public async Task ReverseUsesPhotonAndAnswersInNominatimShape()
    {
        this._handler.Body = PhotonReverse;
        var sut = this.CreateSut(new GeocodingSettings { Provider = "photon", ProviderUrl = "http://host.docker.internal:8181" });

        var result = Assert.IsType<ContentResult>(await sut.ReverseGeocode(41.652813, -83.53721));

        Assert.Equal("http://host.docker.internal:8181/reverse?lat=41.652813&lon=-83.53721&limit=1", this._handler.LastUrl);
        using var doc = JsonDocument.Parse(result.Content!);
        Assert.Equal("420, Madison Avenue, Toledo, United States", doc.RootElement.GetProperty("display_name").GetString());
    }

    // The override exists because PoracleNG's URL is written for the bot's host; it must not be consulted.
    [Fact]
    public async Task OverrideUrlSkipsPoracleConfig()
    {
        this._handler.Body = /*lang=json,strict*/ """{"type":"FeatureCollection","features":[]}""";
        var sut = this.CreateSut(new GeocodingSettings { Provider = "photon", ProviderUrl = "http://photon:2322/" });

        var result = Assert.IsType<ContentResult>(await sut.Geocode("Toledo"));

        Assert.Equal("http://photon:2322/api?q=Toledo&limit=5", this._handler.LastUrl);
        Assert.Equal("[]", result.Content);
        this._proxy.Verify(p => p.GetConfigAsync(), Times.Never);
    }

    [Fact]
    public async Task PhotonProviderFallsBackToPoracleUrl()
    {
        this._handler.Body = PhotonReverse;
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { ProviderUrl = "http://photon.example" });
        var sut = this.CreateSut(new GeocodingSettings { Provider = "Photon" });

        var result = Assert.IsType<ContentResult>(await sut.ReverseGeocode(41.6, -83.5));

        // Photon's request and answer, not Nominatim's: Photon refuses "format" with a 400.
        Assert.Equal("http://photon.example/reverse?lat=41.6&lon=-83.5&limit=1", this._handler.LastUrl);
        using var doc = JsonDocument.Parse(result.Content!);
        Assert.Equal("420, Madison Avenue, Toledo, United States", doc.RootElement.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task OverrideUrlWithoutProviderStaysNominatim()
    {
        this._handler.Body = NominatimReverse;
        var sut = this.CreateSut(new GeocodingSettings { ProviderUrl = "http://nominatim.local" });

        var result = Assert.IsType<ContentResult>(await sut.ReverseGeocode(41.6, -83.5));

        Assert.StartsWith("http://nominatim.local/reverse?", this._handler.LastUrl);
        Assert.Contains("format=json", this._handler.LastUrl);
        Assert.Equal(NominatimReverse, result.Content);
    }

    [Fact]
    public async Task PhotonErrorIsServiceUnavailable()
    {
        this._handler.Status = HttpStatusCode.BadRequest;
        var sut = this.CreateSut(new GeocodingSettings { Provider = "photon", ProviderUrl = "http://photon" });

        var result = Assert.IsType<ObjectResult>(await sut.ReverseGeocode(41.6, -83.5));

        Assert.Equal(503, result.StatusCode);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{}";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? LastUrl
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(this.Status)
            {
                Content = new StringContent(this.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
