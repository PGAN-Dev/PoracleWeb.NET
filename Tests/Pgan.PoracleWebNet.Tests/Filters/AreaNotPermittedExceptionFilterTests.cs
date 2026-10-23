using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

public class AreaNotPermittedExceptionFilterTests
{
    private static ActionExecutedContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutedContext(actionContext, [], controller: null!) { Exception = ex };
    }

    [Fact]
    public void AnswersFourHundredNamingTheAreas()
    {
        var context = BuildContext(new AreaNotPermittedException(["other fence", "secret"]));

        new AreaNotPermittedExceptionFilter().OnActionExecuted(context);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.True(context.ExceptionHandled);
        var areas = (IReadOnlyList<string>)result.Value!.GetType().GetProperty("areas")!.GetValue(result.Value)!;
        Assert.Equal(["other fence", "secret"], areas);
        Assert.NotNull(result.Value.GetType().GetProperty("error")!.GetValue(result.Value));
    }

    [Fact]
    public void LeavesOtherExceptionsAlone()
    {
        var context = BuildContext(new InvalidOperationException("boom"));

        new AreaNotPermittedExceptionFilter().OnActionExecuted(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
