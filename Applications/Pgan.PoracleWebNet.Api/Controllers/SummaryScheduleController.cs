using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Per-user quest summary delivery schedules. The schedule is an <c>active_hours</c> array keyed by the
/// authenticated user + <c>quest</c> — there is NO id/userId route segment or body field anywhere
/// (the JWT's userId is the sole id source; <c>summary_schedules</c> is keyed per-user with no profile_no,
/// so any forwarded request-supplied id would be a full read/write/delete/trigger IDOR). The whole
/// controller is gated by <c>disable_quests</c>; admins are intentionally NOT exempt (see #236).
/// </summary>
[Route("api/summary-schedules")]
[RequireFeatureEnabled(DisableFeatureKeys.Quests)]
public class SummaryScheduleController(
    IPoracleSummaryProxy summaryProxy,
    ISummaryCapabilityService capability) : BaseApiController
{
    // Case-insensitive on purpose (deliberate upgrade over TestAlertController's case-sensitive set).
    private static readonly HashSet<string> ValidAlertTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "quest"
    };

    private readonly IPoracleSummaryProxy _summaryProxy = summaryProxy;
    private readonly ISummaryCapabilityService _capability = capability;

    /// <summary>
    /// Returns whether quest summary delivery is enabled on this server, as a 200-body boolean sourced
    /// from the config flag (<c>tracking.quest_summary_enabled</c>). Degrades to <c>enabled:false</c> on
    /// any fault — never returns 5xx — so a transient outage is never mistaken for "feature off".
    /// </summary>
    [HttpGet("capability")]
    public async Task<IActionResult> GetCapability()
    {
        return this.Ok(new
        {
            enabled = await this._capability.IsQuestSummaryEnabledAsync()
        });
    }

    /// <summary>Lists every summary schedule for the authenticated user, across alert types.</summary>
    [HttpGet]
    public async Task<IActionResult> GetSchedules()
    {
        var schedulesJson = await this._summaryProxy.GetSchedulesAsync(this.UserId);
        if (schedulesJson is not { ValueKind: JsonValueKind.Array } array)
        {
            return this.Ok(Array.Empty<SummarySchedule>());
        }

        var schedules = new List<SummarySchedule>();
        foreach (var element in array.EnumerateArray())
        {
            schedules.Add(MapSchedule(element));
        }

        return this.Ok(schedules);
    }

    /// <summary>
    /// Gets the schedule for one alert type. When no schedule exists yet this returns 200 with an
    /// empty schedule (<c>active_hours = []</c>) rather than 404 — "no schedule yet" is a normal
    /// empty state, not an error, and a 404 would trip the SPA's global not-found toast. Consistent
    /// with <see cref="GetSchedules"/> returning an empty array.
    /// </summary>
    [HttpGet("{alertType}")]
    public async Task<IActionResult> GetSchedule(string alertType)
    {
        if (!ValidAlertTypes.Contains(alertType))
        {
            return this.BadRequest(new
            {
                error = $"Invalid alarm type: {alertType}"
            });
        }

        var scheduleJson = await this._summaryProxy.GetScheduleAsync(this.UserId, alertType);
        if (scheduleJson is not { ValueKind: JsonValueKind.Object } element)
        {
            return this.Ok(new SummarySchedule { AlertType = alertType, ActiveHours = "[]" });
        }

        return this.Ok(MapSchedule(element));
    }

    /// <summary>
    /// Creates or replaces the schedule for one alert type (upsert). The DTO carries ONLY
    /// <c>ActiveHours</c> — any id/userId/alertType body field is ignored; the route's validated
    /// <c>{alertType}</c> is the sole alert-type source. Validates the active-hours payload via the
    /// shared <see cref="ActiveHoursValidator"/> BEFORE proxying; null/whitespace clears the schedule.
    /// </summary>
    [HttpPut("{alertType}")]
    [EnableRateLimiting("auth-read")]
    public async Task<IActionResult> SetSchedule(string alertType, [FromBody] SummaryScheduleRequest request)
    {
        if (!ValidAlertTypes.Contains(alertType))
        {
            return this.BadRequest(new
            {
                error = $"Invalid alarm type: {alertType}"
            });
        }

        var (isValid, error) = ActiveHoursValidator.Validate(request.ActiveHours);
        if (!isValid)
        {
            return this.BadRequest(new
            {
                error
            });
        }

        var activeHours = string.IsNullOrWhiteSpace(request.ActiveHours) ? "[]" : request.ActiveHours;
        await this._summaryProxy.SetScheduleAsync(this.UserId, alertType, activeHours);
        return this.NoContent();
    }

    /// <summary>Removes the schedule for one alert type. Idempotent — succeeds even when absent.</summary>
    [HttpDelete("{alertType}")]
    [EnableRateLimiting("auth-read")]
    public async Task<IActionResult> DeleteSchedule(string alertType)
    {
        if (!ValidAlertTypes.Contains(alertType))
        {
            return this.BadRequest(new
            {
                error = $"Invalid alarm type: {alertType}"
            });
        }

        await this._summaryProxy.DeleteScheduleAsync(this.UserId, alertType);
        return this.NoContent();
    }

    /// <summary>
    /// Flush-and-deliver-now: re-enriches the buffered quests, renders, and delivers the summary DM
    /// synchronously, then clears the bucket. Rate-limited (5/60s) and client-cooldown-guarded so a
    /// double-click cannot double-deliver.
    /// </summary>
    [HttpPost("{alertType}/trigger")]
    [EnableRateLimiting("test-alert")]
    public async Task<IActionResult> Trigger(string alertType)
    {
        if (!ValidAlertTypes.Contains(alertType))
        {
            return this.BadRequest(new
            {
                error = $"Invalid alarm type: {alertType}"
            });
        }

        await this._summaryProxy.TriggerAsync(this.UserId, alertType);
        return this.NoContent();
    }

    // Maps the upstream { id, alert_type, active_hours } element to SummarySchedule.
    // MUST NOT read or echo the upstream "id" — it is the user id (IDOR leak).
    private static SummarySchedule MapSchedule(JsonElement element)
    {
        var alertType = element.TryGetProperty("alert_type", out var alertTypeProp) && alertTypeProp.ValueKind == JsonValueKind.String
            ? alertTypeProp.GetString() ?? "quest"
            : "quest";

        var activeHours = "[]";
        if (element.TryGetProperty("active_hours", out var activeHoursProp))
        {
            activeHours = activeHoursProp.ValueKind == JsonValueKind.String
                ? activeHoursProp.GetString() ?? "[]"
                : activeHoursProp.GetRawText();
        }

        return new SummarySchedule
        {
            AlertType = alertType,
            ActiveHours = activeHours
        };
    }
}
