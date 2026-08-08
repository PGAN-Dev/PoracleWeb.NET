using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Turns a refused-because-it-collides write into 409 Conflict.
/// </summary>
/// <remarks>
/// Registered globally so every alarm controller reports a collision the same way. Without it the
/// services' <see cref="TrackingConflictException"/> would surface as an opaque 500 - and before the
/// exception existed at all, the collision was reported as success. See #462 and #463.
/// </remarks>
public sealed class TrackingConflictExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not TrackingConflictException ex)
        {
            return;
        }

        context.Result = new ConflictObjectResult(new
        {
            error = ex.Message,
            trackingType = ex.TrackingType,
        });
        context.ExceptionHandled = true;
    }
}
