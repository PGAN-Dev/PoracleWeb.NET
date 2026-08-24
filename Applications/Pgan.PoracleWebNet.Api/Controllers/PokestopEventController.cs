using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

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
public class PokestopEventController(IPokestopEventService pokestopEventService) : BaseApiController
{
    private readonly IPokestopEventService _pokestopEventService = pokestopEventService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await this._pokestopEventService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(items);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var item = await this._pokestopEventService.GetByUidAsync(this.UserId, uid);
        if (item == null)
        {
            return this.NotFound();
        }

        return this.Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PokestopEventCreate model)
    {
        var result = await this._pokestopEventService.CreateAsync(this.UserId, model.ToPokestopEvent());

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
        var existing = await this._pokestopEventService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
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

        var result = await this._pokestopEventService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._pokestopEventService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._pokestopEventService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._pokestopEventService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._pokestopEventService.UpdateDistanceByUidsAsync(
            request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var invalid = this.RejectInvalidDistance(distance);
        if (invalid != null)
        {
            return invalid;
        }

        var count = await this._pokestopEventService.UpdateDistanceByUserAsync(
            this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
