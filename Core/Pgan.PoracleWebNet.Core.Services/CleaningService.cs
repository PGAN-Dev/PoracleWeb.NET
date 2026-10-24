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
    ILinkedAccount linked,
    ILogger<CleaningService> logger) : ICleaningService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IPoracleIncidentProxy _incidentProxy = incidentProxy;
    private readonly ILinkedAccount _linked = linked;
    private readonly ILogger<CleaningService> _logger = logger;

    public async Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo)
    {
        var counts = await this.GetCleanCountsAsync(userId, profileNo);
        return counts.ToDictionary(kv => kv.Key, kv => kv.Value.IsAllClean());
    }

    public async Task<Dictionary<string, CleanCount>> GetCleanCountsAsync(string userId, int profileNo)
    {
        // A linked person's counts are both humans' added together, so a type reads clean only when every
        // rule on both sides is. An unlinked caller has one id and makes the one call they always made.
        var pair = await this._linked.ResolveAsync(userId);
        var isOwnInvasion = await this.OwnRowPredicateAsync("invasion");
        var totals = new Dictionary<string, CleanCount>
        {
            ["monsters"] = new(0, 0),
            ["raids"] = new(0, 0),
            ["eggs"] = new(0, 0),
            ["quests"] = new(0, 0),
            ["invasions"] = new(0, 0),
            ["lures"] = new(0, 0),
            ["nests"] = new(0, 0),
            ["gyms"] = new(0, 0),
            ["maxbattles"] = new(0, 0),
        };

        foreach (var humanId in pair.ExistingHumanIds)
        {
            var allTracking = await this._trackingProxy.GetAllTrackingAsync(humanId);
            Add(totals, "monsters", CountClean(allTracking, "pokemon"));
            Add(totals, "raids", CountClean(allTracking, "raid"));
            Add(totals, "eggs", CountClean(allTracking, "egg"));
            Add(totals, "quests", CountClean(allTracking, "quest"));
            Add(totals, "invasions", CountClean(allTracking, "invasion", isOwnInvasion));
            Add(totals, "lures", CountClean(allTracking, "lure"));
            Add(totals, "nests", CountClean(allTracking, "nest"));
            Add(totals, "gyms", CountClean(allTracking, "gym"));
            Add(totals, "maxbattles", CountClean(allTracking, "maxbattle"));
        }

        return totals;
    }

    private static void Add(Dictionary<string, CleanCount> totals, string key, CleanCount more) =>
        totals[key] = new CleanCount(totals[key].Clean + more.Clean, totals[key].Total + more.Total);

    public async Task<CleanToggleResult> ToggleCleanMonstersAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("pokemon", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanRaidsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("raid", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanEggsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("egg", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanQuestsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("quest", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("invasion", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanLuresAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("lure", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanNestsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("nest", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanGymsAsync(string userId, int profileNo, int clean) =>
        await this.ToggleCleanAsync("gym", userId, clean);

    public async Task<CleanToggleResult> ToggleCleanMaxBattlesAsync(string userId, int profileNo, int clean) =>
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
    private async Task<CleanToggleResult> ToggleCleanAsync(string type, string userId, int clean)
    {
        // Cleaning writes through the proxy directly, bypassing the per-type alarm services and their
        // feature gates. Without this guard a user could toggle the clean flag on an alarm type the admin
        // disabled. (#236)
        if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var disableKey))
        {
            await this._featureGate.EnsureEnabledAsync(disableKey);
        }

        // A linked pair is toggled on both humans, the signed-in one first. Its failure is the answer; the other
        // failing keeps the first side's count and says so, the way PairedHumanWriter reports a partial write.
        var pair = await this._linked.ResolveAsync(userId);
        var ids = pair.ExistingHumanIds;
        var updated = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            if (i == 0)
            {
                updated += await this.ToggleCleanForAsync(type, ids[i], clean);
                continue;
            }

            try
            {
                updated += await this.ToggleCleanForAsync(type, ids[i], clean);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or FeatureDisabledException))
            {
                var side = pair.SideOf(ids[i]) ?? LinkSide.Telegram;
                LogOtherSideCleanFailed(this._logger, ex, type, LogSafe.Id(ids[i]), side);
                return new CleanToggleResult(updated, new PairWarning(side, PairedHumanWriter.ReasonFor(ex, side)));
            }
        }

        return new CleanToggleResult(updated);
    }

    private async Task<int> ToggleCleanForAsync(string type, string userId, int clean)
    {
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
    /// zero of zero. A <c>clean</c> of true, or a mask with the auto-delete bit, is clean; absent, null, false
    /// or a string is not.
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

            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("clean", out var cleanVal) && IsCleanValue(cleanVal))
            {
                clean++;
            }
        }

        return new CleanCount(clean, total);
    }

    /// <summary>A boolean true or a mask with the auto-delete bit is clean; anything else is not.</summary>
    private static bool IsCleanValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.True
        || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var bits) && CleanFlags.IsAutoDelete(bits));

    // Warning: the count the user sees already leaves this rule out, so nothing on screen says it kept its old
    // setting. This line is the only place that does.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Clean toggle {Type}: the linked account {HumanId} ({Side}) did not take it")]
    private static partial void LogOtherSideCleanFailed(ILogger logger, Exception ex, string type, string humanId, LinkSide side);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Clean toggle {Type}: PoracleNG refused uid={Uid} ({Reason}: {Detail}); it keeps its old setting.")]
    private static partial void LogCleanRefused(ILogger logger, string type, int uid, string reason, string detail);
}
