using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// <c>POST /api/alarm-move</c> refuses while someone else's account is being inspected, because a move writes the
/// other human's alarms. <c>PUT /{uid}</c> naming the other destination is the same move, so it is refused with the
/// same words; an in-place edit is not a move and still works.
/// </summary>
public class AlarmMoveWhileImpersonatingTests : ControllerTestBase
{
    private const string Discord = "100000000000000001";
    private const string Message = "Alerts can't be moved while inspecting another account.";

    private static LinkedAlarmRouter Router() => LinkedTestKit.Router(LinkedTestKit.Linked());

    private static void AssertRefused(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        Assert.Equal(Message, obj.Value!.GetType().GetProperty("error")!.GetValue(obj.Value));
    }

    [Fact]
    public async Task Raid_PutNamingTheOtherSide_IsRefused_AndWritesNothing()
    {
        var raids = new Mock<IRaidService>();
        raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(new Raid { Uid = 1 });
        var sut = new RaidController(raids.Object, Router());
        SetupImpersonatingUser(sut, Discord);

        AssertRefused(await sut.Update(1, new RaidUpdate { Destination = "telegram" }));

        raids.Verify(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<Raid>()), Times.Never);
        raids.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Raid_PutNamingItsOwnSide_StillEditsInPlace()
    {
        var existing = new Raid { Uid = 1 };
        var raids = new Mock<IRaidService>();
        raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(existing);
        raids.Setup(r => r.UpdateAsync(Discord, existing)).ReturnsAsync(existing);
        var sut = new RaidController(raids.Object, Router());
        SetupImpersonatingUser(sut, Discord);

        var result = await sut.Update(1, new RaidUpdate { Destination = "discord", Distance = 500 });

        Assert.IsType<OkObjectResult>(result);
        raids.Verify(r => r.UpdateAsync(Discord, existing), Times.Once);
    }

    [Fact]
    public async Task Monster_PutNamingTheOtherSide_IsRefused_AndWritesNothing()
    {
        var monsters = new Mock<IMonsterService>();
        monsters.Setup(m => m.GetByUidAsync(Discord, 1)).ReturnsAsync(new Monster { Uid = 1 });
        var sut = new MonsterController(monsters.Object, Mock.Of<IPokemonEditCapabilityService>(), Router());
        SetupImpersonatingUser(sut, Discord);

        AssertRefused(await sut.Update(1, new MonsterUpdate { Destination = "telegram" }));

        monsters.Verify(m => m.CreateAsync(It.IsAny<string>(), It.IsAny<Monster>()), Times.Never);
    }

    [Fact]
    public async Task Monster_PutWithNoDestination_StillEditsInPlace()
    {
        var existing = new Monster { Uid = 1 };
        var monsters = new Mock<IMonsterService>();
        monsters.Setup(m => m.GetByUidAsync(Discord, 1)).ReturnsAsync(existing);
        monsters.Setup(m => m.UpdateAsync(Discord, existing)).ReturnsAsync(existing);
        var sut = new MonsterController(monsters.Object, Mock.Of<IPokemonEditCapabilityService>(), Router());
        SetupImpersonatingUser(sut, Discord);

        Assert.IsType<OkObjectResult>(await sut.Update(1, new MonsterUpdate { Distance = 500 }));
    }

    [Fact]
    public async Task APutMoveWithoutImpersonationIsStillAMove()
    {
        var raids = new Mock<IRaidService>();
        raids.Setup(r => r.GetByUidAsync(Discord, 1)).ReturnsAsync(new Raid { Uid = 1 });
        var sut = new RaidController(raids.Object, Router());
        SetupUser(sut, Discord);

        var result = await sut.Update(1, new RaidUpdate { Destination = "telegram" });

        // The inert mover refuses for want of collaborators; what matters is that it was attempted, not blocked with a 403.
        Assert.NotEqual(403, (result as ObjectResult)?.StatusCode);
    }
}
