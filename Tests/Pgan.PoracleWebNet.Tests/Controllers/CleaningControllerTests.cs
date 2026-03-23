using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class CleaningControllerTests : ControllerTestBase
{
    private readonly Mock<ICleaningService> _service = new();
    private readonly CleaningController _sut;

    public CleaningControllerTests()
    {
        this._sut = new CleaningController(this._service.Object);
        SetupUser(this._sut);
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
    public async Task ToggleCleanReturnsOkForAllAlarmTypes(string alarmType)
    {
        this._service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanRaidsAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanEggsAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanQuestsAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanInvasionsAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanLuresAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanNestsAsync("123456789", 1, 1)).ReturnsAsync(5);
        this._service.Setup(s => s.ToggleCleanGymsAsync("123456789", 1, 1)).ReturnsAsync(5);

        var result = await this._sut.ToggleClean(alarmType, 1);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ToggleCleanThrowsForUnknownAlarmType() => await Assert.ThrowsAsync<ArgumentException>(() => this._sut.ToggleClean("unknown", 1));

    [Fact]
    public async Task ToggleCleanIsCaseInsensitive()
    {
        this._service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(3);

        var result = await this._sut.ToggleClean("MONSTERS", 1);

        Assert.IsType<OkObjectResult>(result);
    }
}
