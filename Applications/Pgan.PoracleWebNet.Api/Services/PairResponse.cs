using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Reports that the other side of a linked pair did not take a write. The warning travels only as the
/// <c>X-Pair-Warning</c> response header, JSON <c>{"side":"telegram","reason":"..."}</c>; a body is never
/// reshaped, so a warned response has exactly the body, and the status, of an unwarned one.
/// </summary>
public static class PairResponse
{
    public const string HeaderName = "X-Pair-Warning";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Sets the header when there is a warning and returns <paramref name="body"/> as it was.</summary>
    public static T Paired<T>(this ControllerBase controller, T body, PairWarning? warning)
    {
        if (warning is not null)
        {
            controller.Response.Headers[HeaderName] = JsonSerializer.Serialize(warning.ToBody(), Options);
        }

        return body;
    }

    /// <summary>204, with the warning header when there is one.</summary>
    public static IActionResult NoContentPaired(this ControllerBase controller, PairWarning? warning)
    {
        controller.Paired(0, warning);
        return controller.NoContent();
    }
}
