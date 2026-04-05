using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/maxbattles")]
public class MaxBattleController(IMaxBattleService maxBattleService, IMapper mapper) : BaseApiController
{
    private readonly IMaxBattleService _maxBattleService = maxBattleService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var maxBattles = await this._maxBattleService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(maxBattles);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var maxBattle = await this._maxBattleService.GetByUidAsync(this.UserId, uid);
        if (maxBattle == null)
        {
            return this.NotFound();
        }

        return this.Ok(maxBattle);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MaxBattleCreate model)
    {
        var maxBattle = this._mapper.Map<MaxBattle>(model);
        maxBattle.ProfileNo = this.ProfileNo;
        var result = await this._maxBattleService.CreateAsync(this.UserId, maxBattle);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] MaxBattleUpdate model)
    {
        var existing = await this._maxBattleService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._maxBattleService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._maxBattleService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._maxBattleService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._maxBattleService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._maxBattleService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._maxBattleService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
