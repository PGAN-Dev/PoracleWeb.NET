using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Turns "your PoracleNG is too old for this" into 409 Conflict, naming the feature and what the
/// server would need.
/// </summary>
/// <remarks>
/// <para>
/// Registered globally beside the other exception filters. Without it a
/// <see cref="PoracleUnsupportedException"/> surfaces as a 500, and before the exception existed the
/// user saw whatever PoracleNG said — usually a column name, which tells them nothing they can act on.
/// </para>
/// <para>
/// 409 rather than 403 on purpose. The SPA's 403 branch is the disabled-feature path and keys off
/// <c>disableKey</c>; borrowing it would put an operator's switch and a version shortfall behind the
/// same toast, and the version shortfall has the one detail worth reading in it. 409 has no
/// interceptor branch, so it falls through to the caller, which shows the message beside the control
/// that caused it — the same route <see cref="TrackingConflictExceptionFilter"/> already takes.
/// </para>
/// <para>
/// A filter rather than a controller pre-check because the throw comes from the service layer:
/// quick-pick apply, profile duplicate and profile import all reach the alarm services without
/// passing an action that could have checked first.
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

        context.Result = PoracleUnsupportedResponse.Create(ex.Message, ex.Feature, ex.Requires);
        context.ExceptionHandled = true;
    }
}
