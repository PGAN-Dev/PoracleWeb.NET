using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/lures")]
public class LureController : BaseApiController
{
    private readonly ILureService _lureService;
    private readonly IMapper _mapper;

    public LureController(ILureService lureService, IMapper mapper)
    {
        _lureService = lureService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var lures = await _lureService.GetByUserAsync(UserId, ProfileNo);
        return Ok(lures);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var lure = await _lureService.GetByUidAsync(uid);
        if (lure == null)
            return NotFound();
        return Ok(lure);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LureCreate model)
    {
        var lure = _mapper.Map<Lure>(model);
        var result = await _lureService.CreateAsync(UserId, lure);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] LureUpdate model)
    {
        var existing = await _lureService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _lureService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _lureService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _lureService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _lureService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
