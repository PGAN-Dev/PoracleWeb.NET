using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/cleaning")]
public class CleaningController(ICleaningService cleaningService) : BaseApiController
{
    private readonly ICleaningService _cleaningService = cleaningService;

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
            _ => throw new ArgumentException($"Unknown alarm type: {alarmType}")
        };

        return this.Ok(new
        {
            updated = count
        });
    }
}
