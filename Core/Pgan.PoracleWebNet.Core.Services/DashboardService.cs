using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class DashboardService(IPoracleTrackingProxy trackingProxy) : IDashboardService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;

    public async Task<DashboardCounts> GetCountsAsync(string userId, int profileNo)
    {
        var allTracking = await this._trackingProxy.GetAllTrackingAsync(userId);

        return new DashboardCounts
        {
            Monsters = CountArray(allTracking, "pokemon"),
            Raids = CountArray(allTracking, "raid"),
            Eggs = CountArray(allTracking, "egg"),
            Quests = CountArray(allTracking, "quest"),
            Invasions = CountArray(allTracking, "invasion"),
            Lures = CountArray(allTracking, "lure"),
            Nests = CountArray(allTracking, "nest"),
            Gyms = CountArray(allTracking, "gym"),
            FortChanges = CountArray(allTracking, "fort"),
        };
    }

    private static int CountArray(JsonElement root, string key) =>
        root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.GetArrayLength()
            : 0;
}
