using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Reports a refusal from PoracleNG's human, profile, area and location routes as the caller's problem.
/// </summary>
/// <remarks>
/// Registered globally beside <see cref="AlarmValidationExceptionFilter"/>, which does the same job for the
/// tracking routes. Same body shape as every other refusal in this API -- <c>{ "error": "..." }</c> -- so
/// the SPA's interceptor needs nothing new to show it.
/// </remarks>
public sealed class PoracleRequestRefusedExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not PoracleRequestRefusedException ex)
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            error = ex.Message,
        })
        {
            StatusCode = ex.StatusCode,
        };
        context.ExceptionHandled = true;
    }
}
