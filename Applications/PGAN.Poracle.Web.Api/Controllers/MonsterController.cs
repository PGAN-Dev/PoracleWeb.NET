using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/monsters")]
public class MonsterController : BaseApiController
{
    private readonly IMonsterService _monsterService;
    private readonly IMapper _mapper;

    public MonsterController(IMonsterService monsterService, IMapper mapper)
    {
        _monsterService = monsterService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var monsters = await _monsterService.GetByUserAsync(UserId, ProfileNo);
        return Ok(monsters);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var monster = await _monsterService.GetByUidAsync(uid);
        if (monster == null)
            return NotFound();
        return Ok(monster);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MonsterCreate model)
    {
        var monster = _mapper.Map<Monster>(model);
        var result = await _monsterService.CreateAsync(UserId, monster);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] MonsterUpdate model)
    {
        var existing = await _monsterService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _monsterService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _monsterService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _monsterService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _monsterService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
