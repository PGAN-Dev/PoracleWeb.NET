using Pgan.PoracleWebNet.Core.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AreaControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<IUserGeofenceService> _userGeofenceService = new();
    private readonly Mock<IUserAreaWriter> _areaWriter = new();
    private readonly Mock<ILogger<AreaController>> _logger = new();
    private readonly AreaController _sut;
    private static readonly string[] AreasArray = ["west", "east"];

    public AreaControllerTests()
    {
        this._sut = new AreaController(
            this._humanProxy.Object,
            this._proxy.Object,
            this._userGeofenceService.Object,
            this._areaWriter.Object,
            this._logger.Object);
        // Default: the writer keeps everything it is given.
        this._areaWriter
            .Setup(w => w.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> r) => new AreaSaveResult([.. r], []));
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
    public async Task GetSelectedAreasReadsAnArrayShapedArea()
    {
        // jfberry/PoracleNG#303: GET /api/v2/humans/{id} sends area as a real array.
        var humanJson = JsonSerializer.Deserialize<JsonElement>(/*lang=json,strict*/ "{\"area\":[\"west end\",\"downtown\"]}");
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync(humanJson);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["west end", "downtown"], Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task GetSelectedAreasReturnsEmptyForAnEmptyArrayShapedArea()
    {
        var humanJson = JsonSerializer.Deserialize<JsonElement>(/*lang=json,strict*/ "{\"area\":[]}");
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789")).ReturnsAsync(humanJson);

        var result = await this._sut.GetSelectedAreas();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<string[]>(ok.Value));
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

    /// <summary>
    /// PoracleNG returns its whole fence set regardless of userSelectable, and PoracleWeb feeds every
    /// user-drawn geofence into it, so streaming the response through handed any signed-in user the names
    /// of every private geofence anyone had drawn. See #544.
    /// </summary>
    [Fact]
    public async Task GetAvailableAreasHidesOtherUsersPrivateGeofences()
    {
        this._proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync(
            /*lang=json,strict*/
            "[{\"name\":\"downtown\",\"userSelectable\":true},{\"name\":\"someone elses home\",\"userSelectable\":false}]");

        var result = Assert.IsType<ContentResult>(await this._sut.GetAvailableAreas());

        Assert.Contains("downtown", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("someone elses home", result.Content, StringComparison.Ordinal);
    }

    /// <summary>The caller's own fences are theirs to see, even though they are not selectable.</summary>
    [Fact]
    public async Task GetAvailableAreasKeepsTheCallersOwnGeofences()
    {
        this._proxy.Setup(p => p.GetAreasWithGroupsAsync("123456789")).ReturnsAsync(
            /*lang=json,strict*/
            "[{\"name\":\"my garden\",\"userSelectable\":false},{\"name\":\"someone elses home\",\"userSelectable\":false}]");
        this._userGeofenceService.Setup(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([new UserGeofence { Id = 1, HumanId = "123456789", KojiName = "my garden" }]);

        var result = Assert.IsType<ContentResult>(await this._sut.GetAvailableAreas());

        Assert.Contains("my garden", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("someone elses home", result.Content, StringComparison.Ordinal);
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
    public async Task UpdateAreasSavesTheLowercasedListThroughTheWriterOnly()
    {
        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["West", "EAST"] });

        this._areaWriter.Verify(w => w.SaveSelectionAsync("123456789",
            It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(AreasArray))), Times.Once);
        // The old two-step write is gone: no untrusted setAreas, no patch-up (the proxy no longer has one).
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["west", "east"], Assert.IsType<string[]>(ok.Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAreasSavesAnEmptyListWhenAreasAreNullOrEmpty(bool isNull)
    {
        await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = isNull ? null : [] });

        this._areaWriter.Verify(w => w.SaveSelectionAsync("123456789",
            It.Is<IReadOnlyCollection<string>>(c => c.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task UpdateAreasLetsARefusalReachTheExceptionFilter()
    {
        this._areaWriter
            .Setup(w => w.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ThrowsAsync(new AreaNotPermittedException(["other fence"]));

        var ex = await Assert.ThrowsAsync<AreaNotPermittedException>(
            () => this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["other fence"] }));

        Assert.Equal(["other fence"], ex.Names);
    }

    [Fact]
    public async Task UpdateAreasEchoesWhatWasStoredWhenAPoracleDroppedName()
    {
        // No read-back available: the fallback is what the writer reports PoracleNG kept, not the request.
        this._areaWriter
            .Setup(w => w.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new AreaSaveResult(["west"], ["old & gone"]));
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789"))
            .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["west", "old & gone"] });

        Assert.Equal(["west"], Assert.IsType<string[]>(Assert.IsType<OkObjectResult>(result).Value));
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ "{\"area\":\"[\\\"west\\\"]\"}")]
    [InlineData(/*lang=json,strict*/ "{\"area\":[\"west\"]}")]
    public async Task UpdateAreasReadsBackTheStoredListInEitherPoracleShape(string humanBody)
    {
        // PoracleNG drops names it will not accept, so the response is the stored list, not the echo.
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789"))
            .ReturnsAsync(JsonDocument.Parse(humanBody).RootElement.Clone());

        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["West", "Dropped"] });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["west"], Assert.IsType<string[]>(ok.Value));
    }

    [Fact]
    public async Task UpdateAreasThatLandedIsNotReportedAsAFailureWhenTheReadBackFails()
    {
        // The save has succeeded by the time this read runs. It throws on a failed answer since #994;
        // a 500 here would tell the user a save that took effect did not. The submitted list is the
        // fallback it always was.
        this._humanProxy.Setup(p => p.GetAreasAsync("123456789"))
            .ThrowsAsync(new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable));

        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["West", "EAST"] });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["west", "east"], Assert.IsType<string[]>(ok.Value));
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

    // --- Client input must not 500 (#418) ---

    [Fact]
    public async Task UpdateAreasRejectsNullEntriesInsteadOfThrowing()
    {
        var result = await this._sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["west", null!] });

        Assert.IsType<BadRequestObjectResult>(result);
        this._areaWriter.Verify(w => w.SaveSelectionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

}
