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
/// Every supported PoracleNG (5.4.0 and later) has the mute routes, so there is no capability probe: GET
/// returns the list, and answers 503 when PoracleNG cannot be reached.
/// </para>
/// </remarks>
[Route("api/mutes")]
[EnableRateLimiting("mutes")]
public class MuteController(IPoracleMuteProxy muteProxy) : BaseApiController
{
    private readonly IPoracleMuteProxy _muteProxy = muteProxy;

    /// <summary>
    /// The caller's active quiet periods. Reads stay open during impersonation so an admin can answer
    /// "why am I getting nothing". Answers 503 when PoracleNG is unreachable, so the SPA can say so
    /// rather than show a chip that silently claims nothing is quiet.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMutes(CancellationToken cancellationToken)
    {
        try
        {
            var mutes = await this._muteProxy.ListAsync(this.UserId, cancellationToken);

            return this.Ok(new
            {
                mutes
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return this.StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Quiet periods are unavailable because PoracleNG could not be reached."
            });
        }
    }

    /// <summary>Quiets a subject for a while, or extends an existing quiet period on the same subject.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateMute([FromBody] MuteCreateRequest request, CancellationToken cancellationToken)
    {
        var refusal = this.RefuseWrite();
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
    /// <para>
    /// <paramref name="value"/> must be the value the LIST returned, not the one that was submitted:
    /// upstream canonicalises area names to the geofence's own casing ("aberdeen" is stored as
    /// "Aberdeen") and matches the delete string exactly.
    /// </para>
    /// <para>
    /// <paramref name="trackingType"/> is the same rule for a <c>tracking</c> mute: send back the
    /// <see cref="Mute.TrackingType"/> the list returned, or nothing when it returned none. This app never
    /// creates a tracking mute, but the bot does, and from jfberry/PoracleNG#328 §6 such a mute is
    /// addressed by type and uid together because uids are numbered per table.
    /// </para>
    /// </remarks>
    [HttpDelete]
    public async Task<IActionResult> DeleteMute(
        [FromQuery] string? scope, [FromQuery] string? value, [FromQuery] string? trackingType, CancellationToken cancellationToken)
    {
        var refusal = this.RefuseWrite();
        if (refusal is not null)
        {
            return refusal;
        }

        trackingType = string.IsNullOrWhiteSpace(trackingType) ? null : trackingType;

        if (string.IsNullOrWhiteSpace(scope))
        {
            if (!string.IsNullOrWhiteSpace(value) || trackingType is not null)
            {
                return this.BadRequest(new
                {
                    error = "A value or tracking type requires a scope."
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

        // Upstream refuses a tracking type on any other scope, so say so here rather than forward a 422.
        if (trackingType is not null && scope != MuteScopes.Tracking)
        {
            return this.BadRequest(new
            {
                error = $"A tracking type only applies to scope '{MuteScopes.Tracking}'."
            });
        }

        try
        {
            var deleted = await this._muteProxy.DeleteAsync(
                this.UserId, scope, scope == MuteScopes.Everything ? null : value, trackingType, cancellationToken);

            return this.Ok(new
            {
                deleted
            });
        }
        catch (MuteRejectedException ex)
        {
            return this.UnprocessableEntity(new
            {
                error = ex.Reason
            });
        }
    }

    /// <summary>
    /// Refuses a write that would land on somebody else's account.
    /// </summary>
    /// <remarks>
    /// <see cref="BaseApiController.UserId"/> is the INSPECTED account during impersonation, so an
    /// unguarded write would silence the alerts of the person being looked at -- the shape of #663.
    /// Reads are deliberately left open.
    /// </remarks>
    private IActionResult? RefuseWrite()
    {
        if (this.IsImpersonating)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Quiet periods cannot be changed while viewing another account."
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
