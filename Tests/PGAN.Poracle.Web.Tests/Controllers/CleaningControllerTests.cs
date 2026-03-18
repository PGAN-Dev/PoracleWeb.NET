using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class CleaningControllerTests : ControllerTestBase
{
    private readonly Mock<ICleaningService> _service = new();
    private readonly CleaningController _sut;

    public CleaningControllerTests()
    {
        _sut = new CleaningController(_service.Object);
        SetupUser(_sut);
    }

    [Theory]
    [InlineData("monsters")]
    [InlineData("raids")]
    [InlineData("eggs")]
    [InlineData("quests")]
    [InlineData("invasions")]
    [InlineData("lures")]
    [InlineData("nests")]
    [InlineData("gyms")]
    public async Task ToggleClean_ReturnsOk_ForAllAlarmTypes(string alarmType)
    {
        _service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanRaidsAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanEggsAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanQuestsAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanInvasionsAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanLuresAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanNestsAsync("123456789", 1, 1)).ReturnsAsync(5);
        _service.Setup(s => s.ToggleCleanGymsAsync("123456789", 1, 1)).ReturnsAsync(5);

        var result = await _sut.ToggleClean(alarmType, 1);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ToggleClean_Throws_ForUnknownAlarmType()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ToggleClean("unknown", 1));
    }

    [Fact]
    public async Task ToggleClean_IsCaseInsensitive()
    {
        _service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(3);

        var result = await _sut.ToggleClean("MONSTERS", 1);

        Assert.IsType<OkObjectResult>(result);
    }
}
