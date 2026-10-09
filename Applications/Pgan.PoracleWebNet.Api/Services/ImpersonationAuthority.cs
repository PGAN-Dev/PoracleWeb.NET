using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Whether the account that started an impersonation session still has authority over the account it
/// is acting as.
/// </summary>
public interface IImpersonationAuthority
{
    /// <summary>
    /// False when <paramref name="principal"/> is an impersonation session whose <c>impersonatedBy</c> is
    /// no longer an admin and no longer a delegate for the impersonated account. True for every
    /// ordinary session.
    /// </summary>
    Task<bool> StillHoldsAsync(ClaimsPrincipal principal);
}

/// <inheritdoc cref="IImpersonationAuthority"/>
/// <remarks>
/// <para>
/// An impersonation token used to be authorised once, when <c>POST /api/admin/impersonate</c> or
/// <c>POST /api/admin/users/impersonate</c> minted it, and never again. Revoking a delegate's grant stopped
/// them minting another while the one they held kept full read and write access for its 24 hours, and a
/// profile switch re-issued it. The same held for an admin who was demoted.
/// </para>
/// <para>
/// So the question the minting endpoints ask is asked again on every request, from the same source they
/// ask it of: <see cref="IUserRoleResolver"/>. An admin may impersonate anyone; anyone else only a webhook
/// in their resolved <c>ManagedWebhooks</c>. The resolver caches for a minute and grant, revoke and delete
/// invalidate it, so revocation lands on the next request after an admin acts and within a minute of a
/// change made in PoracleJS's own config.
/// </para>
/// <para>
/// <b>An unresolved answer keeps the session.</b> The resolver reports a source it could not read as
/// <c>Resolved: false</c>, and refusing then would end every impersonation session on the site whenever
/// PoracleNG or the <c>poracle_web</c> database blinked -- the shape of #656, which is why degraded answers
/// are never treated as "no". Revocation lands once the sources are readable again: the resolver never
/// caches an unresolved answer, and <see cref="ImpersonationRoleProbe"/> stops re-asking during an outage
/// for at most fifteen seconds, ending that pause the moment a resolve answers.
/// </para>
/// </remarks>
public sealed partial class ImpersonationAuthority(
    IUserRoleResolver roleResolver,
    ILogger<ImpersonationAuthority> logger,
    ImpersonationRoleProbe? probe = null) : IImpersonationAuthority
{
    private readonly IUserRoleResolver _roleResolver = roleResolver;
    private readonly ILogger<ImpersonationAuthority> _logger = logger;

    // Bounds the ask during an outage (see ImpersonationRoleProbe). Registered in the container; a test that
    // constructs this directly asks the resolver it was given.
    private readonly ImpersonationRoleProbe? _probe = probe;

    public async Task<bool> StillHoldsAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var impersonatedBy = principal.FindFirst(ImpersonatedByClaim)?.Value;
        if (impersonatedBy is null)
        {
            return true;
        }

        var target = principal.FindFirst("userId")?.Value;
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(impersonatedBy))
        {
            return false;
        }

        var roles = this._probe is { } probe
            ? await probe.ResolveAsync(impersonatedBy)
            : await this._roleResolver.ResolveAsync(impersonatedBy);
        if (!roles.Resolved)
        {
            LogUnresolved(this._logger, impersonatedBy, target);
            return true;
        }

        if (roles.IsAdmin
            || (roles.ManagedWebhooks ?? []).Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        LogAuthorityGone(this._logger, impersonatedBy, target);
        return false;
    }

    /// <summary>The JwtBearer hook: fails authentication for a session whose authority has gone.</summary>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.Principal?.FindFirst(ImpersonatedByClaim) is null)
        {
            return;
        }

        var authority = context.HttpContext.RequestServices.GetRequiredService<IImpersonationAuthority>();
        if (!await authority.StillHoldsAsync(context.Principal))
        {
            context.Fail("The account that started this impersonation no longer has authority over it.");
        }
    }

    private const string ImpersonatedByClaim = "impersonatedBy";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Refused an impersonation session: {ImpersonatedBy} no longer has authority over {UserId}.")]
    private static partial void LogAuthorityGone(ILogger logger, string impersonatedBy, string userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not resolve {ImpersonatedBy}'s roles; keeping their impersonation of {UserId} until the sources answer.")]
    private static partial void LogUnresolved(ILogger logger, string impersonatedBy, string userId);
}
