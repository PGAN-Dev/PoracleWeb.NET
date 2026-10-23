using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Pokestop-event alarms: Showcase, Kecleon and Gold Stop.
/// </summary>
/// <remarks>
/// The route says what the page says. Upstream's <c>incident</c> and <c>display_type</c> stay behind
/// <c>IPoracleIncidentProxy</c> — a user has no idea what an incident is.
/// </remarks>
[Route("api/pokestop-events")]
[RequireFeatureEnabled(DisableFeatureKeys.PokestopEvents)]
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class PokestopEventController(IPokestopEventService pokestopEventService, LinkedAlarmRouter router) : BaseApiController
{
    private readonly IPokestopEventService _pokestopEventService = pokestopEventService;
    private readonly LinkedAlarmRouter _router = router;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await this._router.ListAsync(this._pokestopEventService, this.UserId, this.ProfileNo);
        return this.Ok(items);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var item = (await this._router.FindAsync(this._pokestopEventService, this.UserId, uid))?.Model;
        if (item == null)
        {
            return this.NotFound();
        }

        return this.Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PokestopEventCreate model)
    {
        var target = await this._router.CreateTargetAsync(this.UserId, model.Destination);
        var result = await this._pokestopEventService.CreateAsync(target, model.ToPokestopEvent());
        this._router.StampCreated(result, target);

        // PoracleNG names no uid when the submission matched a rule that was already there, so nothing
        // was created and a 201 pointing at /0 would advertise a resource that 404s. See #459.
        if (result.Uid <= 0)
        {
            return this.Ok(result);
        }

        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] PokestopEventUpdate model)
    {
        var found = await this._router.FindAsync(this._pokestopEventService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var (existing, ownerId) = found.Value;

        if (await this._router.MoveTargetAsync(this.UserId, model.Destination, ownerId) is { } moveTo)
        {
            model.ApplyUpdate(existing);
            return this.MovedResponse(await this._router.MoveAsync(this._pokestopEventService, "incident", this.UserId, existing, ownerId, moveTo));
        }

        // Nothing to write means nothing to send: see LeavesAlarmUnchanged.
        if (LeavesAlarmUnchanged(existing, () =>
        {
            model.ApplyUpdate(existing);
            return existing;
        }))
        {
            return this.Ok(existing);
        }

        var result = await this._pokestopEventService.UpdateAsync(ownerId, existing);
        this._router.Stamp(result, ownerId);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var found = await this._router.FindAsync(this._pokestopEventService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var ownerId = found.Value.OwnerId;

        await this._pokestopEventService.DeleteAsync(ownerId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._router.DeleteAllAsync(this._pokestopEventService, this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var result = await this._router.UpdateDistanceByUidsAsync(this._pokestopEventService, this.UserId, this.ProfileNo, request.Uids, request.Distance);
        return this.DistanceUpdated(result);
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var invalid = this.RejectInvalidDistance(distance);
        if (invalid != null)
        {
            return invalid;
        }

        var result = await this._router.UpdateDistanceAllAsync(this._pokestopEventService, this.UserId, this.ProfileNo, distance);
        return this.DistanceUpdated(result);
    }
}
