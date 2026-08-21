using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/monsters")]
public class MonsterController(IMonsterService monsterService) : BaseApiController
{
    private readonly IMonsterService _monsterService = monsterService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var monsters = await this._monsterService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(monsters);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var monster = await this._monsterService.GetByUidAsync(this.UserId, uid);
        if (monster == null)
        {
            return this.NotFound();
        }

        return this.Ok(monster);
    }

    [RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
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
        var result = await this._monsterService.CreateAsync(this.UserId, monster);

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

    [RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] MonsterUpdate model)
    {
        var existing = await this._monsterService.GetByUidAsync(this.UserId, uid);
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

        // Checked after the merge, not on the request: a PUT carrying only minIv inverts the window
        // against the value already stored, which validating the DTO alone cannot see. See #461.
        var inverted = MonsterRangeValidator.Validate(existing);
        if (inverted != null)
        {
            return this.BadRequest(new { error = inverted });
        }

        var result = await this._monsterService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._monsterService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._monsterService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._monsterService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._monsterService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var invalid = this.RejectInvalidDistance(distance);
        if (invalid != null)
        {
            return invalid;
        }

        var count = await this._monsterService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
