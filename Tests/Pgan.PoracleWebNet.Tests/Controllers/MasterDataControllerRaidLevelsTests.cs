using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Coverage for the GET /api/masterdata/raid-levels endpoint added for #259.
/// </summary>
public class MasterDataControllerRaidLevelsTests
{
    private static readonly IReadOnlyList<RaidLevelInfo> SampleLevels =
    [
        new() { Value = 1, Category = "star", Name = "1 Star Raid", NamePlural = "1 Star Raids" },
        new() { Value = 9, Category = "special", Name = "Elite Raid", NamePlural = "Elite Raids" },
    ];

    private static MasterDataController CreateController(IRaidLevelService raidLevelService) => new(
        new Mock<IMasterDataService>().Object,
        new Mock<IPoracleApiProxy>().Object,
        raidLevelService);

    [Fact]
    public async Task GetRaidLevelsReturnsOkWithServicePayload()
    {
        var svc = new Mock<IRaidLevelService>();
        svc.Setup(s => s.GetAllAsync()).ReturnsAsync(SampleLevels);
        var sut = CreateController(svc.Object);

        var result = await sut.GetRaidLevels();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<IReadOnlyList<RaidLevelInfo>>(ok.Value, exactMatch: false);
        Assert.Equal(2, payload.Count);
        Assert.Equal(9, payload[1].Value);
        Assert.Equal("Elite Raid", payload[1].Name);
    }

    [Fact]
    public async Task GetRaidLevelsReturnsOkEvenWhenListIsEmpty()
    {
        var svc = new Mock<IRaidLevelService>();
        svc.Setup(s => s.GetAllAsync()).ReturnsAsync([]);
        var sut = CreateController(svc.Object);

        var result = await sut.GetRaidLevels();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<IReadOnlyList<RaidLevelInfo>>(ok.Value, exactMatch: false);
        Assert.Empty(payload);
    }
}
