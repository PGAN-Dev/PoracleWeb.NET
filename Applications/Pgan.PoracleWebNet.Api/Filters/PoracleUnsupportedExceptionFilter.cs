using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Maps <see cref="PoracleUnsupportedException"/> from any service into HTTP 409.
/// </summary>
/// <remarks>
/// <para>
/// 409 rather than 403 on purpose. The SPA's 403 interceptor redirects to the dashboard whenever the
/// body carries a <c>disableKey</c>, which is right for an administrator switching a feature off and
/// wrong here: the user asked for something their PoracleNG cannot do, and bouncing them off the page
/// hides which control was at fault. A 409 falls through to the caller's own error handling, which
/// shows the message beside the form.
/// </para>
/// <para>
/// Registered globally in <c>Program.cs</c> alongside <see cref="FeatureDisabledExceptionFilter"/>.
/// The safety net matters for the same reason it does there: quick-pick apply, profile duplicate and
/// profile import all reach the alarm services without passing an action that could pre-check.
/// </para>
/// </remarks>
public sealed class PoracleUnsupportedExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PoracleUnsupportedException ex)
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            error = ex.Message,
            capability = ex.Capability,
            requires = ex.Requires,
        })
        {
            StatusCode = StatusCodes.Status409Conflict,
        };

        context.ExceptionHandled = true;
    }
}
