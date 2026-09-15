using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Quiet periods: time-boxed suppression of one gym, one area, one species or one station, over
/// PoracleNG's v2 mute store.
/// </summary>
/// <remarks>
/// <para>
/// This is not the user menu's Pause Alerts. That toggles the human's <c>enabled</c> flag -- account
/// wide, indefinite, persisted. A quiet period is one subject, for a while, and PoracleNG holds it in
/// memory: a processor restart clears every one of them.
/// </para>
/// <para>
/// GET returns capability and the list together, deliberately unlike
/// <see cref="SummaryScheduleController"/>'s separate <c>/capability</c> endpoint. The quiet chip is
/// read on every alarm page, so folding the two halves the roundtrips; the schedule surface is one page
/// and could afford the extra call.
/// </para>
/// </remarks>
[Route("api/mutes")]
[EnableRateLimiting("mutes")]
public class MuteController(
    IPoracleMuteProxy muteProxy,
    IMuteCapabilityService capability) : BaseApiController
{
    private readonly IPoracleMuteProxy _muteProxy = muteProxy;
    private readonly IMuteCapabilityService _capability = capability;

    /// <summary>
    /// Whether this server can do quiet periods at all, plus the caller's active ones. Reads stay open
    /// during impersonation so an admin can answer "why am I getting nothing".
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMutes(CancellationToken cancellationToken)
    {
        var capable = await this._capability.IsMuteApiAvailableAsync(cancellationToken);
        if (!capable)
        {
            return this.Ok(new
            {
                capable = false,
                mutes = Array.Empty<Mute>()
            });
        }

        var mutes = await this._muteProxy.ListAsync(this.UserId, cancellationToken);

        return this.Ok(new
        {
            capable = true,
            mutes
        });
    }

    /// <summary>Quiets a subject for a while, or extends an existing quiet period on the same subject.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateMute([FromBody] MuteCreateRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseWriteAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var scope = request.Scope ?? string.Empty;
        if (!MuteScopes.WritableFromWeb.Contains(scope))
        {
            return this.BadRequest(new
            {
                error = $"Invalid mute scope: {scope}"
            });
        }

        // Every writable scope names a subject, so a blank value is always a bug on the way in. The
        // valueless scope upstream has -- 'everything' -- is not one PoracleWeb.NET creates.
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return this.BadRequest(new
            {
                error = $"A value is required for scope '{scope}'."
            });
        }

        var duration = request.DurationMinutes ?? MuteScopes.DefaultDurationMinutes;
        if (duration < 1 || duration > MuteScopes.MaxDurationMinutes)
        {
            return this.BadRequest(new
            {
                error = $"Duration must be between 1 and {MuteScopes.MaxDurationMinutes} minutes."
            });
        }

        try
        {
            var (mute, replaced) = await this._muteProxy.CreateAsync(
                this.UserId, scope, request.Value.Trim(), duration, cancellationToken);

            return this.Ok(new
            {
                mute,
                replaced
            });
        }
        catch (MuteRejectedException ex)
        {
            // Upstream's own sentence -- "unknown area: Foo" is the case that reaches a user, and it is
            // more use than anything this layer could invent.
            return this.UnprocessableEntity(new
            {
                error = ex.Reason
            });
        }
    }

    /// <summary>
    /// Lifts one quiet period, or every one of them when no scope is given.
    /// </summary>
    /// <remarks>
    /// <paramref name="value"/> must be the value the LIST returned, not the one that was submitted:
    /// upstream canonicalises area names to the geofence's own casing ("aberdeen" is stored as
    /// "Aberdeen") and matches the delete string exactly.
    /// </remarks>
    [HttpDelete]
    public async Task<IActionResult> DeleteMute(
        [FromQuery] string? scope, [FromQuery] string? value, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseWriteAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return this.BadRequest(new
                {
                    error = "A value requires a scope."
                });
            }

            var removedAll = await this._muteProxy.DeleteAllAsync(this.UserId, cancellationToken);

            return this.Ok(new
            {
                deleted = removedAll
            });
        }

        // Deletes accept every scope the server can hold, not just the four this app creates: a mute
        // set from the Discord bot must be liftable here, or the list shows something the user cannot act on.
        if (!MuteScopes.All.Contains(scope))
        {
            return this.BadRequest(new
            {
                error = $"Invalid mute scope: {scope}"
            });
        }

        if (scope != MuteScopes.Everything && string.IsNullOrWhiteSpace(value))
        {
            return this.BadRequest(new
            {
                error = $"A value is required for scope '{scope}'."
            });
        }

        var deleted = await this._muteProxy.DeleteAsync(
            this.UserId, scope, scope == MuteScopes.Everything ? null : value, cancellationToken);

        return this.Ok(new
        {
            deleted
        });
    }

    /// <summary>
    /// Refuses a write the caller must not make: one the server cannot honour, and one that would land
    /// on somebody else's account.
    /// </summary>
    /// <remarks>
    /// <see cref="BaseApiController.UserId"/> is the INSPECTED account during impersonation, so an
    /// unguarded write would silence the alerts of the person being looked at -- the shape of #663.
    /// Reads are deliberately left open.
    /// </remarks>
    private async Task<IActionResult?> RefuseWriteAsync(CancellationToken cancellationToken)
    {
        if (this.IsImpersonating)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Quiet periods cannot be changed while viewing another account."
            });
        }

        if (!await this._capability.IsMuteApiAvailableAsync(cancellationToken))
        {
            return this.StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = "This Poracle server does not support quiet periods.",
                capable = false
            });
        }

        return null;
    }
}

/// <summary>Body of a create-mute request.</summary>
public class MuteCreateRequest
{
    /// <summary>One of <see cref="MuteScopes.WritableFromWeb"/>.</summary>
    [Required]
    [StringLength(32)]
    public string? Scope
    {
        get; set;
    }

    /// <summary>Gym/station id, dex id as a numeric string, or area name.</summary>
    [Required]
    [StringLength(256)]
    public string? Value
    {
        get; set;
    }

    /// <summary>Minutes. Defaults to PoracleNG's own 60 when omitted.</summary>
    [Range(1, MuteScopes.MaxDurationMinutes)]
    public int? DurationMinutes
    {
        get; set;
    }
}
