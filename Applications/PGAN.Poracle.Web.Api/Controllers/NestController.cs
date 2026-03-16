using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/nests")]
public class NestController : BaseApiController
{
    private readonly INestService _nestService;
    private readonly IMapper _mapper;

    public NestController(INestService nestService, IMapper mapper)
    {
        _nestService = nestService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var nests = await _nestService.GetByUserAsync(UserId, ProfileNo);
        return Ok(nests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var nest = await _nestService.GetByUidAsync(uid);
        if (nest == null)
            return NotFound();
        return Ok(nest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NestCreate model)
    {
        var nest = _mapper.Map<Nest>(model);
        var result = await _nestService.CreateAsync(UserId, nest);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] NestUpdate model)
    {
        var existing = await _nestService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _nestService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _nestService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _nestService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _nestService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
