using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Manages the "clean" flag on tracking alarms via the PoracleNG REST API proxy.
/// </summary>
public class CleaningService(
    IPoracleTrackingProxy trackingProxy,
    IFeatureGate featureGate,
    ITrackedUidRemapper uidRemapper,
    ILogger<CleaningService> logger) : ICleaningService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;
    private readonly ILogger<CleaningService> _logger = logger;

    /// <summary>
    /// Tracking types whose PoracleNG create only ever inserts, so a re-POST duplicates rather than
    /// updates. Everything else upserts on <c>uid</c>.
    /// </summary>
    private static readonly HashSet<string> InsertOnlyTypes = new(StringComparer.Ordinal) { "maxbattle" };

    public async Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo)
    {
        var allTracking = await this._trackingProxy.GetAllTrackingAsync(userId);

        return new Dictionary<string, bool>
        {
            ["monsters"] = AllClean(allTracking, "pokemon"),
            ["raids"] = AllClean(allTracking, "raid"),
            ["eggs"] = AllClean(allTracking, "egg"),
            ["quests"] = AllClean(allTracking, "quest"),
            ["invasions"] = AllClean(allTracking, "invasion"),
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
    /// Workaround: PoracleNG has no bulk clean toggle endpoint. We fetch all alarms of the type,
    /// set the clean field on each, and POST them back via CreateAsync (which upserts by UID).
    /// This is expensive for users with many alarms but functional until a dedicated bulk clean
    /// endpoint is added to PoracleNG. See: docs/poracleng-enhancement-requests.md#bulk-clean-toggle
    ///
    /// Known limitation: fetch-modify-POST is not atomic. Concurrent requests from the same user
    /// could race, with the last POST winning. Acceptable because clean toggle is infrequent and
    /// idempotent (setting clean=1 twice produces the same result).
    /// </summary>
    private async Task<int> ToggleCleanAsync(string type, string userId, int clean)
    {
        // Cleaning is implemented as a fetch-modify-POST that ultimately calls
        // _trackingProxy.CreateAsync, bypassing the per-type alarm services and their feature gates.
        // Without this guard a user could toggle the clean flag on an alarm type the admin disabled. (#236)
        if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var disableKey))
        {
            await this._featureGate.EnsureEnabledAsync(disableKey);
        }

        var trackingJson = await this._trackingProxy.GetByUserAsync(type, userId);

        if (trackingJson.ValueKind != JsonValueKind.Array || trackingJson.GetArrayLength() == 0)
        {
            return 0;
        }

        var count = trackingJson.GetArrayLength();
        var updatedAlarms = new JsonArray();

        foreach (var alarm in trackingJson.EnumerateArray())
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(alarm.GetRawText())!;

            // The clean toggle only owns the auto-delete bit (bit 1). Read-modify-write so any
            // bot-set edit-in-place (bit 2) / summary (bit 4) bits survive the bulk toggle. (#292)
            var existing = dict.TryGetValue("clean", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            dict["clean"] = JsonSerializer.SerializeToElement(CleanFlags.Preserve(existing, CleanFlags.AutoDelete, clean));
            updatedAlarms.Add(JsonSerializer.SerializeToNode(dict));
        }

        var body = JsonSerializer.SerializeToElement(updatedAlarms);

        // PoracleNG's maxbattle create is insert-only -- it has no upsert path -- so re-POSTing the
        // modified set inserted a full duplicate of every alarm and left the originals untouched.
        // One click per duplicate set, unbounded. Free the rows first for that type only; the others
        // upsert on uid and must not be deleted.
        if (InsertOnlyTypes.Contains(type))
        {
            var uids = trackingJson.EnumerateArray()
                .Where(a => a.TryGetProperty("uid", out var u) && u.ValueKind == JsonValueKind.Number)
                .Select(a => a.GetProperty("uid").GetInt32())
                .ToList();

            await this._trackingProxy.BulkDeleteByUidsAsync(type, userId, uids);

            try
            {
                await this._trackingProxy.CreateAsync(type, userId, body);
            }
            catch
            {
                // Put the originals back rather than leaving the user with no alarms at all.
                await this._trackingProxy.CreateAsync(type, userId, trackingJson);
                throw;
            }

        // Every non-monster type comes back under a new uid, so any quick pick tracking these rows
        // would be left pointing at dead uids -- Remove then deletes nothing and the next page load
        // wipes the applied state. This was the one bulk-repost path that never got the remapper the
        // distance endpoints already use. "clean" is the field being mutated, so it cannot take part
        // in row identity. See #471.
        await BulkUidRemap.ApplyAsync(
            this._trackingProxy, type, userId, body, this._uidRemapper, this._logger, ["clean"]);

            return count;
        }

        await this._trackingProxy.CreateAsync(type, userId, body);

        // Every non-monster type comes back under a new uid, so any quick pick tracking these rows
        // would be left pointing at dead uids -- Remove then deletes nothing and the next page load
        // wipes the applied state. This was the one bulk-repost path that never got the remapper the
        // distance endpoints already use. "clean" is the field being mutated, so it cannot take part
        // in row identity. See #471.
        await BulkUidRemap.ApplyAsync(
            this._trackingProxy, type, userId, body, this._uidRemapper, this._logger, ["clean"]);

        return count;
    }

    /// <summary>
    /// Checks whether all items in a tracking array have clean == true or clean == 1.
    /// Returns false if the array is empty or missing.
    /// </summary>
    private static bool AllClean(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (var item in arr.EnumerateArray())
        {
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

        return true;
    }
}
