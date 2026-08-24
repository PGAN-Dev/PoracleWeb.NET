using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class DashboardService(IPoracleTrackingProxy trackingProxy, IFeatureGate featureGate) : IDashboardService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<DashboardCounts> GetCountsAsync(string userId, int profileNo)
    {
        var allTracking = await this._trackingProxy.GetAllTrackingAsync(userId);

        // The all-tracking snapshot is v1, so its "invasion" array carries pokestop-event rows too.
        // Split them the same way the two lists do, and on the same condition: when the Pokestop
        // Events page is unavailable those rows are still shown under Invasions, so the card has to
        // agree with the page it links to. See InvasionService.EventsLiveElsewhereAsync.
        var eventsHaveTheirOwnPage = await this._featureGate.IsEnabledAsync(DisableFeatureKeys.PokestopEvents);
        var (invasions, pokestopEvents) = SplitInvasions(allTracking, eventsHaveTheirOwnPage);

        return new DashboardCounts
        {
            Monsters = CountArray(allTracking, "pokemon"),
            Raids = CountArray(allTracking, "raid"),
            Eggs = CountArray(allTracking, "egg"),
            Quests = CountArray(allTracking, "quest"),
            Invasions = invasions,
            Lures = CountArray(allTracking, "lure"),
            Nests = CountArray(allTracking, "nest"),
            Gyms = CountArray(allTracking, "gym"),
            FortChanges = CountArray(allTracking, "fort"),
            MaxBattles = CountArray(allTracking, "maxbattle"),
            PokestopEvents = pokestopEvents,
        };
    }

    private static (int Invasions, int PokestopEvents) SplitInvasions(JsonElement root, bool separate)
    {
        if (!root.TryGetProperty("invasion", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return (0, 0);
        }

        if (!separate)
        {
            return (arr.GetArrayLength(), 0);
        }

        var events = arr.EnumerateArray().Count(row =>
            row.TryGetProperty("grunt_type", out var gt)
            && gt.ValueKind == JsonValueKind.String
            && PokestopEventTypes.IsEventName(gt.GetString()));

        return (arr.GetArrayLength() - events, events);
    }

    private static int CountArray(JsonElement root, string key) =>
        root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.GetArrayLength()
            : 0;
}
