using System.Text.Json;

using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

public class CrossProfileService(
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy) : ICrossProfileService
{
    private static readonly string[] AlarmTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle", "fort"];

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;

    public async Task<int> DuplicateProfileAsync(string userId, int sourceProfileNo, int newProfileNo)
    {
        // Get all alarms across all profiles
        var allTracking = await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);

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
}
