using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/fort-changes")]
public class FortChangeController(IFortChangeService fortChangeService, IMapper mapper) : BaseApiController
{
    private readonly IFortChangeService _fortChangeService = fortChangeService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await this._fortChangeService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(items);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var item = await this._fortChangeService.GetByUidAsync(this.UserId, uid);
        if (item == null)
        {
            return this.NotFound();
        }

        return this.Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FortChangeCreate model)
    {
        var fortChange = this._mapper.Map<FortChange>(model);
        fortChange.ProfileNo = this.ProfileNo;
        var result = await this._fortChangeService.CreateAsync(this.UserId, fortChange);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] FortChangeUpdate model)
    {
        var existing = await this._fortChangeService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._fortChangeService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._fortChangeService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._fortChangeService.DeleteAsync(this.UserId, uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._fortChangeService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._fortChangeService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._fortChangeService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
