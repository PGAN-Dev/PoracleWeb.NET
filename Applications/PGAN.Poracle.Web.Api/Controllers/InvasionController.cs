using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/invasions")]
public class InvasionController : BaseApiController
{
    private readonly IInvasionService _invasionService;
    private readonly IMapper _mapper;

    public InvasionController(IInvasionService invasionService, IMapper mapper)
    {
        _invasionService = invasionService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var invasions = await _invasionService.GetByUserAsync(UserId, ProfileNo);
        return Ok(invasions);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var invasion = await _invasionService.GetByUidAsync(uid);
        if (invasion == null)
            return NotFound();
        return Ok(invasion);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InvasionCreate model)
    {
        var invasion = _mapper.Map<Invasion>(model);
        var result = await _invasionService.CreateAsync(UserId, invasion);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] InvasionUpdate model)
    {
        var existing = await _invasionService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _invasionService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _invasionService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _invasionService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _invasionService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
