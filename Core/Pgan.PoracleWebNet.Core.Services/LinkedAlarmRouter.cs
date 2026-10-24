using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Sends an alarm call to the right human of a linked pair. A caller with no link gets exactly the call
/// they always got: one service call for their own id, nothing stamped. A linked caller reads both
/// humans' alarms as one list, and a write goes to whichever human owns the alarm.
/// </summary>
/// <remarks>Scoped: it remembers the pair it resolved so <see cref="IsPaused"/> needs no second read.</remarks>
public class LinkedAlarmRouter(ILinkedAccount linked, AlarmMover mover)
{
    private readonly ILinkedAccount _linked = linked;
    private readonly AlarmMover _mover = mover;
    private LinkedPair? _pair;

    public async Task<IReadOnlyList<TModel>> ListAsync<TModel>(IAlarmService<TModel> svc, string userId, int profileNo)
        where TModel : class, IHasDestination
    {
        var pair = await this.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return (await svc.GetByUserAsync(userId, profileNo)).ToList();
        }

        var all = new List<TModel>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            foreach (var model in await svc.GetByUserAsync(humanId, profileNo))
            {
                Stamp(pair, model, humanId);
                all.Add(model);
            }
        }

        return all;
    }

    /// <summary>The alarm and the human id that owns it, or null when neither human of the pair has that uid.</summary>
    public Task<(TModel Model, string OwnerId)?> FindAsync<TModel>(IAlarmService<TModel> svc, string userId, int uid)
        where TModel : class, IHasDestination =>
        this.FindAsync(userId, humanId => svc.GetByUidAsync(humanId, uid));

    /// <summary>
    /// The alarm <paramref name="lookup"/> finds on either human of the pair, and the human id that owns it. For a
    /// lookup narrower than a uid, such as an invasion uid of a stated kind.
    /// </summary>
    public async Task<(TModel Model, string OwnerId)?> FindAsync<TModel>(string userId, Func<string, Task<TModel?>> lookup)
        where TModel : class, IHasDestination
    {
        var pair = await this.ResolveAsync(userId);
        foreach (var humanId in pair.ExistingHumanIds)
        {
            var model = await lookup(humanId);
            if (model is not null)
            {
                Stamp(pair, model, humanId);
                return (model, humanId);
            }
        }

        return null;
    }

    public async Task<int> CountAsync<TModel>(IAlarmService<TModel> svc, string userId, int profileNo)
    {
        var pair = await this.ResolveAsync(userId);
        var total = 0;
        foreach (var humanId in pair.ExistingHumanIds)
        {
            total += await svc.CountByUserAsync(humanId, profileNo);
        }

        return total;
    }

    public async Task<int> DeleteAllAsync<TModel>(IAlarmService<TModel> svc, string userId, int profileNo)
    {
        var pair = await this.ResolveAsync(userId);
        var total = 0;
        foreach (var humanId in pair.ExistingHumanIds)
        {
            total += await svc.DeleteAllByUserAsync(humanId, profileNo);
        }

        return total;
    }

    public async Task<DistanceUpdateResult> UpdateDistanceAllAsync<TModel>(
        IAlarmService<TModel> svc, string userId, int profileNo, int distance)
    {
        var pair = await this.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return await svc.UpdateDistanceByUserAsync(userId, profileNo, distance);
        }

        var results = new List<DistanceUpdateResult>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            results.Add(await svc.UpdateDistanceByUserAsync(humanId, profileNo, distance));
        }

        return Merge(results);
    }

    /// <summary>
    /// Sends each uid to the human that owns it. A uid neither human has goes to the caller's own id, where the
    /// service skips it exactly as it does for an unlinked caller.
    /// </summary>
    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync<TModel>(
        IAlarmService<TModel> svc, string userId, int profileNo, List<int> uids, int distance)
        where TModel : class, IHasDestination
    {
        var pair = await this.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return await svc.UpdateDistanceByUidsAsync(uids, userId, distance);
        }

        // One list read per human, not one per uid: a service's GetByUidAsync reads the whole list itself.
        var ownerOf = new Dictionary<int, string>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            foreach (var model in await svc.GetByUserAsync(humanId, profileNo))
            {
                ownerOf.TryAdd(model.Uid, humanId);
            }
        }

        var byOwner = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var uid in uids)
        {
            var owner = ownerOf.GetValueOrDefault(uid, userId);
            if (!byOwner.TryGetValue(owner, out var group))
            {
                byOwner[owner] = group = [];
            }

            group.Add(uid);
        }

        var results = new List<DistanceUpdateResult>();
        foreach (var (owner, group) in byOwner)
        {
            results.Add(await svc.UpdateDistanceByUidsAsync(group, owner, distance));
        }

        return Merge(results);
    }

    /// <summary>
    /// The human id a create belongs to. Unlinked callers always get their own, whatever they asked for. With no
    /// destination named, the pair's default side is used, and the default is ignored while that side has no
    /// human yet: the alarm belongs to the caller.
    /// </summary>
    /// <exception cref="AlarmDestinationException">A side named by the caller has no human yet.</exception>
    public async Task<string> CreateTargetAsync(string userId, string? destination)
    {
        var pair = await this.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return userId;
        }

        LinkSide side;
        if (destination is null)
        {
            side = pair.Link!.DefaultDestination;
            if ((side == LinkSide.Discord ? pair.Discord : pair.Telegram) is not { Exists: true })
            {
                return userId;
            }
        }
        else if (!AccountLinkKeys.TryParse(destination, out side))
        {
            throw new AlarmDestinationException(400, "Destination must be discord or telegram.");
        }

        var target = side == LinkSide.Discord ? pair.Discord : pair.Telegram;
        if (target is not { Exists: true })
        {
            throw new AlarmDestinationException(409,
                side == LinkSide.Discord ? "Discord isn't set up yet." : "Telegram isn't set up yet.");
        }

        return target.HumanId;
    }

    /// <summary>
    /// The side an update asks to move its alarm to, or null for an ordinary edit: no destination, the alarm's
    /// own side, or a caller with no link.
    /// </summary>
    /// <exception cref="AlarmDestinationException">The destination is neither discord nor telegram.</exception>
    public async Task<LinkSide?> MoveTargetAsync(string userId, string? destination, string ownerId)
    {
        var pair = await this.ResolveAsync(userId);
        if (!pair.IsLinked || destination is null)
        {
            return null;
        }

        if (!AccountLinkKeys.TryParse(destination, out var side))
        {
            throw new AlarmDestinationException(400, "Destination must be discord or telegram.");
        }

        return pair.SideOf(ownerId) == side ? null : side;
    }

    /// <summary>Moves an updated alarm to the other human of the pair. See <see cref="AlarmMover"/>.</summary>
    public async Task<MoveResult<TModel>> MoveAsync<TModel>(
        IAlarmService<TModel> svc, string trackingType, string userId, TModel model, string ownerId, LinkSide target)
        where TModel : class, IHasDestination
    {
        var result = await this._mover.MoveAsync(svc, trackingType, userId, model, ownerId, target);
        if (result.Created is not null && this._pair?.HumanIdFor(target) is { } targetId)
        {
            this.Stamp(result.Created, targetId);
        }

        return result;
    }

    /// <summary>True when the human is one side of the resolved pair and that side is paused. Always false when unlinked.</summary>
    public bool IsPaused(string humanId)
    {
        var pair = this._pair;
        if (pair is null || !pair.IsLinked)
        {
            return false;
        }

        foreach (var side in new[] { pair.Discord, pair.Telegram })
        {
            if (side is not null && string.Equals(side.HumanId, humanId, StringComparison.Ordinal))
            {
                return side.Pause != PauseReason.None;
            }
        }

        return false;
    }

    /// <summary>Stamps a model PoracleNG just answered a create with. A linked create into a paused side is flagged.</summary>
    public void StampCreated<TModel>(TModel model, string ownerId) where TModel : class, IHasDestination
    {
        if (this._pair is not { IsLinked: true } pair)
        {
            return;
        }

        Stamp(pair, model, ownerId);
        if (this.IsPaused(ownerId))
        {
            model.PausedDestination = true;
        }
    }

    /// <summary>Stamps a model that was written to <paramref name="ownerId"/>. Leaves it alone when unlinked.</summary>
    public void Stamp<TModel>(TModel model, string ownerId) where TModel : class, IHasDestination
    {
        if (this._pair is { IsLinked: true } pair)
        {
            Stamp(pair, model, ownerId);
        }
    }

    private async Task<LinkedPair> ResolveAsync(string userId)
    {
        var pair = await this._linked.ResolveAsync(userId);
        this._pair = pair;
        return pair;
    }

    private static void Stamp(LinkedPair pair, IHasDestination model, string ownerId)
    {
        if (pair.IsLinked && pair.SideOf(ownerId) is { } side)
        {
            model.Destination = AccountLinkKeys.Wire(side);
        }
    }

    private static DistanceUpdateResult Merge(List<DistanceUpdateResult> results) => new(
        results.Sum(r => r.Updated),
        results.SelectMany(r => r.SkippedAreaScoped).ToList(),
        results.SelectMany(r => r.SkippedPlaceScoped).ToList(),
        results.SelectMany(r => r.SkippedConflict).ToList());
}
