using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/eggs")]
public class EggController : BaseApiController
{
    private readonly IEggService _eggService;
    private readonly IMapper _mapper;

    public EggController(IEggService eggService, IMapper mapper)
    {
        _eggService = eggService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var eggs = await _eggService.GetByUserAsync(UserId, ProfileNo);
        return Ok(eggs);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var egg = await _eggService.GetByUidAsync(uid);
        if (egg == null)
            return NotFound();
        return Ok(egg);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EggCreate model)
    {
        var egg = _mapper.Map<Egg>(model);
        var result = await _eggService.CreateAsync(UserId, egg);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] EggUpdate model)
    {
        var existing = await _eggService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _eggService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _eggService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _eggService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _eggService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
