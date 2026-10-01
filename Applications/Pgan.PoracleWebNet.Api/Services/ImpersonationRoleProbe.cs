using System.Collections.Concurrent;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Asks <see cref="IUserRoleResolver"/> about an impersonator on behalf of <see cref="ImpersonationAuthority"/>,
/// within a time budget, and stops asking for a short while once the sources have been found down.
/// </summary>
/// <remarks>
/// <para>
/// The re-authorisation runs on every request an impersonation token makes, and the resolver caches only
/// answers it could resolve. That is right for its other consumers, and it meant that while PoracleNG was
/// unreachable every impersonated request re-asked all three sources and waited each one out. Measured on
/// the test bed with PoracleNG's address black-holed: an impersonated request to a route that touches
/// nothing upstream took minutes, against 0.03 s for an ordinary session, because the config and
/// admin-roles reads each sat out a 100-second HttpClient timeout. A refused connection costs nothing; a
/// dropped one costs the whole budget.
/// </para>
/// <para>
/// Two bounds, for the impersonation check only. A resolve that has not answered within <see cref="Budget"/>
/// is treated as unresolved, which the authority already reads as "keep the session". And once an answer
/// comes back unresolved, or not at all, this impersonator is not re-asked for <see cref="QuietFor"/>:
/// requests in that window keep the session at once. The window closes early the moment a resolve does
/// answer, so revocation lands on the first request after the sources recover, or at most
/// <see cref="QuietFor"/> later. Nothing here is written to the resolver's cache, so its rule that a
/// degraded answer is never cached still holds for every other consumer.
/// </para>
/// <para>
/// The resolve runs in its own DI scope. One that outlives its budget keeps running after the request that
/// started it has moved on, and sharing that request's scoped <c>DbContext</c> would race it. One resolve per
/// impersonator is in flight at a time; concurrent requests wait on the same one.
/// </para>
/// </remarks>
public sealed partial class ImpersonationRoleProbe(
    IServiceScopeFactory scopes,
    ILogger<ImpersonationRoleProbe> logger,
    TimeProvider? timeProvider = null)
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<ImpersonationRoleProbe> _logger = logger;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Task<UserRoles>> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _quietUntil = new(StringComparer.Ordinal);

    /// <summary>
    /// How long a request waits for the resolve. A healthy one takes milliseconds and is then cached for
    /// a minute; two seconds is slack for a slow PoracleNG, not a target.
    /// </summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long an impersonator is not re-asked about after the sources were found down.</summary>
    public TimeSpan QuietFor { get; init; } = TimeSpan.FromSeconds(15);

    private static readonly UserRoles Unresolved = new(false, null, Resolved: false);

    /// <summary>The impersonator's roles, or an unresolved answer when the sources are down or too slow.</summary>
    public async Task<UserRoles> ResolveAsync(string userId)
    {
        if (this._quietUntil.TryGetValue(userId, out var until) && this._time.GetUtcNow() < until)
        {
            return Unresolved;
        }

        var resolve = this._inFlight.GetOrAdd(userId, this.StartResolve);

        using var budget = new CancellationTokenSource();
        var finished = await Task.WhenAny(resolve, Task.Delay(this.Budget, this._time, budget.Token));
        if (finished != resolve)
        {
            LogOverBudget(this._logger, userId, this.Budget.TotalSeconds);
            this.Quiet(userId);
            return Unresolved;
        }

        await budget.CancelAsync();
        var roles = await resolve;
        if (!roles.Resolved)
        {
            this.Quiet(userId);
        }

        return roles;
    }

    private void Quiet(string userId) => this._quietUntil[userId] = this._time.GetUtcNow() + this.QuietFor;

    private Task<UserRoles> StartResolve(string userId) => Task.Run(async () =>
    {
        try
        {
            await using var scope = this._scopes.CreateAsyncScope();
            var roles = await scope.ServiceProvider.GetRequiredService<IUserRoleResolver>().ResolveAsync(userId);

            // The sources answered, so stop keeping sessions on the strength of an outage.
            if (roles.Resolved)
            {
                this._quietUntil.TryRemove(userId, out _);
            }

            return roles;
        }
        catch (Exception ex)
        {
            // The resolver catches its own sources; this is the scope or the container failing, which is
            // no more a "no" than a source that did not answer.
            LogResolveFailed(this._logger, ex, userId);
            return Unresolved;
        }
        finally
        {
            this._inFlight.TryRemove(userId, out _);
        }
    });

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Resolving {ImpersonatedBy}'s roles took longer than {BudgetSeconds}s; keeping their impersonation session and not re-asking for a while.")]
    private static partial void LogOverBudget(ILogger logger, string impersonatedBy, double budgetSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve {ImpersonatedBy}'s roles for the impersonation check.")]
    private static partial void LogResolveFailed(ILogger logger, Exception exception, string impersonatedBy);
}
