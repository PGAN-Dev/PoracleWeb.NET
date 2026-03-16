using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/quests")]
public class QuestController : BaseApiController
{
    private readonly IQuestService _questService;
    private readonly IMapper _mapper;

    public QuestController(IQuestService questService, IMapper mapper)
    {
        _questService = questService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var quests = await _questService.GetByUserAsync(UserId, ProfileNo);
        return Ok(quests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var quest = await _questService.GetByUidAsync(uid);
        if (quest == null)
            return NotFound();
        return Ok(quest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuestCreate model)
    {
        var quest = _mapper.Map<Quest>(model);
        var result = await _questService.CreateAsync(UserId, quest);
        return CreatedAtAction(nameof(GetByUid), new { uid = result.Uid }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] QuestUpdate model)
    {
        var existing = await _questService.GetByUidAsync(uid);
        if (existing == null)
            return NotFound();

        _mapper.Map(model, existing);
        var result = await _questService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await _questService.DeleteAsync(uid);
        if (!success)
            return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await _questService.DeleteAllByUserAsync(UserId, ProfileNo);
        return Ok(new { deleted = count });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await _questService.UpdateDistanceByUserAsync(UserId, ProfileNo, distance);
        return Ok(new { updated = count });
    }
}
