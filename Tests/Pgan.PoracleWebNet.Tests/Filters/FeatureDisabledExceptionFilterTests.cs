using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// Verifies the global exception filter that maps <see cref="FeatureDisabledException"/> to HTTP 403.
/// This is the safety net for service-to-service callers (QuickPick, profile import/duplicate)
/// that bypass the controller-level <c>RequireFeatureEnabledAttribute</c>. See #236.
/// </summary>
public class FeatureDisabledExceptionFilterTests
{
    private static ExceptionContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = ex };
    }

    [Fact]
    public void MapsFeatureDisabledExceptionTo403WithDisableKey()
    {
        var context = BuildContext(new FeatureDisabledException("disable_mons"));
        var sut = new FeatureDisabledExceptionFilter();

        sut.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.True(context.ExceptionHandled);
        // The frontend interceptor keys off `disableKey` to distinguish "feature disabled"
        // from a generic permission denial — flipping that contract breaks the redirect-to-dashboard UX.
        Assert.NotNull(result.Value);
        var disableKeyProp = result.Value.GetType().GetProperty("disableKey");
        Assert.NotNull(disableKeyProp);
        Assert.Equal("disable_mons", disableKeyProp.GetValue(result.Value));
    }

    [Fact]
    public void IgnoresOtherExceptions()
    {
        var context = BuildContext(new InvalidOperationException("unrelated"));
        var sut = new FeatureDisabledExceptionFilter();

        sut.OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
