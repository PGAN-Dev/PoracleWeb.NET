using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/invasions")]
[RequireFeatureEnabled(DisableFeatureKeys.Invasions)]
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class InvasionController(IInvasionService invasionService, LinkedAlarmRouter router) : BaseApiController
{
    private readonly IInvasionService _invasionService = invasionService;
    private readonly LinkedAlarmRouter _router = router;

    internal const string MoveWithKindChangeMessage =
        "Change where this alarm is delivered or change it between an invasion and a Pokéstop event, not both at once.";

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var invasions = await this._router.ListAsync(this._invasionService, this.UserId, this.ProfileNo);
        return this.Ok(invasions);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var invasion = (await this._router.FindAsync(this._invasionService, this.UserId, uid))?.Model;
        if (invasion == null)
        {
            return this.NotFound();
        }

        return this.Ok(invasion);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InvasionCreate model)
    {
        var invasion = model.ToInvasion();
        if (await this.EventElsewhereAsync(invasion.GruntType) is { } refused)
        {
            return refused;
        }
        // Deliberately not stamped from the JWT claim: writes no longer carry profile_no, so
        // PoracleNG files the alarm under the live current_profile_no. Echoing a possibly-stale
        // claim back would assert a profile the row was never written to. See #411.
        var target = await this._router.CreateTargetAsync(this.UserId, model.Destination);
        var result = await this._invasionService.CreateAsync(target, invasion);
        this._router.StampCreated(result, target);

        // PoracleNG assigns no uid when the submission duplicates an alarm the user already has, so
        // nothing was created. Answering 201 with a Location of /0 advertised a resource that 404s.
        // 200 keeps multi-select creates working while no longer claiming a creation. See #459.
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
    public async Task<IActionResult> Update(int uid, [FromBody] InvasionUpdate model, [FromQuery] string? kind = null)
    {
        // The kind of the rule the person opened. Invasion and event rules are numbered separately, so a uid
        // alone can name either; absent or unknown keeps the first match, as older clients expect. See #1062.
        // With a linked pair the row of that kind is looked for on both humans, so it is found on its owner.
        kind = kind is "event" or "invasion" ? kind : null;
        var found = kind is null
            ? await this._router.FindAsync(this._invasionService, this.UserId, uid)
            : await this._router.FindAsync(this.UserId, humanId => this._invasionService.GetByUidAsync(humanId, uid, kind));
        if (found is null)
        {
            return this.NotFound();
        }

        var (existing, ownerId) = found.Value;

        if (await this._router.MoveTargetAsync(this.UserId, model.Destination, ownerId) is { } moveTo)
        {
            if (this.RefuseMoveWhileImpersonating() is { } impersonationRefused)
            {
                return impersonationRefused;
            }

            // A move and a kind change in one edit would be a create on the target and a delete on the source, each
            // of a different kind; the source delete could then reach an unrelated rule sharing the uid. Refused
            // before anything is written, so the person does one, then the other.
            var openedEvent = PokestopEventTypes.IsEventName(existing.GruntType);
            model.ApplyUpdate(existing);
            if (PokestopEventTypes.IsEventName(existing.GruntType) != openedEvent)
            {
                return this.BadRequest(new
                {
                    error = MoveWithKindChangeMessage,
                });
            }

            if (await this.EventElsewhereAsync(existing.GruntType) is { } movedRefused)
            {
                return movedRefused;
            }

            return this.MovedResponse(await this._router.MoveAsync(this._invasionService, "invasion", this.UserId, existing, ownerId, moveTo));
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

        if (await this.EventElsewhereAsync(existing.GruntType) is { } refused)
        {
            return refused;
        }

        // A kind change stays on the human that owns the rule: the new rule is created there and the old one removed.
        var result = kind is null
            ? await this._invasionService.UpdateAsync(ownerId, existing)
            : await this._invasionService.UpdateAsync(ownerId, existing, kind);
        this._router.Stamp(result, ownerId);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid, [FromQuery] string? kind = null)
    {
        // The kind of the card being deleted. Invasion and event rules are numbered separately, so a uid alone can
        // name either; absent or unknown keeps the first match, as older clients expect. See #1062.
        kind = kind is "event" or "invasion" ? kind : null;
        var found = kind is null
            ? await this._router.FindAsync(this._invasionService, this.UserId, uid)
            : await this._router.FindAsync(this.UserId, humanId => this._invasionService.GetByUidAsync(humanId, uid, kind));
        if (found is null)
        {
            return this.NotFound();
        }

        var ownerId = found.Value.OwnerId;

        if (kind is null)
        {
            await this._invasionService.DeleteAsync(ownerId, uid);
        }
        else
        {
            await this._invasionService.DeleteAsync(ownerId, uid, kind);
        }

        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._router.DeleteAllAsync(this._invasionService, this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var result = await this._router.UpdateDistanceByUidsAsync(this._invasionService, this.UserId, this.ProfileNo, request.Uids, request.Distance);
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

        var result = await this._router.UpdateDistanceAllAsync(this._invasionService, this.UserId, this.ProfileNo, distance);
        return this.DistanceUpdated(result);
    }

    /// <summary>
    /// Refuses a Pokestop event (kecleon, gold-stop, showcase) where the Pokestop Events page owns them.
    /// </summary>
    /// <remarks>
    /// PoracleNG stores events in the invasion table, and this list hides them on a server that has the
    /// events page, so a rule written here answered 201 with a Location that 404s and never appeared on the
    /// page it was added from; beside an existing event rule it answered 200 with uid 0, the guard having
    /// read the filtered list. The SPA offers these names only where there is no events page, so this is
    /// reached by direct calls. Quick picks, import and duplicate write through the service and are
    /// unaffected: their rules are real and are listed on the events page.
    /// </remarks>
    private async Task<IActionResult?> EventElsewhereAsync(string? gruntType) =>
        await this._invasionService.BelongsToPokestopEventsAsync(gruntType)
            ? this.BadRequest(new
            {
                error = $"'{gruntType}' is a Pokestop event, not an invasion. Add it on the Pokestop Events page."
            })
            : null;
}
