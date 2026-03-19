using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/invasions")]
public class InvasionController(IInvasionService invasionService, IMapper mapper) : BaseApiController
{
    private readonly IInvasionService _invasionService = invasionService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var invasions = await this._invasionService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(invasions);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var invasion = await this._invasionService.GetByUidAsync(uid);
        if (invasion == null)
        {
            return this.NotFound();
        }

        return this.Ok(invasion);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InvasionCreate model)
    {
        var invasion = this._mapper.Map<Invasion>(model);
        var result = await this._invasionService.CreateAsync(this.UserId, invasion);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] InvasionUpdate model)
    {
        var existing = await this._invasionService.GetByUidAsync(uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._invasionService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await this._invasionService.DeleteAsync(uid);
        if (!success)
        {
            return this.NotFound();
        }

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
