using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AreaControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<AreaController>> _logger = new();
    private readonly AreaController _sut;

    public AreaControllerTests()
    {
        this._sut = new AreaController(this._humanProxy.Object, this._proxy.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    // --- GetSelectedAreas ---

    [Fact]
    public async Task GetSelectedAreasReadsFromProxy()
    {
        var humanJson = JsonSerializer.Deserialize<JsonElement>(/*lang=json,strict*/ "{\"area\":\"[\\\"west end\\\",\\\"downtown\\\"]\"}");
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync(humanJson);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Equal(2, areas.Length);
        Assert.Contains("west end", areas);
        Assert.Contains("downtown", areas);
    }

    [Fact]
    public async Task GetSelectedAreasReturnsEmptyWhenAreaIsNull()
    {
        var humanJson = JsonSerializer.Deserialize<JsonElement>(/*lang=json,strict*/ "{\"area\":null}");
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync(humanJson);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task GetSelectedAreasReturnsEmptyWhenNoAreaProperty()
    {
        var humanJson = JsonSerializer.Deserialize<JsonElement>(/*lang=json,strict*/ "{\"id\":\"123456789\"}");
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync(humanJson);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task GetSelectedAreasReturnsNotFoundWhenProxyReturnsNull()
    {
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync((JsonElement?)null);

        Assert.IsType<NotFoundResult>(await this._sut.GetSelectedAreas());
    }

    // --- GetAvailableAreas ---

    [Fact]
    public async Task GetAvailableAreasReturnsContentWhenProxyReturnsData()
    {
        this._proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync(/*lang=json,strict*/ "[{\"name\":\"area1\"}]");
        var result = await this._sut.GetAvailableAreas();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetAvailableAreasReturnsOkEmptyWhenProxyReturnsNull()
    {
        this._proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync((string?)null);
        Assert.IsType<OkObjectResult>(await this._sut.GetAvailableAreas());
    }

    [Fact]
    public async Task GetAvailableAreasReturnsOkEmptyWhenProxyThrows()
    {
        this._proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ThrowsAsync(new HttpRequestException());
        Assert.IsType<OkObjectResult>(await this._sut.GetAvailableAreas());
    }

    // --- UpdateAreas ---

    [Fact]
    public async Task UpdateAreasCallsProxyWithNormalizedAreas()
    {
        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["West", "EAST"] });

        this._humanProxy.Verify(p => p.SetAreasAsync("123456789", new[] { "west", "east" }), Times.Once);
        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Equal(["west", "east"], areas);
    }

    [Fact]
    public async Task UpdateAreasCallsProxyWithEmptyArrayWhenAreasNull()
    {
        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = null });

        this._humanProxy.Verify(p => p.SetAreasAsync("123456789", Array.Empty<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAreasCallsProxyWithEmptyArrayWhenAreasEmpty()
    {
        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = [] });

        this._humanProxy.Verify(p => p.SetAreasAsync("123456789", Array.Empty<string>()), Times.Once);
    }

    // --- GetGeofencePolygons ---

    [Fact]
    public async Task GetGeofencePolygonsReturnsContentWhenAvailable()
    {
        this._proxy.Setup(p => p.GetAllGeofenceDataAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"geofence\":[]}");
        Assert.IsType<ContentResult>(await this._sut.GetGeofencePolygons());
    }

    [Fact]
    public async Task GetGeofencePolygonsReturnsFallbackWhenNull()
    {
        this._proxy.Setup(p => p.GetAllGeofenceDataAsync()).ReturnsAsync((string?)null);
        Assert.IsType<OkObjectResult>(await this._sut.GetGeofencePolygons());
    }

    [Fact]
    public async Task GetGeofencePolygonsReturnsFallbackWhenThrows()
    {
        this._proxy.Setup(p => p.GetAllGeofenceDataAsync()).ThrowsAsync(new InvalidOperationException());
        Assert.IsType<OkObjectResult>(await this._sut.GetGeofencePolygons());
    }

    // --- GetAreaMap ---

    [Fact]
    public async Task GetAreaMapReturnsOkWhenMapUrlAvailable()
    {
        this._proxy.Setup(p => p.GetAreaMapUrlAsync("downtown")).ReturnsAsync("https://example.com/map.png");
        var result = await this._sut.GetAreaMap("downtown");
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetAreaMapReturnsNotFoundWhenNull()
    {
        this._proxy.Setup(p => p.GetAreaMapUrlAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetAreaMap("unknown"));
    }

    [Fact]
    public async Task GetAreaMapReturnsNotFoundWhenThrows()
    {
        this._proxy.Setup(p => p.GetAreaMapUrlAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException());
        Assert.IsType<NotFoundResult>(await this._sut.GetAreaMap("bad"));
    }
}
