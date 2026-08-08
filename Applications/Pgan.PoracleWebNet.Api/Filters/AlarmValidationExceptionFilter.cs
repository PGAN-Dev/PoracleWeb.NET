using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Turns a service-layer rejection of a bad request into 400 Bad Request.
/// </summary>
/// <remarks>
/// Registered globally, next to <see cref="TrackingConflictExceptionFilter"/>, so a guard that lives in the
/// service rather than in a DataAnnotation still reports the same way. Without it the update paths answered
/// 500 for exactly the request the create paths described in a 400. See #518.
/// </remarks>
public sealed class AlarmValidationExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not AlarmValidationException ex)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(new
        {
            error = ex.Message,
        });
        context.ExceptionHandled = true;
    }
}
