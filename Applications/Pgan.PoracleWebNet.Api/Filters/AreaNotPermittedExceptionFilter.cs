using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Answers an Areas save that named fences the caller may not subscribe to with 400 and the names,
/// so the SPA can say which ones.
/// </summary>
public sealed class AreaNotPermittedExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not AreaNotPermittedException ex)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(new
        {
            error = ex.Message,
            areas = ex.Names,
        });
        context.ExceptionHandled = true;
    }
}
