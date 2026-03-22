using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class ScannerControllerTests : ControllerTestBase
{
    [Fact]
    public async Task GetActiveQuestsReturnsNotFoundWhenScannerNotConfigured()
    {
        var sut = new ScannerController(scannerService: null);
        SetupUser(sut);

        var result = await sut.GetActiveQuests();

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task GetActiveQuestsReturnsOkWhenScannerConfigured()
    {
        var service = new Mock<IScannerService>();
        var quests = new List<QuestData> { new() { PokestopId = "stop1", Name = "Test Stop" } };
        service.Setup(s => s.GetActiveQuestsAsync()).ReturnsAsync(quests);
        var sut = new ScannerController(service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveQuests();

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<IEnumerable<QuestData>>(ok.Value, exactMatch: false);
        Assert.Single(data);
    }

    [Fact]
    public async Task GetActiveRaidsReturnsNotFoundWhenScannerNotConfigured()
    {
        var sut = new ScannerController(scannerService: null);
        SetupUser(sut);

        var result = await sut.GetActiveRaids();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetActiveRaidsReturnsOkWhenScannerConfigured()
    {
        var service = new Mock<IScannerService>();
        var raids = new List<RaidData> { new() { GymId = "gym1", Level = 5 } };
        service.Setup(s => s.GetActiveRaidsAsync()).ReturnsAsync(raids);
        var sut = new ScannerController(service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveRaids();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(Assert.IsType<IEnumerable<RaidData>>(ok.Value, exactMatch: false));
    }
}
