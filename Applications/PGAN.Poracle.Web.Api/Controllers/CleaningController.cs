using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/cleaning")]
public class CleaningController : BaseApiController
{
    private readonly ICleaningService _cleaningService;

    public CleaningController(ICleaningService cleaningService)
    {
        _cleaningService = cleaningService;
    }

    [HttpPut("{alarmType}/{enabled:int}")]
    public async Task<IActionResult> ToggleClean(string alarmType, int enabled)
    {
        var count = alarmType.ToLowerInvariant() switch
        {
            "monsters" => await _cleaningService.ToggleCleanMonstersAsync(UserId, ProfileNo, enabled),
            "raids" => await _cleaningService.ToggleCleanRaidsAsync(UserId, ProfileNo, enabled),
            "eggs" => await _cleaningService.ToggleCleanEggsAsync(UserId, ProfileNo, enabled),
            "quests" => await _cleaningService.ToggleCleanQuestsAsync(UserId, ProfileNo, enabled),
            "invasions" => await _cleaningService.ToggleCleanInvasionsAsync(UserId, ProfileNo, enabled),
            "lures" => await _cleaningService.ToggleCleanLuresAsync(UserId, ProfileNo, enabled),
            "nests" => await _cleaningService.ToggleCleanNestsAsync(UserId, ProfileNo, enabled),
            "gyms" => await _cleaningService.ToggleCleanGymsAsync(UserId, ProfileNo, enabled),
            _ => throw new ArgumentException($"Unknown alarm type: {alarmType}")
        };

        return Ok(new { updated = count });
    }
}
