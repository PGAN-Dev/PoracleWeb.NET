using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/quests")]
public class QuestController(IQuestService questService, IMapper mapper) : BaseApiController
{
    private readonly IQuestService _questService = questService;
    private readonly IMapper _mapper = mapper;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var quests = await this._questService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(quests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var quest = await this._questService.GetByUidAsync(uid);
        if (quest == null)
        {
            return this.NotFound();
        }

        return this.Ok(quest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuestCreate model)
    {
        var quest = this._mapper.Map<Quest>(model);
        quest.ProfileNo = this.ProfileNo;
        var result = await this._questService.CreateAsync(this.UserId, quest);
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] QuestUpdate model)
    {
        var existing = await this._questService.GetByUidAsync(uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        this._mapper.Map(model, existing);
        var result = await this._questService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var success = await this._questService.DeleteAsync(uid);
        if (!success)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        var count = await this._questService.DeleteAllByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(new
        {
            deleted = count
        });
    }

    [HttpPut("distance/bulk")]
    public async Task<IActionResult> UpdateBulkDistance([FromBody] BulkDistanceRequest request)
    {
        var count = await this._questService.UpdateDistanceByUidsAsync(request.Uids, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var count = await this._questService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
