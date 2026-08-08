using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/cleaning")]
public class CleaningController(ICleaningService cleaningService, IFeatureGate featureGate) : BaseApiController
{
    private readonly ICleaningService _cleaningService = cleaningService;
    private readonly IFeatureGate _featureGate = featureGate;

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = await this._cleaningService.GetCleanStatusAsync(this.UserId, this.ProfileNo);
        return this.Ok(status);
    }

    [HttpPut("all/{enabled:int}")]
    public async Task<IActionResult> ToggleAll(int enabled)
    {
        // Each toggle re-checks its own feature gate, so awaiting all of them unconditionally meant one
        // disabled alarm type threw partway through -- 403 to the caller, with the types processed before
        // it already written. Skip disabled types instead, and tell the caller which were skipped.
        var total = 0;
        var skipped = new List<string>();

        foreach (var (disableKey, toggle) in this.BulkToggles())
        {
            if (!await this._featureGate.IsEnabledAsync(disableKey))
            {
                skipped.Add(disableKey);
                continue;
            }

            total += await toggle(enabled);
        }

        return this.Ok(new
        {
            updated = total,
            skipped,
        });
    }

    /// <summary>Every alarm type the bulk toggle covers, paired with the gate that can switch it off.</summary>
    private IEnumerable<(string DisableKey, Func<int, Task<int>> Toggle)> BulkToggles()
    {
        yield return (DisableFeatureKeys.Pokemon, e => this._cleaningService.ToggleCleanMonstersAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Raids, e => this._cleaningService.ToggleCleanRaidsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Raids, e => this._cleaningService.ToggleCleanEggsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Quests, e => this._cleaningService.ToggleCleanQuestsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Invasions, e => this._cleaningService.ToggleCleanInvasionsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Lures, e => this._cleaningService.ToggleCleanLuresAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Nests, e => this._cleaningService.ToggleCleanNestsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.Gyms, e => this._cleaningService.ToggleCleanGymsAsync(this.UserId, this.ProfileNo, e));
        yield return (DisableFeatureKeys.MaxBattles, e => this._cleaningService.ToggleCleanMaxBattlesAsync(this.UserId, this.ProfileNo, e));
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

            "maxbattles" => await this._cleaningService.ToggleCleanMaxBattlesAsync(this.UserId, this.ProfileNo, enabled),
            // Unknown types are client input, not a fault. "pokemon" is a natural guess for "monsters".
            _ => (int?)null
        };

        if (count is null)
        {
            return this.BadRequest(new
            {
                error = $"Unknown alarm type '{alarmType}'. Expected one of: {string.Join(", ", ValidAlarmTypes)}."
            });
        }

        return this.Ok(new
        {
            updated = count.Value
        });
    }

    private static readonly string[] ValidAlarmTypes =
    [
        "monsters", "raids", "eggs", "quests", "invasions",
        "lures", "nests", "gyms", "maxbattles",
    ];
}
