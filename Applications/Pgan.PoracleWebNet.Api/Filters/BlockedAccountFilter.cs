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
/// See #609.
/// </remarks>
public sealed class BlockedAccountFilter(IHumanService humanService, IMemoryCache cache) : IAsyncActionFilter
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly IHumanService _humanService = humanService;
    private readonly IMemoryCache _cache = cache;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var userId = context.HttpContext.User.FindFirst("userId")?.Value;
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        if (string.IsNullOrEmpty(userId)
            || path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
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
