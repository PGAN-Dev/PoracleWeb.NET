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

    /// <summary>
    /// 500 names of 195 characters fit both per-name caps and the count, and serialise to about 99 KB,
    /// which the TEXT column cannot hold: the write answered 500 "An unexpected error occurred" and the
    /// previous list survived. Refused up front with the byte limit named, like every other
    /// <c>site_settings</c> writer.
    /// </summary>
    [Fact]
    public async Task AListTooLargeForTheColumnIsRefusedWithTheLimitNamed()
    {
        var names = Enumerable.Range(0, 500).Select(i => $"{i:D3}".PadRight(195, 'x')).ToArray();

        var result = await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = names });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("65535 bytes", System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
        this._settings.Verify(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()), Times.Never);
    }

    [Fact]
    public async Task TheByteLimitCountsUtf8NotCharacters()
    {
        // 300 names of 100 CJK characters: 30,000 characters, 90,000 bytes.
        var names = Enumerable.Range(0, 300).Select(i => $"{i:D3}" + new string('区', 97)).ToArray();

        Assert.IsType<BadRequestObjectResult>(
            await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = names }));
    }

    [Fact]
    public async Task AListJustUnderTheColumnIsSaved()
    {
        // The legitimate case beside the refusal: 500 names of 125 characters serialise to about 64 KB.
        this._settings.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>())).ReturnsAsync((SiteSetting s) => s);
        var names = Enumerable.Range(0, 500).Select(i => $"{i:D3}".PadRight(125, 'x')).ToArray();

        Assert.IsType<OkObjectResult>(await this._sut.SetHidden(new AdminAreaController.SetHiddenAreasRequest { Names = names }));
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
