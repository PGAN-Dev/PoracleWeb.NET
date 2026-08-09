using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Answers 401 when the authenticated account no longer exists.
/// </summary>
/// <remarks>
/// Registered globally, beside the conflict and validation filters. A deleted user's token stayed valid, so
/// every endpoint threw on PoracleNG's 404 and the global handler returned 500 -- and the SPA signs out only
/// on 401, so the session sat there failing on every page. /api/auth/me alone got this right (#545); this
/// makes the rest of the API agree with it. See #584.
/// </remarks>
public sealed class AccountGoneExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not AccountGoneException ex)
        {
            return;
        }

        context.Result = new UnauthorizedObjectResult(new
        {
            error = ex.Message,
        });
        context.ExceptionHandled = true;
    }
}
