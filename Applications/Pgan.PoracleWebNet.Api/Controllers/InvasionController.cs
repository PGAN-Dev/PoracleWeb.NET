using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/invasions")]
public class InvasionController(IInvasionService invasionService) : BaseApiController
{
    private readonly IInvasionService _invasionService = invasionService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var invasions = await this._invasionService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(invasions);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var invasion = await this._invasionService.GetByUidAsync(this.UserId, uid);
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
        invasion.ProfileNo = this.ProfileNo;
        var result = await this._invasionService.CreateAsync(this.UserId, invasion);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] InvasionUpdate model)
    {
        var existing = await this._invasionService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        model.ApplyUpdate(existing);
        var result = await this._invasionService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._invasionService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._invasionService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._invasionService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._invasionService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._invasionService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
