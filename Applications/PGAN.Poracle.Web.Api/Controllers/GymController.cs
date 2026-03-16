using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/gyms")]
public class GymController : BaseApiController
{
    private readonly IGymService _gymService;
    private readonly IMapper _mapper;

    public GymController(IGymService gymService, IMapper mapper)
    {
        _gymService = gymService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var gyms = await _gymService.GetByUserAsync(UserId, ProfileNo);
        return Ok(gyms);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var gym = await _gymService.GetByUidAsync(uid);
        if (gym == null)
            return NotFound();
        return Ok(gym);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GymCreate model)
    {
        var gym = _mapper.Map<Gym>(model);
        var result = await _gymService.CreateAsync(UserId, gym);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] GymUpdate model)
    {
        var existing = await _gymService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _gymService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _gymService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _gymService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _gymService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
