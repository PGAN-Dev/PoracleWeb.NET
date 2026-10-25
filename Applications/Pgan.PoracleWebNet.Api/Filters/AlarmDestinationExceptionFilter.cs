using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>Answers a create aimed at a side that cannot take it with the exception's status and <c>{ error }</c>.</summary>
public sealed class AlarmDestinationExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not AlarmDestinationException ex)
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
