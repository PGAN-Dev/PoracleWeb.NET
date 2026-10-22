using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Manages the "clean" flag on tracking alarms via the PoracleNG REST API proxy.
/// </summary>
public partial class CleaningService(
    IPoracleTrackingProxy trackingProxy,
    IFeatureGate featureGate,
    IPoracleIncidentProxy incidentProxy,
    ILogger<CleaningService> logger) : ICleaningService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IPoracleIncidentProxy _incidentProxy = incidentProxy;
    private readonly ILogger<CleaningService> _logger = logger;

    public async Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo)
    {
        var counts = await this.GetCleanCountsAsync(userId, profileNo);
        return counts.ToDictionary(kv => kv.Key, kv => kv.Value.IsAllClean());
    }

    public async Task<Dictionary<string, CleanCount>> GetCleanCountsAsync(string userId, int profileNo)
    {
        var allTracking = await this._trackingProxy.GetAllTrackingAsync(userId);

        return new Dictionary<string, CleanCount>
        {
            ["monsters"] = CountClean(allTracking, "pokemon"),
            ["raids"] = CountClean(allTracking, "raid"),
            ["eggs"] = CountClean(allTracking, "egg"),
            ["quests"] = CountClean(allTracking, "quest"),
            ["invasions"] = CountClean(allTracking, "invasion", await this.OwnRowPredicateAsync("invasion")),
            ["lures"] = CountClean(allTracking, "lure"),
            ["nests"] = CountClean(allTracking, "nest"),
            ["gyms"] = CountClean(allTracking, "gym"),
            ["maxbattles"] = CountClean(allTracking, "maxbattle"),
        };
    }

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("pokemon", userId, clean);

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("raid", userId, clean);

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("egg", userId, clean);

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("quest", userId, clean);

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("invasion", userId, clean);

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("lure", userId, clean);

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("nest", userId, clean);

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("gym", userId, clean);

    public async Task<int> ToggleCleanMaxBattlesAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("maxbattle", userId, clean);


    /// <summary>
    /// Which rows of a tracking type this page owns.
    /// </summary>
    /// <remarks>
    /// Invasion and incident rules share one table, and the invasion read folds the Pokestop event rules
    /// back in (<c>PoracleTrackingProxy</c> reads v2's incident list and restates it in v1's form), so the list
    /// does not partition them. A clean toggle over "invasion" swept up the user's Kecleon, Showcase and
    /// Gold Stop rules, set the auto-delete bit on them and POSTed them back through the invasion
    /// endpoint -- a write to rows that belong to the Pokestop Events page, from a switch that never
    /// mentions it. <see cref="InvasionService"/> and <c>DashboardService</c> both draw this line at the
    /// same boundary; cleaning did not. Where the event rules are this page's, they are patched through
    /// <c>incident</c>, since v2's invasion route does not hold them.
    /// </remarks>
    /// <remarks>
    /// Conditional for the same reason as <see cref="InvasionService"/>: when the Pokestop Events
    /// surface is switched off there is no other page holding those rows, and the invasion switch is
    /// the only control the user has over them.
    /// </remarks>
    private async Task<Func<JsonElement, bool>> OwnRowPredicateAsync(string type)
    {
        if (type != "invasion" || !await this._featureGate.IsEnabledAsync(DisableFeatureKeys.PokestopEvents))
        {
            return _ => true;
        }

        return row => !PokestopEventTypes.IsEventName(
            row.TryGetProperty("grunt_type", out var gt) && gt.ValueKind == JsonValueKind.String
                ? gt.GetString()
                : null);
    }

    /// <summary>
    /// Sets or clears the auto-delete bit on every rule of a type through one v2 <c>PATCH</c>, which names
    /// <c>clean</c> and nothing else, so the edit-in-place and summary bits stay as stored (#292) and each rule
    /// keeps its uid. Returns how many rules PoracleNG updated or found already set.
    /// </summary>
    private async Task<int> ToggleCleanAsync(string type, string userId, int clean)
    {
        // Cleaning writes through the proxy directly, bypassing the per-type alarm services and their
        // feature gates. Without this guard a user could toggle the clean flag on an alarm type the admin
        // disabled. (#236)
        if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var disableKey))
        {
            await this._featureGate.EnsureEnabledAsync(disableKey);
        }

        var trackingJson = await this._trackingProxy.GetByUserAsync(type, userId);

        if (trackingJson.ValueKind != JsonValueKind.Array || trackingJson.GetArrayLength() == 0)
        {
            return 0;
        }

        var isOurs = await this.OwnRowPredicateAsync(type);
        var ownRows = trackingJson.EnumerateArray().Where(isOurs).ToList();
        var eventUids = type == "invasion" ? UidsOf(ownRows.Where(IsEventRow)) : [];
        var ruleUids = UidsOf(ownRows).Where(uid => !eventUids.Contains(uid)).ToList();

        var set = new TrackingPatchSet(Clean: CleanFlags.IsAutoDelete(clean));
        var count = 0;

        if (ruleUids.Count > 0)
        {
            count += this.Count(type, await this._trackingProxy.PatchAsync(type, userId, ruleUids, set));
        }

        if (eventUids.Count > 0)
        {
            count += this.Count("incident", await this._incidentProxy.PatchAsync(userId, eventUids, set));
        }

        return count;
    }

    private int Count(string type, TrackingPatchResult result)
    {
        foreach (var refusal in result.Refused)
        {
            LogCleanRefused(this._logger, type, refusal.Uid, refusal.Reason, refusal.Detail);
        }

        return result.Updated.Count + result.Unchanged.Count;
    }

    private static List<int> UidsOf(IEnumerable<JsonElement> rows) =>
        [.. rows.Select(PoracleJsonHelper.UidOf).OfType<int>().Where(uid => uid > 0)];

    private static bool IsEventRow(JsonElement row) => PokestopEventTypes.IsEventName(
        row.TryGetProperty("grunt_type", out var gt) && gt.ValueKind == JsonValueKind.String ? gt.GetString() : null);

    /// <summary>
    /// Counts the rules in a tracking array, and those with the auto-delete bit set. A missing array is
    /// zero of zero. A <c>clean</c> that is absent, null, false or not a number counts as not clean.
    /// </summary>
    private static CleanCount CountClean(JsonElement root, string key, Func<JsonElement, bool>? isOurs = null)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return new CleanCount(0, 0);
        }

        var total = 0;
        var clean = 0;

        foreach (var item in arr.EnumerateArray())
        {
            if (isOurs != null && !isOurs(item))
            {
                continue;
            }

            total++;

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("clean", out var cleanVal)
                && cleanVal.ValueKind == JsonValueKind.Number
                && cleanVal.TryGetInt32(out var bits)
                && CleanFlags.IsAutoDelete(bits))
            {
                clean++;
            }
        }

        return new CleanCount(clean, total);
    }

    // Warning: the count the user sees already leaves this rule out, so nothing on screen says it kept its old
    // setting. This line is the only place that does.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Clean toggle {Type}: PoracleNG refused uid={Uid} ({Reason}: {Detail}); it keeps its old setting.")]
    private static partial void LogCleanRefused(ILogger logger, string type, int uid, string reason, string detail);
}
