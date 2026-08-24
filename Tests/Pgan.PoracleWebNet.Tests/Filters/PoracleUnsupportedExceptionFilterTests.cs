using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// Verifies the global exception filter that maps <see cref="PoracleUnsupportedException"/> to HTTP
/// 409. Like <see cref="FeatureDisabledExceptionFilterTests"/>, this is the safety net for
/// service-to-service callers (quick-pick apply, profile import and duplicate) that never pass a
/// controller action that could have pre-checked.
/// </summary>
public class PoracleUnsupportedExceptionFilterTests
{
    private static ExceptionContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = ex };
    }

    private static object? Read(object value, string property) =>
        value.GetType().GetProperty(property)?.GetValue(value);

    [Fact]
    public void MapsUnsupportedExceptionTo409NamingTheFeatureAndWhatItNeeds()
    {
        var context = BuildContext(new PoracleUnsupportedException("costume filters", "PoracleNG database migration 6"));
        var sut = new PoracleUnsupportedExceptionFilter();

        sut.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.True(context.ExceptionHandled);
        Assert.NotNull(result.Value);
        Assert.Equal("costume filters", Read(result.Value, "feature"));
        Assert.Equal("PoracleNG database migration 6", Read(result.Value, "requires"));
    }

    [Fact]
    public void ErrorTextSaysWhichFeatureAndWhichVersion()
    {
        // The whole point of the surface: the reader learns what they asked for and what would serve it,
        // instead of PoracleNG's own wording, which names a column.
        var context = BuildContext(new PoracleUnsupportedException("pokecoin quest rewards", "PoracleNG 5.2.1 or newer"));

        new PoracleUnsupportedExceptionFilter().OnException(context);

        var error = Read(Assert.IsType<ObjectResult>(context.Result).Value!, "error") as string;
        Assert.NotNull(error);
        Assert.Contains("pokecoin quest rewards", error);
        Assert.Contains("5.2.1", error);
    }

    [Fact]
    public void DoesNotBorrowTheDisabledFeatureContract()
    {
        // 403-with-disableKey is the administrator-switched-it-off path and the SPA's 403 branch keys off
        // it. An old server is not a disabled feature, and answering with that shape would put the two
        // behind the same toast -- losing the one detail here worth reading.
        var context = BuildContext(new PoracleUnsupportedException("mutes", "PoracleNG 5.2.1 or newer"));

        new PoracleUnsupportedExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.NotEqual(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Null(Read(result.Value!, "disableKey"));
    }

    [Fact]
    public void IgnoresOtherExceptions()
    {
        var context = BuildContext(new InvalidOperationException("unrelated"));
        var sut = new PoracleUnsupportedExceptionFilter();

        sut.OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    [Fact]
    public void LeavesTheDisabledFeatureFilterAlone()
    {
        // Both filters run on every request. Each must decline the other's exception, or whichever is
        // registered first answers for both.
        var context = BuildContext(new FeatureDisabledException("disable_mons"));

        new PoracleUnsupportedExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
