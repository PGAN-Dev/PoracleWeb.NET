using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/monsters")]
[RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class MonsterController(IMonsterService monsterService, IPokemonEditCapabilityService editCapability, LinkedAlarmRouter router) : BaseApiController
{
    private readonly IMonsterService _monsterService = monsterService;
    private readonly LinkedAlarmRouter _router = router;
    private readonly IPokemonEditCapabilityService _editCapability = editCapability;

    /// <summary>
    /// Whether this deployment's PoracleNG honours the pokemon edit-in-place bit
    /// (jfberry/PoracleNG#246). Degrades to <c>edit:false</c> on any fault -- never 5xx -- so a
    /// transient outage hides the toggle rather than offering a control that would be a no-op.
    /// Mirrors the shape of <c>GET /api/summary-schedules/capability</c>.
    /// </summary>
    [HttpGet("capability")]
    public async Task<IActionResult> GetCapability() => this.Ok(new
    {
        edit = await this._editCapability.IsPokemonEditEnabledAsync()
    });

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var monsters = await this._router.ListAsync(this._monsterService, this.UserId, this.ProfileNo);
        return this.Ok(monsters);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var monster = (await this._router.FindAsync(this._monsterService, this.UserId, uid))?.Model;
        if (monster == null)
        {
            return this.NotFound();
        }

        return this.Ok(monster);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MonsterCreate model)
    {
        var monster = model.ToMonster();

        var inverted = MonsterRangeValidator.Validate(monster);
        if (inverted != null)
        {
            return this.BadRequest(new { error = inverted });
        }

        // Deliberately not stamped from the JWT claim: writes no longer carry profile_no, so
        // PoracleNG files the alarm under the live current_profile_no. Echoing a possibly-stale
        // claim back would assert a profile the row was never written to. See #411.
        var target = await this._router.CreateTargetAsync(this.UserId, model.Destination);
        var result = await this._monsterService.CreateAsync(target, monster);
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
    public async Task<IActionResult> Update(int uid, [FromBody] MonsterUpdate model)
    {
        var found = await this._router.FindAsync(this._monsterService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var (existing, ownerId) = found.Value;

        if (await this._router.MoveTargetAsync(this.UserId, model.Destination, ownerId) is { } moveTo)
        {
            model.ApplyUpdate(existing);
            return this.MoveResult(await this._router.MoveAsync(this._monsterService, "pokemon", this.UserId, existing, ownerId, moveTo));
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

        // Checked after the merge, not on the request: a PUT carrying only minIv inverts the window
        // against the value already stored, which validating the DTO alone cannot see. See #461.
        var inverted = MonsterRangeValidator.Validate(existing);
        if (inverted != null)
        {
            return this.BadRequest(new { error = inverted });
        }

        var result = await this._monsterService.UpdateAsync(ownerId, existing);
        this._router.Stamp(result, ownerId);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var found = await this._router.FindAsync(this._monsterService, this.UserId, uid);
        if (found is null)
        {
            return this.NotFound();
        }

        var ownerId = found.Value.OwnerId;

        await this._monsterService.DeleteAsync(ownerId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._router.DeleteAllAsync(this._monsterService, this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var result = await this._router.UpdateDistanceByUidsAsync(this._monsterService, this.UserId, this.ProfileNo, request.Uids, request.Distance);
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

        var result = await this._router.UpdateDistanceAllAsync(this._monsterService, this.UserId, this.ProfileNo, distance);
        return this.DistanceUpdated(result);
    }
}
