using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

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
        if (raid == null)
        {
            return this.NotFound();
        }

        return this.Ok(raid);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RaidCreate model)
    {
        var raid = this._mapper.Map<Raid>(model);
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
        if (existing == null)
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
        var success = await this._raidService.DeleteAsync(uid);
        if (!success)
        {
            return this.NotFound();
        }

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
        var count = await this._raidService.UpdateDistanceByUidsAsync(request.Uids, request.Distance);
        return this.Ok(new { updated = count });
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
