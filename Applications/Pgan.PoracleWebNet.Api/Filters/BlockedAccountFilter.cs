using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Filters;

/// <summary>
/// Rejects requests from an account an admin has blocked.
/// </summary>
/// <remarks>
/// #597 made <c>/api/auth/me</c> answer 401 so the SPA signs a blocked user out. That is a client-side
/// courtesy, not enforcement: the API kept serving every other endpoint until the SPA happened to poll, and
/// anything not going through the SPA was unaffected entirely. Blocking has to mean the API refuses.
/// <para>
/// The lookup is cached for a minute per user, so this costs one PoracleNG call per user per minute rather
/// than one per request. A minute of stale access after a block is the trade; without the cache this would
/// add a round trip to every single request. Auth endpoints are exempt so signing out and reading
/// <c>/api/auth/me</c> still work — that is how the SPA learns it has been blocked.
/// </para>
/// <para>
/// Under impersonation the block checked is the impersonator's, not the target's. The <c>userId</c> claim
/// then names the destination being managed, and refusing on its block meant an admin or delegate could
/// not open a blocked destination to unblock or clean it up, while a blocked impersonator was never checked
/// at all. This matches <c>/api/auth/me</c>, which already exempts an impersonated disabled account (#706).
/// Under impersonation the <c>/api/auth</c> exemption narrows to reads and signing out, so a blocked
/// impersonator cannot pause or resume the destination's alerts.
/// </para>
/// See #609 and #1038.
/// </remarks>
public sealed class BlockedAccountFilter(IHumanService humanService, IMemoryCache cache) : IAsyncActionFilter
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The writes under <c>/api/auth</c> an impersonation session may still send while its impersonator is
    /// blocked: signing out and looking after the impersonator's own refresh token. None acts on the
    /// destination. Going back to the admin's own token happens in the SPA, with no request.
    /// </summary>
    private static readonly HashSet<string> SessionWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/logout",
        "/api/auth/oidc/refresh",
        "/api/auth/oidc/refresh/revoke",
    };

    private readonly IHumanService _humanService = humanService;
    private readonly IMemoryCache _cache = cache;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var user = context.HttpContext.User;
        var impersonatedBy = user.FindFirst("impersonatedBy")?.Value;
        var userId = string.IsNullOrEmpty(impersonatedBy) ? user.FindFirst("userId")?.Value : impersonatedBy;
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        if (string.IsNullOrEmpty(userId)
            || IsExemptAuthRequest(path, context.HttpContext.Request.Method, impersonating: !string.IsNullOrEmpty(impersonatedBy)))
        {
            await next();
            return;
        }

        if (await this.IsBlockedAsync(userId))
        {
            context.Result = new ObjectResult(new
            {
                error = "This account has been blocked by an administrator.",
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

            return;
        }

        await next();
    }

    /// <remarks>
    /// An ordinary session keeps the whole <c>/api/auth</c> exemption: that is how a blocked member signs out
    /// and how <c>/api/auth/me</c> tells the SPA it has been blocked. An impersonation session keeps only the
    /// reads and <see cref="SessionWrites"/>, because <c>POST /api/auth/alerts/toggle</c> pauses or resumes the
    /// destination being managed, and a blocked impersonator must not change it (#1038).
    /// </remarks>
    private static bool IsExemptAuthRequest(string path, string method, bool impersonating)
    {
        if (!path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!impersonating)
        {
            return true;
        }

        return HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || SessionWrites.Contains(path.TrimEnd('/'));
    }

    private async Task<bool> IsBlockedAsync(string userId)
    {
        var key = $"blocked:{userId}";
        if (this._cache.TryGetValue(key, out bool blocked))
        {
            return blocked;
        }

        try
        {
            var human = await this._humanService.GetByIdAsync(userId);
            blocked = human?.AdminDisable == 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Never lock everyone out because PoracleNG blinked. A deleted account is handled elsewhere.
            return false;
        }

        this._cache.Set(key, blocked, CacheFor);
        return blocked;
    }
}
