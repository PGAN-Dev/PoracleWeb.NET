using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// The global filter that answers a PoracleNG refusal on the human, profile, area and location routes.
/// </summary>
public class PoracleRequestRefusedExceptionFilterTests
{
    private static ActionExecutedContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutedContext(actionContext, [], controller: null!) { Exception = ex };
    }

    private static object? Read(object value, string property) =>
        value.GetType().GetProperty(property)?.GetValue(value);

    [Fact]
    public void AnswersFourHundredCarryingPoraclesOwnWording()
    {
        var context = BuildContext(new PoracleRequestRefusedException("state is required (true/false)"));

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.True(context.ExceptionHandled);
        Assert.Equal("state is required (true/false)", Read(result.Value!, "error"));
    }

    [Fact]
    public void KeepsTheStatusTheRefusalCarries()
    {
        // "Profile not found" is a 404 upstream and stays one here. Flattening it to 400 would say the
        // request was malformed when it named something that is simply not there.
        var context = BuildContext(new PoracleRequestRefusedException("Profile not found", 404));

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void UsesTheSameBodyShapeAsEveryOtherRefusal()
    {
        // The SPA's interceptor reads `error`. A different key here would show a blank snackbar.
        var context = BuildContext(new PoracleRequestRefusedException("invalid latitude"));

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(context);

        Assert.NotNull(Read(Assert.IsType<ObjectResult>(context.Result).Value!, "error"));
    }

    [Fact]
    public void LeavesTheAccountGoneFilterAlone()
    {
        // Every filter runs on every request. Answering 400 for a dead account would cost the SPA the 401
        // it signs out on -- the defect #584 closed.
        var context = BuildContext(new AccountGoneException());

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    [Fact]
    public void IgnoresOtherExceptions()
    {
        var context = BuildContext(new InvalidOperationException("unrelated"));

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
