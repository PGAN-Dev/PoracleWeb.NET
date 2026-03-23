using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/raids")]
public class RaidController(IRaidService raidService, IMapper mapper) : BaseApiController
{
    private readonly IRaidService _raidService = raidService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var raids = await this._raidService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(raids);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var raid = await this._raidService.GetByUidAsync(uid);
        if (raid == null || this.NotOwnedByCurrentUser(raid.Id))
        {
            return this.NotFound();
        }

        return this.Ok(raid);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RaidCreate model)
    {
        var raid = this._mapper.Map<Raid>(model);
        raid.ProfileNo = this.ProfileNo;
        var result = await this._raidService.CreateAsync(this.UserId, raid);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] RaidUpdate model)
    {
        var existing = await this._raidService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._raidService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._raidService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        await this._raidService.DeleteAsync(uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._raidService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._raidService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._raidService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
