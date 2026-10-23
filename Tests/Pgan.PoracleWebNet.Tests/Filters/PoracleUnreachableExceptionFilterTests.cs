using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;

namespace Pgan.PoracleWebNet.Tests.Filters;

public class PoracleUnreachableExceptionFilterTests
{
    private static ExceptionContext BuildContext(Exception ex, bool aborted = false)
    {
        using var cts = new CancellationTokenSource();
        var http = new DefaultHttpContext();
        if (aborted)
        {
            cts.Cancel();
        }

        http.RequestAborted = cts.Token;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = ex };
    }

    [Fact]
    public void HttpRequestExceptionIs503WithAFixedBody()
    {
        var context = BuildContext(new HttpRequestException("connect to http://secret-host:3030 refused"));

        new PoracleUnreachableExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.True(context.ExceptionHandled);
        var error = (string?)result.Value!.GetType().GetProperty("error")!.GetValue(result.Value);
        Assert.Contains("PoracleNG", error);
        Assert.DoesNotContain("secret-host", error);
    }

    [Fact]
    public void ATimeoutOnALiveRequestIs503()
    {
        var context = BuildContext(new TaskCanceledException("timed out"));

        new PoracleUnreachableExceptionFilter().OnException(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void ACancellationBecauseTheCallerLeftIsNotHandled()
    {
        var context = BuildContext(new TaskCanceledException("gone"), aborted: true);

        new PoracleUnreachableExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    [Fact]
    public void OtherExceptionsAreNotHandled()
    {
        var context = BuildContext(new InvalidOperationException("bug"));

        new PoracleUnreachableExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    /// <summary>It is per controller on purpose; the mute controller must carry it for all three actions.</summary>
    [Fact]
    public void MuteControllerCarriesTheFilter() =>
        Assert.Contains(
            typeof(MuteController).GetCustomAttributes<TypeFilterAttribute>(),
            a => a.ImplementationType == typeof(PoracleUnreachableExceptionFilter));

    [Fact]
    public void LocationControllerCarriesTheFilter() =>
        Assert.Contains(
            typeof(LocationController).GetCustomAttributes<TypeFilterAttribute>(),
            a => a.ImplementationType == typeof(PoracleUnreachableExceptionFilter));

    [Fact]
    public void QuestControllerCarriesTheFilter() =>
        Assert.Contains(
            typeof(QuestController).GetCustomAttributes<TypeFilterAttribute>(),
            a => a.ImplementationType == typeof(PoracleUnreachableExceptionFilter));

    [Fact]
    public void AreaControllerCarriesTheFilter() =>
        Assert.Contains(
            typeof(AreaController).GetCustomAttributes<TypeFilterAttribute>(),
            a => a.ImplementationType == typeof(PoracleUnreachableExceptionFilter));

    public static TheoryData<Type> AlarmControllers() =>
    [
        typeof(MonsterController),
        typeof(RaidController),
        typeof(EggController),
        typeof(InvasionController),
        typeof(LureController),
        typeof(NestController),
        typeof(GymController),
        typeof(MaxBattleController),
        typeof(FortChangeController),
        typeof(PokestopEventController)
    ];

    /// <summary>These controllers reach PoracleNG alone, so an HttpRequestException from them names the right upstream.</summary>
    [Theory]
    [MemberData(nameof(AlarmControllers))]
    public void AlarmControllersCarryTheFilter(Type controller) =>
        Assert.Contains(
            controller.GetCustomAttributes<TypeFilterAttribute>(),
            a => a.ImplementationType == typeof(PoracleUnreachableExceptionFilter));
}
