using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Maps a failed call to PoracleNG (<see cref="HttpRequestException"/>, or a
/// <see cref="TaskCanceledException"/> that is a timeout rather than the caller leaving) into a 503 with
/// a fixed body. The body carries no upstream URL, secret or <c>ex.Message</c>.
/// </summary>
/// <remarks>
/// Applied per controller with <c>[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]</c>, never
/// globally: <see cref="HttpRequestException"/> also comes from Koji, Discord and Golbat, and naming
/// PoracleNG for those would be wrong.
/// </remarks>
public sealed class PoracleUnreachableExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var handled = context.Exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => !context.HttpContext.RequestAborted.IsCancellationRequested,
            _ => false
        };

        if (!handled)
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            error = "PoracleNG could not be reached. Try again shortly."
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
        context.ExceptionHandled = true;
    }
}
