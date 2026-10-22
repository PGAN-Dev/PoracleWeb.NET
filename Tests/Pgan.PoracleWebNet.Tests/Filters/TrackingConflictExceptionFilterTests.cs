using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// A refused Add names the rule in its way, so the add dialog can say which one and offer to edit it.
/// </summary>
public class TrackingConflictExceptionFilterTests
{
    private static ActionExecutedContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutedContext(actionContext, [], controller: null!) { Exception = ex };
    }

    private static object? Field(ObjectResult result, string name) =>
        result.Value!.GetType().GetProperty(name)!.GetValue(result.Value);

    [Fact]
    public void ConflictCarriesTheExistingRuleUid()
    {
        var context = BuildContext(new TrackingConflictException("pokemon", "taken") { ExistingUid = 77 });

        new TrackingConflictExceptionFilter().OnActionExecuted(context);

        var result = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.True(context.ExceptionHandled);
        Assert.Equal("taken", Field(result, "error"));
        Assert.Equal("pokemon", Field(result, "trackingType"));
        Assert.Equal(77, Field(result, "existingUid"));
    }

    [Fact]
    public void ConflictWithoutAKnownRuleStillAnswersWithTheMessage()
    {
        // A refusal that names no stored rule (an identical max battle, an event named twice) keeps its
        // message; the uid is simply absent.
        var context = BuildContext(new TrackingConflictException("maxbattle", "You already have an identical max battle alarm."));

        new TrackingConflictExceptionFilter().OnActionExecuted(context);

        var result = Assert.IsType<ConflictObjectResult>(context.Result);
        Assert.Equal("You already have an identical max battle alarm.", Field(result, "error"));
        Assert.Null(Field(result, "existingUid"));
    }

    [Fact]
    public void LeavesOtherExceptionsAlone()
    {
        var context = BuildContext(new InvalidOperationException("boom"));

        new TrackingConflictExceptionFilter().OnActionExecuted(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
