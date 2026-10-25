using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class DashboardService(IPoracleTrackingProxy trackingProxy, IFeatureGate featureGate, ILinkedAccount linked) : IDashboardService
{
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILinkedAccount _linked = linked;

    public async Task<DashboardCounts> GetCountsAsync(string userId, int profileNo)
    {
        // A linked person's cards add both humans' rules together; an unlinked caller makes the one read.
        var pair = await this._linked.ResolveAsync(userId);

        // The proxy folds v2's incident rules into the snapshot's "invasion" array, as v1 held them, so
        // that array carries pokestop-event rows too.
        // Split them the same way the two lists do, and on the same condition: when the Pokestop
        // Events page is unavailable those rows are still shown under Invasions, so the card has to
        // agree with the page it links to. See InvasionService.EventsLiveElsewhereAsync.
        var eventsHaveTheirOwnPage = await this._featureGate.IsEnabledAsync(DisableFeatureKeys.PokestopEvents);

        var counts = new DashboardCounts();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            var allTracking = await this._trackingProxy.GetAllTrackingAsync(humanId);
            var (invasions, pokestopEvents) = SplitInvasions(allTracking, eventsHaveTheirOwnPage);

            counts.Monsters += CountArray(allTracking, "pokemon");
            counts.Raids += CountArray(allTracking, "raid");
            counts.Eggs += CountArray(allTracking, "egg");
            counts.Quests += CountArray(allTracking, "quest");
            counts.Invasions += invasions;
            counts.Lures += CountArray(allTracking, "lure");
            counts.Nests += CountArray(allTracking, "nest");
            counts.Gyms += CountArray(allTracking, "gym");
            counts.FortChanges += CountArray(allTracking, "fort");
            counts.MaxBattles += CountArray(allTracking, "maxbattle");
            counts.PokestopEvents += pokestopEvents;
        }

        return counts;
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
