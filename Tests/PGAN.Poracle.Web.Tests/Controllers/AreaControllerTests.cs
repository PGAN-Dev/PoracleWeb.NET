using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class AreaControllerTests : ControllerTestBase
{
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<AreaController>> _logger = new();
    private readonly AreaController _sut;

    public AreaControllerTests()
    {
        _sut = new AreaController(_humanService.Object, _proxy.Object, _logger.Object);
        SetupUser(_sut);
    }

    // --- GetSelectedAreas ---

    [Fact]
    public async Task GetSelectedAreas_ReturnsNotFound_WhenHumanMissing()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetSelectedAreas());
    }

    [Fact]
    public async Task GetSelectedAreas_ReturnsEmptyArray_WhenAreaIsNull()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(new Human { Id = "123456789", Area = null });

        var result = await _sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Empty(areas);
    }

    [Fact]
    public async Task GetSelectedAreas_ReturnsEmptyArray_WhenAreaIsWhitespace()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(new Human { Id = "123456789", Area = "   " });

        var result = await _sut.GetSelectedAreas();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task GetSelectedAreas_ParsesJsonArray()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Area = "[\"west end\",\"downtown\"]" });

        var result = await _sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Equal(2, areas.Length);
        Assert.Contains("west end", areas);
        Assert.Contains("downtown", areas);
    }

    [Fact]
    public async Task GetSelectedAreas_FallsBackToCommaSeparated_WhenJsonInvalid()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789"))
            .ReturnsAsync(new Human { Id = "123456789", Area = "west end,downtown" });

        var result = await _sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var areas = Assert.IsType<string[]>(ok.Value);
        Assert.Equal(2, areas.Length);
    }

    // --- GetAvailableAreas ---

    [Fact]
    public async Task GetAvailableAreas_ReturnsContent_WhenProxyReturnsData()
    {
        _proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync("[{\"name\":\"area1\"}]");
        var result = await _sut.GetAvailableAreas();
        Assert.IsType<ContentResult>(result);
    }

    [Fact]
    public async Task GetAvailableAreas_ReturnsOkEmpty_WhenProxyReturnsNull()
    {
        _proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync((string?)null);
        Assert.IsType<OkObjectResult>(await _sut.GetAvailableAreas());
    }

    [Fact]
    public async Task GetAvailableAreas_ReturnsOkEmpty_WhenProxyThrows()
    {
        _proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ThrowsAsync(new HttpRequestException());
        Assert.IsType<OkObjectResult>(await _sut.GetAvailableAreas());
    }

    // --- UpdateAreas ---

    [Fact]
    public async Task UpdateAreas_ReturnsNotFound_WhenHumanMissing()
    {
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["a"] }));
    }

    [Fact]
    public async Task UpdateAreas_SetsAreasAsJsonArray()
    {
        var human = new Human { Id = "123456789" };
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["west", "east"] });

        Assert.Equal("[\"west\",\"east\"]", human.Area);
    }

    [Fact]
    public async Task UpdateAreas_SetsEmptyJsonArray_WhenAreasNull()
    {
        var human = new Human { Id = "123456789" };
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = null });

        Assert.Equal("[]", human.Area);
    }

    [Fact]
    public async Task UpdateAreas_SetsEmptyJsonArray_WhenAreasEmpty()
    {
        var human = new Human { Id = "123456789" };
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await _sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = [] });

        Assert.Equal("[]", human.Area);
    }

    // --- GetGeofencePolygons ---

    [Fact]
    public async Task GetGeofencePolygons_ReturnsContent_WhenAvailable()
    {
        _proxy.Setup(p => p.GetAllGeofenceDataAsync()).ReturnsAsync("{\"geofence\":[]}");
        Assert.IsType<ContentResult>(await _sut.GetGeofencePolygons());
    }

    [Fact]
    public async Task GetGeofencePolygons_ReturnsFallback_WhenNull()
    {
        _proxy.Setup(p => p.GetAllGeofenceDataAsync()).ReturnsAsync((string?)null);
        Assert.IsType<OkObjectResult>(await _sut.GetGeofencePolygons());
    }

    [Fact]
    public async Task GetGeofencePolygons_ReturnsFallback_WhenThrows()
    {
        _proxy.Setup(p => p.GetAllGeofenceDataAsync()).ThrowsAsync(new Exception());
        Assert.IsType<OkObjectResult>(await _sut.GetGeofencePolygons());
    }

    // --- GetAreaMap ---

    [Fact]
    public async Task GetAreaMap_ReturnsOk_WhenMapUrlAvailable()
    {
        _proxy.Setup(p => p.GetAreaMapUrlAsync("downtown")).ReturnsAsync("https://example.com/map.png");
        var result = await _sut.GetAreaMap("downtown");
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetAreaMap_ReturnsNotFound_WhenNull()
    {
        _proxy.Setup(p => p.GetAreaMapUrlAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetAreaMap("unknown"));
    }

    [Fact]
    public async Task GetAreaMap_ReturnsNotFound_WhenThrows()
    {
        _proxy.Setup(p => p.GetAreaMapUrlAsync(It.IsAny<string>())).ThrowsAsync(new Exception());
        Assert.IsType<NotFoundResult>(await _sut.GetAreaMap("bad"));
    }
}
