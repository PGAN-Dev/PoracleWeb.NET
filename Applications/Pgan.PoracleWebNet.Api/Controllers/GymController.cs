using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/gyms")]
[RequireFeatureEnabled(DisableFeatureKeys.Gyms)]
public class GymController(IGymService gymService) : BaseApiController
{
    private readonly IGymService _gymService = gymService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var gyms = await this._gymService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(gyms);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var gym = await this._gymService.GetByUidAsync(this.UserId, uid);
        if (gym == null)
        {
            return this.NotFound();
        }

        return this.Ok(gym);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GymCreate model)
    {
        var gym = model.ToGym();
        gym.ProfileNo = this.ProfileNo;
        var result = await this._gymService.CreateAsync(this.UserId, gym);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] GymUpdate model)
    {
        var existing = await this._gymService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        model.ApplyUpdate(existing);
        var result = await this._gymService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._gymService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._gymService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._gymService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._gymService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._gymService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
