using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

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
        if (nest == null)
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
        if (existing == null)
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
        var success = await this._nestService.DeleteAsync(uid);
        if (!success)
        {
            return this.NotFound();
        }

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
        var count = await this._nestService.UpdateDistanceByUidsAsync(request.Uids, request.Distance);
        return this.Ok(new { updated = count });
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
