using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/lures")]
public class LureController(ILureService lureService, IMapper mapper) : BaseApiController
{
    private readonly ILureService _lureService = lureService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var lures = await this._lureService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(lures);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var lure = await this._lureService.GetByUidAsync(uid);
        if (lure == null || this.NotOwnedByCurrentUser(lure.Id))
        {
            return this.NotFound();
        }

        return this.Ok(lure);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LureCreate model)
    {
        var lure = this._mapper.Map<Lure>(model);
        lure.ProfileNo = this.ProfileNo;
        var result = await this._lureService.CreateAsync(this.UserId, lure);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] LureUpdate model)
    {
        var existing = await this._lureService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._lureService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._lureService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        await this._lureService.DeleteAsync(uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._lureService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._lureService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._lureService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
