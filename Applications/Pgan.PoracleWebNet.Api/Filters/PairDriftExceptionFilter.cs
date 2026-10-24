using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>Answers a resolve that cannot proceed with 409 <c>{ error }</c>: the pair changed again, or a profile still holds alerts.</summary>
public sealed class PairDriftExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not (DriftChangedException or DriftProfileHoldsAlarmsException))
        {
            return;
        }

        context.Result = new ObjectResult(new { error = context.Exception.Message })
        {
            StatusCode = StatusCodes.Status409Conflict,
        };
        context.ExceptionHandled = true;
    }
}
