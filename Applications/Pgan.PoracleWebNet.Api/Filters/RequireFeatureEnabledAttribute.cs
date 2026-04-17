using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Blocks the action when the corresponding <c>disable_*</c> site setting is true.
/// Closes the gap reported in #236 where alarm types disabled in admin settings were
/// only hidden in the UI but still reachable via direct API calls. Defense-in-depth:
/// service-layer guards in each <c>*Service.CreateAsync</c> / <c>BulkCreateAsync</c>
/// catch service-to-service callers (QuickPick, profile import/duplicate) that bypass
/// the controller filter entirely.
/// Admins are NOT exempt — the toggle means "nobody uses this feature." If an admin
/// needs to use the feature, they flip the toggle off in Admin → Settings.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed partial class RequireFeatureEnabledAttribute : TypeFilterAttribute
{
    public RequireFeatureEnabledAttribute(string disableKey) : base(typeof(RequireFeatureEnabledFilter)) => this.Arguments = [disableKey];

    internal sealed partial class RequireFeatureEnabledFilter(
        string disableKey,
        IFeatureGate gate,
        ILogger<RequireFeatureEnabledFilter> logger) : IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            if (!await gate.IsEnabledAsync(disableKey))
            {
                // Symmetric with the FeatureGate service-layer log so admins tuning toggles see
                // BOTH controller-blocked and service-bypass-blocked attempts in one stream.
                // ASP.NET Core's request logging middleware already logs the path with the response
                // status, so the disable key alone gives admins enough to correlate. Avoids CA1873.
                LogControllerBlocked(logger, disableKey);
                context.Result = FeatureDisabledResponse.Create(disableKey);
                return;
            }

            await next();
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Controller-layer feature gate blocked '{DisableKey}'")]
        private static partial void LogControllerBlocked(ILogger logger, string disableKey);
    }
}
