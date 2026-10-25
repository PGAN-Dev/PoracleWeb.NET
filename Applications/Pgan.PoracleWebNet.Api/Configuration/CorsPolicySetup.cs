using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>The site's one CORS policy, kept apart from Program.cs so a test can evaluate it.</summary>
public static class CorsPolicySetup
{
    /// <summary>
    /// Headers the SPA reads off a response. A browser hides every response header not listed here from a
    /// cross-origin caller (an <c>allowedOrigins</c> deployment, or <c>ng serve</c>), so an unlisted
    /// <c>X-Move-Status</c> read as absent and a move that left a copy behind looked like a clean move.
    /// <c>X-Pair-Warning</c> is the header person-level writes use to say the linked pair drifted.
    /// </summary>
    public static readonly string[] ExposedHeaders = ["X-Move-Status", "X-Pair-Warning"];

    public static void Apply(CorsPolicyBuilder policy, string[]? allowedOrigins)
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            // Development only — never runs in production due to startup check
            policy.SetIsOriginAllowed(_ => true);
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders(ExposedHeaders);
    }
}
