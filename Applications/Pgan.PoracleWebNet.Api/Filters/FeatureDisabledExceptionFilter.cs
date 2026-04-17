using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Maps <see cref="FeatureDisabledException"/> thrown from any service into HTTP 403.
/// This is the safety net for code paths that bypass the controller-level
/// <c>RequireFeatureEnabledAttribute</c> — e.g. <c>QuickPickController.Apply</c> calling
/// <c>MonsterService.CreateAsync</c> directly, or <c>ProfileOverviewController.ImportProfile</c>
/// fanning out across all alarm services. Registered globally in <c>Program.cs</c>.
/// </summary>
public sealed class FeatureDisabledExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not FeatureDisabledException ex)
        {
            return;
        }

        context.Result = FeatureDisabledResponse.Create(ex.DisableKey);
        context.ExceptionHandled = true;
    }
}
