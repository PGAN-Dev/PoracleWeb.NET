using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/raids")]
public class RaidController : BaseApiController
{
    private readonly IRaidService _raidService;
    private readonly IMapper _mapper;

    public RaidController(IRaidService raidService, IMapper mapper)
    {
        _raidService = raidService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var raids = await _raidService.GetByUserAsync(UserId, ProfileNo);
        return Ok(raids);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var raid = await _raidService.GetByUidAsync(uid);
        if (raid == null)
            return NotFound();
        return Ok(raid);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RaidCreate model)
    {
        var raid = _mapper.Map<Raid>(model);
        var result = await _raidService.CreateAsync(UserId, raid);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] RaidUpdate model)
    {
        var existing = await _raidService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _raidService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _raidService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _raidService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _raidService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
