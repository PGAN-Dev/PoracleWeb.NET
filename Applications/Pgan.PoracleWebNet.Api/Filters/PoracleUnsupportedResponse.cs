using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Single source of truth for the HTTP 409 body returned when the PoracleNG on the other end is too
/// old to serve a request.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="FeatureDisabledResponse"/>: the body is a contract the SPA reads, and
/// more than one path can produce it — the global <see cref="PoracleUnsupportedExceptionFilter"/>
/// today, a controller-level pre-check the first time one is worth having. Keeping the shape in one
/// place is what stops the two answering the same failure differently.
/// </remarks>
internal static class PoracleUnsupportedResponse
{
    public static ObjectResult Create(string message, string feature, string requires) => new(new
    {
        error = message,
        feature,
        requires
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}
