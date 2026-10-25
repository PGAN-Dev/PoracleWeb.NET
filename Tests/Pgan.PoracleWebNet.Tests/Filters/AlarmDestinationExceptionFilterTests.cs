using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Controllers;

namespace Pgan.PoracleWebNet.Tests.Filters;

public class AlarmDestinationExceptionFilterTests : ControllerTestBase
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private static ActionExecutedContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutedContext(actionContext, [], controller: null!) { Exception = ex };
    }

    [Fact]
    public void AnswersWithTheExceptionsStatusAndMessage()
    {
        var context = BuildContext(new AlarmDestinationException(409, "Telegram isn't set up yet."));

        new AlarmDestinationExceptionFilter().OnActionExecuted(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(409, result.StatusCode);
        Assert.True(context.ExceptionHandled);
        Assert.Equal("Telegram isn't set up yet.", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
    }

    [Fact]
    public void LeavesOtherExceptionsAlone()
    {
        var context = BuildContext(new InvalidOperationException("boom"));

        new AlarmDestinationExceptionFilter().OnActionExecuted(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    [Fact]
    public async Task LinkedCreateIntoAPausedSide_CarriesPausedDestinationAndTheSide()
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(Discord)).ReturnsAsync(new LinkedPair(
            Discord,
            new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
            new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.Stopped)));
        var service = new Mock<IRaidService>();
        service.Setup(s => s.CreateAsync(Telegram, It.IsAny<Raid>())).ReturnsAsync(new Raid { Uid = 5, Id = Telegram });
        var sut = new RaidController(service.Object, new LinkedAlarmRouter(linked.Object, Pgan.PoracleWebNet.Tests.Controllers.ControllerTestBase.InertMover(linked.Object)));
        SetupUser(sut, Discord);

        var result = await sut.Create(new RaidCreate { Destination = "telegram" });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var raid = Assert.IsType<Raid>(created.Value);
        Assert.Equal("telegram", raid.Destination);
        Assert.True(raid.PausedDestination);
    }

    [Fact]
    public async Task UnlinkedCreate_StaysUnmarked()
    {
        var service = new Mock<IRaidService>();
        service.Setup(s => s.CreateAsync("123456789", It.IsAny<Raid>())).ReturnsAsync(new Raid { Uid = 5 });
        var sut = new RaidController(service.Object, UnlinkedRouter());
        SetupUser(sut);

        var result = await sut.Create(new RaidCreate { Destination = "telegram" });

        var raid = Assert.IsType<Raid>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Null(raid.Destination);
        Assert.Null(raid.PausedDestination);
    }
}
