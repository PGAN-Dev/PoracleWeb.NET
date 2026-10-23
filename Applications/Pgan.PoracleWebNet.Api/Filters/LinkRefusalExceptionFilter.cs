using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>Answers a refused link with <c>{ error, code }</c>: 409 taken, 502 provision failed, 403 not permitted, 400 otherwise.</summary>
public sealed class LinkRefusalExceptionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is not LinkRefusalException ex)
        {
            return;
        }

        context.Result = new ObjectResult(new { error = ex.Message, code = ex.WireCode })
        {
            StatusCode = StatusFor(ex.Code),
        };
        context.ExceptionHandled = true;
    }

    internal static int StatusFor(LinkRefusalCode code) => code switch
    {
        LinkRefusalCode.Taken => StatusCodes.Status409Conflict,
        LinkRefusalCode.ProvisionFailed => StatusCodes.Status502BadGateway,
        LinkRefusalCode.NotPermitted => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };
}
