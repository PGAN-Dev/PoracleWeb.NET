using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/nests")]
public class NestController(INestService nestService, IMapper mapper) : BaseApiController
{
    private readonly INestService _nestService = nestService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var nests = await this._nestService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(nests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var nest = await this._nestService.GetByUidAsync(uid);
        if (nest == null || this.NotOwnedByCurrentUser(nest.Id))
        {
            return this.NotFound();
        }

        return this.Ok(nest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NestCreate model)
    {
        var nest = this._mapper.Map<Nest>(model);
        nest.ProfileNo = this.ProfileNo;
        var result = await this._nestService.CreateAsync(this.UserId, nest);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] NestUpdate model)
    {
        var existing = await this._nestService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._nestService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._nestService.GetByUidAsync(uid);
        if (existing == null || this.NotOwnedByCurrentUser(existing.Id))
        {
            return this.NotFound();
        }

        await this._nestService.DeleteAsync(uid);
        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._nestService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._nestService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._nestService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
