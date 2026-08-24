using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Shared validator for the <c>active_hours</c> JSON shape used by both profile schedules and
/// quest summary schedules. The schedule is a JSON array of <c>{day:1-7, hours:0-23, mins:0-59}</c>
/// entries (max 28). Extracted from <c>ProfileController.ValidateActiveHours</c> so the profile and
/// summary controllers share one implementation rather than risking drift between two copies.
///
/// An entry may also carry <c>step</c>, <c>end_hours</c> and <c>end_mins</c>, which turn it into a
/// repeating range: it fires at the start, then every <c>step</c> hours, up to and including the end.
/// Leniency here mirrors PoracleNG's own: <c>step</c> absent or non-positive is a single fire and the
/// end fields are then ignored rather than refused, exactly as <c>ActiveHourEntry.Fires()</c> ignores
/// them. The end fields carry <c>omitempty</c> upstream, so a range ending on the hour arrives with
/// <c>end_mins</c> missing; a missing end field means zero, never an error. See #808.
/// </summary>
public static class ActiveHoursValidator
{
    public static (bool IsValid, string? Error) Validate(string? activeHours)
    {
        if (string.IsNullOrWhiteSpace(activeHours))
        {
            return (true, null);
        }

        activeHours = activeHours.Trim();

        JsonElement arr;
        try
        {
            arr = JsonSerializer.Deserialize<JsonElement>(activeHours);
        }
        catch (JsonException)
        {
            return (false, "active_hours must be a valid JSON array.");
        }

        if (arr.ValueKind != JsonValueKind.Array)
        {
            return (false, "active_hours must be a JSON array.");
        }

        if (arr.GetArrayLength() > 28)
        {
            return (false, "active_hours may contain at most 28 entries.");
        }

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return (false, "Each active_hours entry must be an object.");
            }

            if (!entry.TryGetProperty("day", out var dayProp) || !TryGetIntValue(dayProp, out var day) || day < 1 || day > 7)
            {
                return (false, "Each active_hours entry must have a 'day' between 1 and 7.");
            }

            if (!entry.TryGetProperty("hours", out var hoursProp))
            {
                return (false, "Each active_hours entry must have an 'hours' property.");
            }

            if (!TryGetIntValue(hoursProp, out var hours) || hours < 0 || hours > 23)
            {
                return (false, "Each active_hours entry must have 'hours' between 0 and 23.");
            }

            if (!entry.TryGetProperty("mins", out var minsProp))
            {
                return (false, "Each active_hours entry must have a 'mins' property.");
            }

            if (!TryGetIntValue(minsProp, out var mins) || mins < 0 || mins > 59)
            {
                return (false, "Each active_hours entry must have 'mins' between 0 and 59.");
            }

            var step = 0;
            if (entry.TryGetProperty("step", out var stepProp) && stepProp.ValueKind != JsonValueKind.Null)
            {
                if (!TryGetIntValue(stepProp, out step) || step > 23)
                {
                    return (false, "An active_hours 'step' must be a whole number of hours between 1 and 23.");
                }
            }

            if (step <= 0)
            {
                // Single fire. Any end fields alongside it are inert upstream, so they are not checked.
                continue;
            }

            var endHours = 0;
            if (entry.TryGetProperty("end_hours", out var endHoursProp) && endHoursProp.ValueKind != JsonValueKind.Null &&
                (!TryGetIntValue(endHoursProp, out endHours) || endHours < 0 || endHours > 23))
            {
                return (false, "Each repeating active_hours entry must have 'end_hours' between 0 and 23.");
            }

            var endMins = 0;
            if (entry.TryGetProperty("end_mins", out var endMinsProp) && endMinsProp.ValueKind != JsonValueKind.Null &&
                (!TryGetIntValue(endMinsProp, out endMins) || endMins < 0 || endMins > 59))
            {
                return (false, "Each repeating active_hours entry must have 'end_mins' between 0 and 59.");
            }

            if (endHours * 60 + endMins <= hours * 60 + mins)
            {
                return (false, "A repeating active_hours entry must end after it starts (spanning midnight is not supported).");
            }
        }

        return (true, null);
    }

    private static bool TryGetIntValue(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
