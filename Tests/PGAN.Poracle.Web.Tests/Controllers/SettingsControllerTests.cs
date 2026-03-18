using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class SettingsControllerTests : ControllerTestBase
{
    private readonly Mock<IPwebSettingService> _service = new();
    private readonly SettingsController _sut;

    public SettingsControllerTests()
    {
        _sut = new SettingsController(_service.Object);
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        SetupUser(_sut);
        _service.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<PwebSetting> { new() { Setting = "k", Value = "v" } });
        Assert.IsType<OkObjectResult>(await _sut.GetAll());
    }

    [Fact]
    public async Task Upsert_ReturnsOk_WhenAdmin()
    {
        SetupUser(_sut, isAdmin: true);
        var setting = new PwebSetting { Value = "val" };
        _service.Setup(s => s.CreateOrUpdateAsync(It.IsAny<PwebSetting>())).ReturnsAsync(setting);

        var result = await _sut.Upsert("key1", setting);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("key1", setting.Setting);
    }

    [Fact]
    public async Task Upsert_ReturnsForbid_WhenNotAdmin()
    {
        SetupUser(_sut, isAdmin: false);
        var result = await _sut.Upsert("key1", new PwebSetting());
        Assert.IsType<ForbidResult>(result);
    }
}
