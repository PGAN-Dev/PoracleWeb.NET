using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Net.Http.Headers;

namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// What an <c>/api</c> request nothing handled should hear: 405 or 415 when a real route matches its path,
/// the JSON 404 only when none does.
/// </summary>
/// <remarks>
/// <para>
/// The catch-all <c>api/{**path}</c> accepts every method and every content type, so routing keeps it as a
/// candidate exactly when it rejects the real action on method or media type -- and a lone surviving
/// candidate beats the 405/415 endpoints routing would otherwise have produced. A GET on the POST-only
/// refresh route, or the GeoJSON import posted as JSON, then answered "No API endpoint answers", sending
/// the caller looking for a route that exists.
/// </para>
/// <para>
/// So the catch-all asks the question routing skipped: does any real route match this path, and if so, did
/// the method or the content type rule it out? Constraints count, because routing drops a constrained
/// parameter that cannot take a literal before it looks at the method, so a path whose parameter fails
/// its constraint (<c>/api/raids/abc</c>) matched no action and keeps the 404. Only reached for requests
/// nothing else handled, so rebuilding the matchers per request costs nothing on the paths that matter.
/// </para>
/// </remarks>
public static class ApiFallback
{
    private static readonly string[] AnyMethod = [];

    /// <summary>Answers a request no /api action took.</summary>
    public static IResult Handle(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sources = context.RequestServices.GetService<EndpointDataSource>();
        var candidates = sources is null ? [] : MatchingRoutes(context, sources);

        if (candidates.Count > 0)
        {
            var method = context.Request.Method;
            var sameMethod = candidates.Where(c => Accepts(c.Methods, method)).ToList();

            if (sameMethod.Count == 0)
            {
                var allow = candidates.SelectMany(c => c.Methods).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                context.Response.Headers.Allow = string.Join(", ", allow);
                return Results.Json(
                    new { error = $"{method} is not allowed on {context.Request.Path}. It accepts {string.Join(", ", allow)}." },
                    statusCode: StatusCodes.Status405MethodNotAllowed);
            }

            if (!sameMethod.Any(c => AcceptsContentType(c.ContentTypes, c.ContentTypeOptional, context.Request.ContentType)))
            {
                var accepted = sameMethod.SelectMany(c => c.ContentTypes).Distinct(StringComparer.OrdinalIgnoreCase);
                return Results.Json(
                    new { error = $"{method} {context.Request.Path} takes {string.Join(" or ", accepted)}, not {context.Request.ContentType ?? "no content type"}." },
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }
        }

        return Results.NotFound(new
        {
            error = $"No API endpoint answers {context.Request.Method} {context.Request.Path}.",
        });
    }

    private sealed record Route(IReadOnlyList<string> Methods, IReadOnlyList<string> ContentTypes, bool ContentTypeOptional);

    private static List<Route> MatchingRoutes(HttpContext context, EndpointDataSource sources)
    {
        var matches = new List<Route>();
        var policies = context.RequestServices.GetService<ParameterPolicyFactory>();

        foreach (var endpoint in sources.Endpoints.OfType<RouteEndpoint>())
        {
            // Fallbacks (this one and the SPA's) carry int.MaxValue; they are what is being explained.
            if (endpoint.Order == int.MaxValue)
            {
                continue;
            }

            var values = new RouteValueDictionary();
            var matcher = new TemplateMatcher(new RouteTemplate(endpoint.RoutePattern), new RouteValueDictionary());
            if (!matcher.TryMatch(context.Request.Path, values)
                || !SatisfiesConstraints(context, endpoint, policies, values))
            {
                continue;
            }

            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            matches.Add(new Route(
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? AnyMethod,
                accepts?.ContentTypes ?? [],
                accepts is null || accepts.IsOptional));
        }

        return matches;
    }

    /// <summary>
    /// Whether the captured values pass the route's constraints. Routing rules a literal out of a
    /// constrained parameter before it looks at the method (<c>GET /api/eggs/distance</c> never considers
    /// <c>{uid:int}</c>), so this has to as well, or the PUT-only distance route would read as a GET route.
    /// </summary>
    private static bool SatisfiesConstraints(
        HttpContext context, RouteEndpoint endpoint, ParameterPolicyFactory? policies, RouteValueDictionary values)
    {
        if (policies is null)
        {
            return true;
        }

        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            foreach (var reference in parameter.ParameterPolicies)
            {
                if (policies.Create(parameter, reference) is IRouteConstraint constraint
                    && !constraint.Match(context, null, parameter.Name, values, RouteDirection.IncomingRequest))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Accepts(IReadOnlyList<string> methods, string method) =>
        methods.Count == 0 || methods.Contains(method, StringComparer.OrdinalIgnoreCase);

    private static bool AcceptsContentType(IReadOnlyList<string> accepted, bool optional, string? requestType)
    {
        if (accepted.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(requestType))
        {
            return optional;
        }

        if (!MediaTypeHeaderValue.TryParse(requestType, out var sent))
        {
            return false;
        }

        return accepted.Any(a => MediaTypeHeaderValue.TryParse(a, out var type) && sent.IsSubsetOf(type));
    }
}
