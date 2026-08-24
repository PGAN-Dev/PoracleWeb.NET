using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/quests")]
[RequireFeatureEnabled(DisableFeatureKeys.Quests)]
public class QuestController(IQuestService questService, IQuestPokecoinCapabilityService pokecoinCapability) : BaseApiController
{
    private readonly IQuestService _questService = questService;
    private readonly IQuestPokecoinCapabilityService _pokecoinCapability = pokecoinCapability;

    /// <summary>
    /// Which optional quest reward types the PoracleNG behind this install can actually store.
    /// </summary>
    /// <remarks>
    /// Degrades to <c>pokecoins:false</c> on any fault -- never 5xx -- so a transient outage hides the
    /// tab rather than offering one every save of which would be refused. Mirrors the shape of
    /// <c>GET /api/summary-schedules/capability</c>. This is presentation only: the real refusal lives
    /// in <c>QuestService</c>, because quick-pick apply and profile import never pass through a dialog.
    /// </remarks>
    [HttpGet("capability")]
    public async Task<IActionResult> GetCapability() => this.Ok(new
    {
        pokecoins = await this._pokecoinCapability.ArePokecoinRewardsSupportedAsync()
    });

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var quests = await this._questService.GetByUserAsync(this.UserId, this.ProfileNo);
        return this.Ok(quests);
    }

    [HttpGet("{uid:int}")]
    public async Task<IActionResult> GetByUid(int uid)
    {
        var quest = await this._questService.GetByUidAsync(this.UserId, uid);
        if (quest == null)
        {
            return this.NotFound();
        }

        return this.Ok(quest);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuestCreate model)
    {
        var quest = model.ToQuest();
        // Deliberately not stamped from the JWT claim: writes no longer carry profile_no, so
        // PoracleNG files the alarm under the live current_profile_no. Echoing a possibly-stale
        // claim back would assert a profile the row was never written to. See #411.
        var result = await this._questService.CreateAsync(this.UserId, quest);

        // PoracleNG assigns no uid when the submission duplicates an alarm the user already has, so
        // nothing was created. Answering 201 with a Location of /0 advertised a resource that 404s.
        // 200 keeps multi-select creates working while no longer claiming a creation. See #459.
        if (result.Uid <= 0)
        {
            return this.Ok(result);
        }
        return this.CreatedAtAction(nameof(GetByUid), new
        {
            uid = result.Uid
        }, result);
    }

    [HttpPut("{uid:int}")]
    public async Task<IActionResult> Update(int uid, [FromBody] QuestUpdate model)
    {
        var existing = await this._questService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        // Nothing to write means nothing to send: see LeavesAlarmUnchanged.
        if (LeavesAlarmUnchanged(existing, () =>
        {
            model.ApplyUpdate(existing);
            return existing;
        }))
        {
            return this.Ok(existing);
        }

        var result = await this._questService.UpdateAsync(this.UserId, existing);
        return this.Ok(result);
    }

    [HttpDelete("{uid:int}")]
    public async Task<IActionResult> Delete(int uid)
    {
        var existing = await this._questService.GetByUidAsync(this.UserId, uid);
        if (existing == null)
        {
            return this.NotFound();
        }

        await this._questService.DeleteAsync(this.UserId, uid);
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
        var count = await this._questService.UpdateDistanceByUidsAsync(request.Uids, this.UserId, request.Distance);
        return this.Ok(new
        {
            updated = count
        });
    }

    [HttpPut("distance")]
    public async Task<IActionResult> UpdateAllDistance([FromBody] int distance)
    {
        var invalid = this.RejectInvalidDistance(distance);
        if (invalid != null)
        {
            return invalid;
        }

        var count = await this._questService.UpdateDistanceByUserAsync(this.UserId, this.ProfileNo, distance);
        return this.Ok(new
        {
            updated = count
        });
    }
}
