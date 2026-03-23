using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/eggs")]
public class EggController(IEggService eggService, IMapper mapper) : BaseApiController
{
    private readonly IEggService _eggService = eggService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var eggs = await this._eggService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(eggs);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var egg = await this._eggService.GetByUidAsync(uid);
        if (egg == null || this.NotOwnedByCurrentUser(egg.Id))
        {
            return this.NotFound();
        }

        return this.Ok(egg);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EggCreate model)
    {
        var egg = this._mapper.Map<Egg>(model);
        egg.ProfileNo = this.ProfileNo;
        var result = await this._eggService.CreateAsync(this.UserId, egg);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] EggUpdate model)
    {
        var existing = await this._eggService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._eggService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._eggService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        await this._eggService.DeleteAsync(uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._eggService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._eggService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._eggService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
