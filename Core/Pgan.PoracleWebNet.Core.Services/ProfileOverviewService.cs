using System.Text.Json;

using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class ProfileOverviewService(
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy,
    IFeatureGate featureGate) : IProfileOverviewService
{
    private static readonly string[] AlarmTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle", "fort"];

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<int> DuplicateProfileAsync(string userId, int sourceProfileNo, int newProfileNo)
    {
        // Get all alarms across all profiles
        var allTracking = await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);

        // Pre-validate: this service writes to the tracking proxy directly, bypassing the per-type
        // alarm services and their FeatureGate checks. Without this pass, a partial duplicate could
        // succeed for some types and 403 mid-loop for a disabled type — leaving the new profile
        // half-populated. Walk the source first to surface the disable error before any writes. (#236)
        await this.EnsureNoDisabledTypesAsync(allTracking, alarm => alarm.GetIntProp("profile_no") == sourceProfileNo);

        // Remember current profile so we can restore it
        var humanJson = await this._humanProxy.GetHumanAsync(userId);
        var originalProfileNo = humanJson?.GetIntProp("current_profile_no") ?? 1;

        // Switch to the new profile so PoracleNG scopes creates to it
        await this._humanProxy.SwitchProfileAsync(userId, newProfileNo);

        var totalCreated = 0;
        try
        {
            foreach (var type in AlarmTypes)
            {
                if (!allTracking.TryGetProperty(type, out var alarmsArray) ||
                    alarmsArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var alarm in alarmsArray.EnumerateArray())
                {
                    if (alarm.GetIntProp("profile_no") != sourceProfileNo)
                    {
                        continue;
                    }

                    // Strip uid so PoracleNG creates a new alarm instead of updating
                    var cleaned = PoracleJsonHelper.StripProperty(alarm, "uid");
                    await this._trackingProxy.CreateAsync(type, userId, cleaned);
                    totalCreated++;
                }
            }
        }
        finally
        {
            // Always restore the original profile
            await this._humanProxy.SwitchProfileAsync(userId, originalProfileNo);
        }

        return totalCreated;
    }

    public async Task<int> ImportAlarmsAsync(string userId, int targetProfileNo, JsonElement alarms)
    {
        // Pre-validate the import payload before any state mutation. See DuplicateProfileAsync above
        // for why this can't be a per-iteration check. (#236)
        await this.EnsureNoDisabledTypesAsync(alarms, _ => true);

        var humanJson = await this._humanProxy.GetHumanAsync(userId);
        var originalProfileNo = humanJson?.GetIntProp("current_profile_no") ?? 1;

        await this._humanProxy.SwitchProfileAsync(userId, targetProfileNo);

        var totalCreated = 0;
        try
        {
            foreach (var type in AlarmTypes)
            {
                if (!alarms.TryGetProperty(type, out var alarmsArray) ||
                    alarmsArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var alarm in alarmsArray.EnumerateArray())
                {
                    // Strip uid defensively — export removes it client-side but manually edited backups may include it
                    var cleaned = alarm.TryGetProperty("uid", out _)
                        ? PoracleJsonHelper.StripProperty(alarm, "uid")
                        : alarm;
                    await this._trackingProxy.CreateAsync(type, userId, cleaned);
                    totalCreated++;
                }
            }
        }
        finally
        {
            await this._humanProxy.SwitchProfileAsync(userId, originalProfileNo);
        }

        return totalCreated;
    }

    public async Task<JsonElement> GetAllProfilesOverviewAsync(string userId) => await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);

    /// <summary>
    /// Throws <see cref="FeatureDisabledException"/> on the first alarm-type bucket that has at
    /// least one matching alarm AND whose <c>disable_*</c> setting is true. <paramref name="alarmFilter"/>
    /// lets <c>DuplicateProfileAsync</c> skip alarms outside the source profile so a disabled type
    /// only blocks when alarms of that type are actually about to be copied.
    /// </summary>
    private async Task EnsureNoDisabledTypesAsync(JsonElement payload, Func<JsonElement, bool> alarmFilter)
    {
        foreach (var type in AlarmTypes)
        {
            if (!payload.TryGetProperty(type, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var hasAny = false;
            foreach (var alarm in arr.EnumerateArray())
            {
                if (alarmFilter(alarm))
                {
                    hasAny = true;
                    break;
                }
            }

            if (!hasAny)
            {
                continue;
            }

            if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var key))
            {
                await this._featureGate.EnsureEnabledAsync(key);
            }
        }
    }
}
