using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// A linked caller editing or deleting an alarm the other side owns: the write goes to the owner, never to the
/// signed-in id. Two types, through the real router, so a controller that passes <c>this.UserId</c> fails here.
/// </summary>
public class LinkedAlarmOwnerRoutingTests : ControllerTestBase
{
    private const string Discord = LinkedTestKit.Discord;
    private const string Telegram = LinkedTestKit.Telegram;

    private static T Signed<T>(T controller)
        where T : ControllerBase
    {
        SetupUser(controller, Discord);
        return controller;
    }

    [Fact]
    public async Task Lure_PutOnTheOtherSidesUid_UpdatesTheOwner()
    {
        var lures = new Mock<ILureService>();
        lures.Setup(s => s.GetByUidAsync(Telegram, 42)).ReturnsAsync(new Lure { Uid = 42, LureId = 501, Distance = 100 });
        lures.Setup(s => s.UpdateAsync(Telegram, It.IsAny<Lure>())).ReturnsAsync((string _, Lure l) => l);
        var sut = Signed(new LureController(lures.Object, LinkedTestKit.Router(LinkedTestKit.Linked())));

        var result = await sut.Update(42, new LureUpdate { Distance = 250 });

        Assert.IsType<OkObjectResult>(result);
        lures.Verify(s => s.UpdateAsync(Telegram, It.Is<Lure>(l => l.Uid == 42 && l.Distance == 250)), Times.Once);
        lures.Verify(s => s.UpdateAsync(Discord, It.IsAny<Lure>()), Times.Never);
    }

    [Fact]
    public async Task Lure_DeleteOnTheOtherSidesUid_DeletesFromTheOwner()
    {
        var lures = new Mock<ILureService>();
        lures.Setup(s => s.GetByUidAsync(Telegram, 42)).ReturnsAsync(new Lure { Uid = 42 });
        var sut = Signed(new LureController(lures.Object, LinkedTestKit.Router(LinkedTestKit.Linked())));

        Assert.IsType<NoContentResult>(await sut.Delete(42));

        lures.Verify(s => s.DeleteAsync(Telegram, 42), Times.Once);
        lures.Verify(s => s.DeleteAsync(Discord, It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Gym_PutOnTheOtherSidesUid_UpdatesTheOwner()
    {
        var gyms = new Mock<IGymService>();
        gyms.Setup(s => s.GetByUidAsync(Telegram, 7)).ReturnsAsync(new Gym { Uid = 7, Distance = 100 });
        gyms.Setup(s => s.UpdateAsync(Telegram, It.IsAny<Gym>())).ReturnsAsync((string _, Gym g) => g);
        var sut = Signed(new GymController(gyms.Object, LinkedTestKit.Router(LinkedTestKit.Linked())));

        Assert.IsType<OkObjectResult>(await sut.Update(7, new GymUpdate { Distance = 300 }));

        gyms.Verify(s => s.UpdateAsync(Telegram, It.Is<Gym>(g => g.Uid == 7 && g.Distance == 300)), Times.Once);
        gyms.Verify(s => s.UpdateAsync(Discord, It.IsAny<Gym>()), Times.Never);
    }

    [Fact]
    public async Task Gym_DeleteOnTheOtherSidesUid_DeletesFromTheOwner()
    {
        var gyms = new Mock<IGymService>();
        gyms.Setup(s => s.GetByUidAsync(Telegram, 7)).ReturnsAsync(new Gym { Uid = 7 });
        var sut = Signed(new GymController(gyms.Object, LinkedTestKit.Router(LinkedTestKit.Linked())));

        Assert.IsType<NoContentResult>(await sut.Delete(7));

        gyms.Verify(s => s.DeleteAsync(Telegram, 7), Times.Once);
        gyms.Verify(s => s.DeleteAsync(Discord, It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Gym_DeleteOfAUidNeitherSideHas_Is404AndDeletesNothing()
    {
        var gyms = new Mock<IGymService>();
        var sut = Signed(new GymController(gyms.Object, LinkedTestKit.Router(LinkedTestKit.Linked())));

        Assert.IsType<NotFoundResult>(await sut.Delete(7));

        gyms.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Lure_Unlinked_PutAndDeleteStayOnTheCaller()
    {
        var lures = new Mock<ILureService>();
        lures.Setup(s => s.GetByUidAsync(Discord, 42)).ReturnsAsync(new Lure { Uid = 42, Distance = 100 });
        lures.Setup(s => s.UpdateAsync(Discord, It.IsAny<Lure>())).ReturnsAsync((string _, Lure l) => l);
        var sut = Signed(new LureController(lures.Object, LinkedTestKit.Router(LinkedTestKit.Unlinked())));

        await sut.Update(42, new LureUpdate { Distance = 250 });
        await sut.Delete(42);

        lures.Verify(s => s.UpdateAsync(Discord, It.IsAny<Lure>()), Times.Once);
        lures.Verify(s => s.DeleteAsync(Discord, 42), Times.Once);
        lures.Verify(s => s.GetByUidAsync(Telegram, It.IsAny<int>()), Times.Never);
    }
}
