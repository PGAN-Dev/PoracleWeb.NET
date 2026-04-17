using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/monsters")]
[RequireFeatureEnabled(DisableFeatureKeys.Pokemon)]
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MonsterCreate model)
    {
        var monster = model.ToMonster();
        monster.ProfileNo = this.ProfileNo;
        var result = await this._monsterService.CreateAsync(this.UserId, monster);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] MonsterUpdate model)
    {
        var existing = await this._monsterService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        model.ApplyUpdate(existing);
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

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._monsterService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._monsterService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
