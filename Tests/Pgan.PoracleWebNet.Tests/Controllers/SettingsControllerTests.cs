using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class SettingsControllerTests : ControllerTestBase
{
    private readonly Mock<IPwebSettingService> _service = new();
    private readonly SettingsController _sut;

    public SettingsControllerTests() => this._sut = new SettingsController(this._service.Object);

    [Fact]
    public async Task GetAllReturnsOk()
    {
        SetupUser(this._sut);
        this._service.Setup(s => s.GetAllAsync()).ReturnsAsync([new() { Setting = "k", Value = "v" }]);
        Assert.IsType<OkObjectResult>(await this._sut.GetAll());
    }

    [Fact]
    public async Task UpsertReturnsOkWhenAdmin()
    {
        SetupUser(this._sut, isAdmin: true);
        var setting = new PwebSetting { Value = "val" };
        this._service.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>())).ReturnsAsync(setting);

        var result = await this._sut.Upsert("key1", setting);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("key1", setting.Setting);
    }

    [Fact]
    public async Task UpsertReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        var result = await this._sut.Upsert("key1", new PwebSetting());
        Assert.IsType<ForbidResult>(result);
    }
}
