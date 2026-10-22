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
        var allTracking = await this._trackingProxy.GetAllTrackingAsync(userId);

        return new Dictionary<string, bool>
        {
            ["monsters"] = AllClean(allTracking, "pokemon"),
            ["raids"] = AllClean(allTracking, "raid"),
            ["eggs"] = AllClean(allTracking, "egg"),
            ["quests"] = AllClean(allTracking, "quest"),
            ["invasions"] = AllClean(allTracking, "invasion", await this.OwnRowPredicateAsync("invasion")),
            ["lures"] = AllClean(allTracking, "lure"),
            ["nests"] = AllClean(allTracking, "nest"),
            ["gyms"] = AllClean(allTracking, "gym"),
            ["maxbattles"] = AllClean(allTracking, "maxbattle"),
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
    /// Checks whether all items in a tracking array have clean == true or clean == 1.
    /// Returns false if the array is empty or missing.
    /// </summary>
    private static bool AllClean(JsonElement root, string key, Func<JsonElement, bool>? isOurs = null)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        {
            return false;
        }

        var any = false;

        foreach (var item in arr.EnumerateArray())
        {
            if (isOurs != null && !isOurs(item))
            {
                continue;
            }

            any = true;

            if (!item.TryGetProperty("clean", out var cleanVal))
            {
                return false;
            }

            var isClean = cleanVal.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => CleanFlags.IsAutoDelete(cleanVal.GetInt32()),
                JsonValueKind.Undefined => throw new NotImplementedException(),
                JsonValueKind.Object => throw new NotImplementedException(),
                JsonValueKind.Array => throw new NotImplementedException(),
                JsonValueKind.String => throw new NotImplementedException(),
                JsonValueKind.False => throw new NotImplementedException(),
                JsonValueKind.Null => throw new NotImplementedException(),
                _ => false,
            };

            if (!isClean)
            {
                return false;
            }
        }

        // An array holding nothing but somebody else's rows is the empty case, not the all-clean one.
        return any;
    }

    // Warning: the count the user sees already leaves this rule out, so nothing on screen says it kept its old
    // setting. This line is the only place that does.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Clean toggle {Type}: PoracleNG refused uid={Uid} ({Reason}: {Detail}); it keeps its old setting.")]
    private static partial void LogCleanRefused(ILogger logger, string type, int uid, string reason, string detail);
}
