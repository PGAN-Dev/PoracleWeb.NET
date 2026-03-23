using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AreaControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<AreaController>> _logger = new();
    private readonly AreaController _sut;

    public AreaControllerTests()
    {
        this._sut = new AreaController(this._humanService.Object, this._profileService.Object, this._proxy.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    // --- GetSelectedAreas ---

    [Fact]
    public async Task GetSelectedAreasReadsFromProfileArea()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 1, Area = "[\"west end\",\"downtown\"]" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Equal(2, areas.Length);
        Assert.Contains("west end", areas);
        Assert.Contains("downtown", areas);
    }

    [Fact]
    public async Task GetSelectedAreasReturnsEmptyWhenProfileAreaIsNull()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 1, Area = null! };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task GetSelectedAreasFallsBackToHumanWhenProfileNotFound()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        this._humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Area = "[\"fallback\"]" });

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Single(areas);
        Assert.Contains("fallback", areas);
    }

    [Fact]
    public async Task GetSelectedAreasReturnsNotFoundWhenNoProfileAndNoHuman()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync((Profile?)null);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);

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
    public async Task UpdateAreasReturnsNotFoundWhenHumanMissing()
    {
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["a"] }));
    }

    [Fact]
    public async Task UpdateAreasSetsAreasAsJsonArray()
    {
        var human = new Human { Id = "123456789" };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["west", "east"] });

        Assert.Equal("[\"west\",\"east\"]", human.Area);
    }

    [Fact]
    public async Task UpdateAreasSetsEmptyJsonArrayWhenAreasNull()
    {
        var human = new Human { Id = "123456789" };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = null });

        Assert.Equal("[]", human.Area);
    }

    [Fact]
    public async Task UpdateAreasSetsEmptyJsonArrayWhenAreasEmpty()
    {
        var human = new Human { Id = "123456789" };
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = [] });

        Assert.Equal("[]", human.Area);
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
