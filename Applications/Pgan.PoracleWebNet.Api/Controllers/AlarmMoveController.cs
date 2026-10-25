using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>Moves alarms between the Discord and Telegram humans of a linked pair, creating on the target before deleting the source.</summary>
[Route("api/alarm-move")]
[EnableRateLimiting("alarm-move")]
[RequireFeatureEnabled(DisableFeatureKeys.AccountLinking)]
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class AlarmMoveController(IFeatureGate featureGate, ILinkedAccount linked, AlarmMoveDispatcher dispatcher) : BaseApiController
{
    private readonly AlarmMoveDispatcher _dispatcher = dispatcher;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILinkedAccount _linked = linked;

    [HttpPost]
    public async Task<IActionResult> Move([FromBody] AlarmMoveRequest request)
    {
        // A move writes the target human's alarms, so it is never available while someone else's account is being inspected.
        if (this.RefuseMoveWhileImpersonating() is { } refused)
        {
            return refused;
        }

        if (!this._dispatcher.Supports(request.Type))
        {
            return this.BadRequest(new
            {
                error = "Unknown alarm type."
            });
        }

        if (!AccountLinkKeys.TryParse(request.Destination, out var target))
        {
            return this.BadRequest(new
            {
                error = "Destination must be discord or telegram."
            });
        }

        // The type's own gate, so a disabled alarm type can't be reached through here either.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.ByTrackingType[request.Type]);

        var pair = await this._linked.ResolveAsync(this.UserId);
        if (!pair.IsLinked)
        {
            return this.BadRequest(new
            {
                error = "Your accounts aren't linked.",
                code = MoveReasonCodes.NotLinked,
            });
        }

        var results = await this._dispatcher.MoveAsync(request.Type, this.UserId, this.ProfileNo, request.Uids, target, request.Type == "invasion" ? request.Kind : null);
        return this.Ok(new
        {
            results
        });
    }
}
