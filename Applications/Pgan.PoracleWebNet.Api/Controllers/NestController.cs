using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/nests")]
[RequireFeatureEnabled(DisableFeatureKeys.Nests)]
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class NestController(INestService nestService, LinkedAlarmRouter router) : BaseApiController
{
    private readonly INestService _nestService = nestService;
    private readonly LinkedAlarmRouter _router = router;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var nests = await this._router.ListAsync(this._nestService, this.UserId, this.ProfileNo);
        return this.Ok(nests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var nest = (await this._router.FindAsync(this._nestService, this.UserId, uid))?.Model;
        if (nest == null)
        {
            return this.NotFound();
        }

        return this.Ok(nest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NestCreate model)
    {
        var nest = model.ToNest();
        // Deliberately not stamped from the JWT claim: writes no longer carry profile_no, so
        // PoracleNG files the alarm under the live current_profile_no. Echoing a possibly-stale
        // claim back would assert a profile the row was never written to. See #411.
        var target = await this._router.CreateTargetAsync(this.UserId, model.Destination);
        var result = await this._nestService.CreateAsync(target, nest);
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
    public async Task<IActionResult> Update(int uid, [FromBody] NestUpdate model)
    {
        var found = await this._router.FindAsync(this._nestService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var (existing, ownerId) = found.Value;

        if (await this._router.MoveTargetAsync(this.UserId, model.Destination, ownerId) is { } moveTo)
        {
            model.ApplyUpdate(existing);
            return this.MovedResponse(await this._router.MoveAsync(this._nestService, "nest", this.UserId, existing, ownerId, moveTo));
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

        var result = await this._nestService.UpdateAsync(ownerId, existing);
        this._router.Stamp(result, ownerId);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var found = await this._router.FindAsync(this._nestService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var ownerId = found.Value.OwnerId;

        await this._nestService.DeleteAsync(ownerId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._router.DeleteAllAsync(this._nestService, this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var result = await this._router.UpdateDistanceByUidsAsync(this._nestService, this.UserId, this.ProfileNo, request.Uids, request.Distance);
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

        var result = await this._router.UpdateDistanceAllAsync(this._nestService, this.UserId, this.ProfileNo, distance);
        return this.DistanceUpdated(result);
    }
}
