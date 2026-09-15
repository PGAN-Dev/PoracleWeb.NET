using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Turns a write against a rule that is not there into 404 Not Found.
/// </summary>
/// <remarks>
/// Registered globally alongside <see cref="TrackingConflictExceptionFilter"/> and
/// <see cref="AlarmValidationExceptionFilter"/>. Only the uid-addressed <c>/api/v2</c> PUT can report this
/// at all, and it means the row went away between the controller reading it and the write landing — a
/// second tab, the bot, or the active-hours scheduler. 404 tells the SPA to reload the list; a 500 would
/// tell the user the server broke.
/// </remarks>
public sealed class TrackingRuleNotFoundExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not TrackingRuleNotFoundException ex)
        {
            return;
        }

        context.Result = new NotFoundObjectResult(new
        {
            error = ex.Message,
            trackingType = ex.TrackingType,
        });
        context.ExceptionHandled = true;
    }
}
