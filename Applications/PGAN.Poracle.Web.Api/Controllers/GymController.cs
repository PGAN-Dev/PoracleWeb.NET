using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/gyms")]
public class GymController(IGymService gymService, IMapper mapper) : BaseApiController
{
    private readonly IGymService _gymService = gymService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var gyms = await this._gymService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(gyms);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var gym = await this._gymService.GetByUidAsync(uid);
        if (gym == null)
        {
            return this.NotFound();
        }

        return this.Ok(gym);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GymCreate model)
    {
        var gym = this._mapper.Map<Gym>(model);
        var result = await this._gymService.CreateAsync(this.UserId, gym);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] GymUpdate model)
    {
        var existing = await this._gymService.GetByUidAsync(uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._gymService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await this._gymService.DeleteAsync(uid);
        if (!success)
        {
            return this.NotFound();
        }

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
