using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Single source of truth for the HTTP 403 body returned when a <c>disable_*</c> site setting blocks
/// an action. Used by both <see cref="RequireFeatureEnabledAttribute"/> (controller-level pre-check)
/// and <see cref="FeatureDisabledExceptionFilter"/> (service-layer exception map). Keeping the wire
/// contract in one place means a future tweak to the message or the field name can't drift between
/// the two paths — important because the SPA's 403 interceptor keys off the <c>disableKey</c>
/// property to distinguish "feature disabled" from generic permission denials.
/// </summary>
internal static class FeatureDisabledResponse
{
    public const string Message = "This feature is disabled by the administrator.";

    public static ObjectResult Create(string disableKey) => new(new
    {
        error = Message,
        disableKey
    })
    {
        StatusCode = StatusCodes.Status403Forbidden
    };
}
