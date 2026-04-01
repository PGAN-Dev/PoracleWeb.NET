using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Manages the "clean" flag on tracking alarms via the PoracleNG REST API proxy.
/// </summary>
public class CleaningService(IPoracleTrackingProxy trackingProxy) : ICleaningService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;

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
        };
    }

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("pokemon", userId, clean);

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("raid", userId, clean);

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("egg", userId, clean);

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("quest", userId, clean);

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("invasion", userId, clean);

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("lure", userId, clean);

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("nest", userId, clean);

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean) =>
        await ToggleCleanAsync("gym", userId, clean);

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
        var trackingJson = await this._trackingProxy.GetByUserAsync(type, userId);

        if (trackingJson.ValueKind != JsonValueKind.Array || trackingJson.GetArrayLength() == 0)
            return 0;

        var count = trackingJson.GetArrayLength();
        var updatedAlarms = new JsonArray();

        foreach (var alarm in trackingJson.EnumerateArray())
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(alarm.GetRawText())!;
            dict["clean"] = JsonSerializer.SerializeToElement(clean);
            updatedAlarms.Add(JsonSerializer.SerializeToNode(dict));
        }

        var body = JsonSerializer.SerializeToElement(updatedAlarms);
        await this._trackingProxy.CreateAsync(type, userId, body);

        return count;
    }

    /// <summary>
    /// Checks whether all items in a tracking array have clean == true or clean == 1.
    /// Returns false if the array is empty or missing.
    /// </summary>
    private static bool AllClean(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return false;

        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("clean", out var cleanVal))
                return false;

            var isClean = cleanVal.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => cleanVal.GetInt32() == 1,
                _ => false,
            };

            if (!isClean)
                return false;
        }

        return true;
    }
}
