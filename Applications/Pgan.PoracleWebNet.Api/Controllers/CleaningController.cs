using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/cleaning")]
public class CleaningController(ICleaningService cleaningService) : BaseApiController
{
    private readonly ICleaningService _cleaningService = cleaningService;

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = await this._cleaningService.GetCleanStatusAsync(this.UserId, this.ProfileNo);
        return this.Ok(status);
    }

    [HttpPut("all/{enabled:int}")]
    public async Task<IActionResult> ToggleAll(int enabled)
    {
        var total = 0;
        total += await this._cleaningService.ToggleCleanMonstersAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanRaidsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanEggsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanQuestsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanInvasionsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanLuresAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanNestsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanGymsAsync(this.UserId, this.ProfileNo, enabled);
        total += await this._cleaningService.ToggleCleanFortChangesAsync(this.UserId, this.ProfileNo, enabled);
        return this.Ok(new
        {
            updated = total
        });
    }

    [HttpPut("{alarmType}/{enabled:int}")]
    public async Task<IActionResult> ToggleClean(string alarmType, int enabled)
    {
        var count = alarmType.ToLowerInvariant() switch
        {
            "monsters" => await this._cleaningService.ToggleCleanMonstersAsync(this.UserId, this.ProfileNo, enabled),
            "raids" => await this._cleaningService.ToggleCleanRaidsAsync(this.UserId, this.ProfileNo, enabled),
            "eggs" => await this._cleaningService.ToggleCleanEggsAsync(this.UserId, this.ProfileNo, enabled),
            "quests" => await this._cleaningService.ToggleCleanQuestsAsync(this.UserId, this.ProfileNo, enabled),
            "invasions" => await this._cleaningService.ToggleCleanInvasionsAsync(this.UserId, this.ProfileNo, enabled),
            "lures" => await this._cleaningService.ToggleCleanLuresAsync(this.UserId, this.ProfileNo, enabled),
            "nests" => await this._cleaningService.ToggleCleanNestsAsync(this.UserId, this.ProfileNo, enabled),
            "gyms" => await this._cleaningService.ToggleCleanGymsAsync(this.UserId, this.ProfileNo, enabled),
            "fortchanges" => await this._cleaningService.ToggleCleanFortChangesAsync(this.UserId, this.ProfileNo, enabled),
            _ => throw new ArgumentException($"Unknown alarm type: {alarmType}")
        };

        return this.Ok(new
        {
            updated = count
        });
    }
}
