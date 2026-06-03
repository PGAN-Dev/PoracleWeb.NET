using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Maps <see cref="SummaryBackendUnavailableException"/> thrown from <c>PoracleSummaryProxy</c> into a
/// generic HTTP 503. The response body carries no upstream URL, no <c>X-Poracle-Secret</c>, no
/// <c>ex.Message</c>, and no stack trace — the SPA treats this as a transient "try again later" banner,
/// NOT as "feature off" (the config-flag capability boolean is the only "feature off" source).
/// Registered globally in <c>Program.cs</c> next to <c>FeatureDisabledExceptionFilter</c>.
/// </summary>
public sealed class SummaryBackendUnavailableExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not SummaryBackendUnavailableException)
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            error = "Quest summary service unavailable."
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
        context.ExceptionHandled = true;
    }
}
