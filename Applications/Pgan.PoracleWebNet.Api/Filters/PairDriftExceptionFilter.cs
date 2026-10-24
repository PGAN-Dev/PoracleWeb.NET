using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Answers a resolve that cannot proceed with 409 <c>{ error, code, side, name, count }</c>: the pair changed again,
/// a profile or place still holds alarms, the profile to switch to is missing, or the resolve stopped partway.
/// <c>code</c> is what the SPA translates; <c>error</c> is the English fallback.
/// </summary>
public sealed class PairDriftExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not DriftRefusalException refusal)
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            error = refusal.Message,
            code = refusal.Code,
            side = refusal.Side is { } side ? AccountLinkKeys.Wire(side) : null,
            name = refusal.Name,
            count = refusal.Count,
        })
        {
            StatusCode = StatusCodes.Status409Conflict,
        };
        context.ExceptionHandled = true;
    }
}
