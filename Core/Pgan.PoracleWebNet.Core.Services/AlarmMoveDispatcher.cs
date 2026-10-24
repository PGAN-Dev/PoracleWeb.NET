using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>Picks the alarm service for a tracking type and hands it to the <see cref="AlarmMover"/>.</summary>
public class AlarmMoveDispatcher
{
    private readonly Dictionary<string, Func<string, int, IReadOnlyList<int>, LinkSide, string?, Task<IReadOnlyList<MoveOutcome>>>> _byType;

    public AlarmMoveDispatcher(
        AlarmMover mover,
        IMonsterService monsters,
        IRaidService raids,
        IEggService eggs,
        IQuestService quests,
        IInvasionService invasions,
        ILureService lures,
        INestService nests,
        IGymService gyms,
        IMaxBattleService maxBattles,
        IFortChangeService fortChanges,
        IPokestopEventService pokestopEvents) =>
        this._byType = new(StringComparer.Ordinal)
        {
            ["pokemon"] = (user, profile, uids, side, _) => mover.MoveManyAsync(monsters, "pokemon", user, profile, uids, side),
            ["raid"] = (user, profile, uids, side, _) => mover.MoveManyAsync(raids, "raid", user, profile, uids, side),
            ["egg"] = (user, profile, uids, side, _) => mover.MoveManyAsync(eggs, "egg", user, profile, uids, side),
            ["quest"] = (user, profile, uids, side, _) => mover.MoveManyAsync(quests, "quest", user, profile, uids, side),
            ["invasion"] = (user, profile, uids, side, kind) => mover.MoveManyAsync(invasions, "invasion", user, profile, uids, side, kind),
            ["lure"] = (user, profile, uids, side, _) => mover.MoveManyAsync(lures, "lure", user, profile, uids, side),
            ["nest"] = (user, profile, uids, side, _) => mover.MoveManyAsync(nests, "nest", user, profile, uids, side),
            ["gym"] = (user, profile, uids, side, _) => mover.MoveManyAsync(gyms, "gym", user, profile, uids, side),
            ["maxbattle"] = (user, profile, uids, side, _) => mover.MoveManyAsync(maxBattles, "maxbattle", user, profile, uids, side),
            ["fort"] = (user, profile, uids, side, _) => mover.MoveManyAsync(fortChanges, "fort", user, profile, uids, side),
            ["incident"] = (user, profile, uids, side, _) => mover.MoveManyAsync(pokestopEvents, "incident", user, profile, uids, side),
        };

    public bool Supports(string? trackingType) => trackingType is not null && this._byType.ContainsKey(trackingType);

    public Task<IReadOnlyList<MoveOutcome>> MoveAsync(string trackingType, string userId, int profileNo, IReadOnlyList<int> uids, LinkSide target, string? kind = null) =>
        this._byType[trackingType](userId, profileNo, uids, target, kind);
}
