using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AdminAreaControllerTests : ControllerTestBase
{
    private readonly Mock<IKojiService> _koji = new();
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Mock<IPoracleApiProxy> _poracleApi = new();
    private readonly AdminAreaController _sut;

    public AdminAreaControllerTests()
    {
        this._sut = new AdminAreaController(
            this._koji.Object, this._settings.Object, this._poracleApi.Object, NullLogger<AdminAreaController>.Instance);
        SetupUser(this._sut, isAdmin: true);
    }

    private static string[] Names(int count) => [.. Enumerable.Range(0, count).Select(i => $"area{i}")];

    /// <summary>
    /// A 501-name PUT stored the first 500 and answered 200, so one area stayed on the menu with the
    /// operator told it was hidden. The list is validated before anything is dropped.
    /// </summary>
    [Fact]
    public async Task AListOverTheCapIsRefusedRatherThanTrimmed()
    {
        var result = await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = Names(501) });

        Assert.IsType<BadRequestObjectResult>(result);
        this._settings.Verify(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()), Times.Never);
    }

    [Fact]
    public async Task AListAtTheCapIsSavedWhole()
    {
        SiteSetting? saved = null;
        this._settings.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .Callback<SiteSetting>(s => saved = s)
            .ReturnsAsync((SiteSetting s) => s);

        var result = await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = Names(500) });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(500, HiddenAreas.Parse(saved!.Value).Count);
    }

    [Fact]
    public async Task DuplicatesDoNotCountAgainstTheCap()
    {
        // 501 entries that normalise to 500 names is a list of 500.
        this._settings.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>())).ReturnsAsync((SiteSetting s) => s);
        var names = Names(500).Append("AREA0").ToArray();

        Assert.IsType<OkObjectResult>(await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = names }));
    }
}
