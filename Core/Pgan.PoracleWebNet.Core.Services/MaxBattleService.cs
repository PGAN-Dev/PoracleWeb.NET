using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class MaxBattleService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<MaxBattleService> logger, ITrackedUidRemapper uidRemapper) : IMaxBattleService
{
    private const string TrackingType = "maxbattle";
    private readonly ILogger<MaxBattleService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<IEnumerable<MaxBattle>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<MaxBattle?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<MaxBattle> CreateAsync(string userId, MaxBattle model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        model.Id = userId;

        // Max battles are insert-only upstream: PoracleNG dedups every other type and this one not at all,
        // so pressing Add twice stacked identical alarms forever and the user got two of every
        // notification, with no way to tell the rows apart in the list. An exact duplicate is refused
        // rather than filed again. Alarms that differ in any field -- including radius -- still stack,
        // because upstream has no key that would merge them. See #521.
        var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
        if (siblings.Any(x => IsSameAlarm(x, model)))
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have an identical max battle alarm.");
        }

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<MaxBattle> UpdateAsync(string userId, MaxBattle model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        // MaxBattle is insert-only in PoracleNG (no dedup/upsert).
        // Delete the old alarm first, then create a replacement.
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        // Create refuses an exact duplicate (#521) and this path did not, so editing one max battle onto
        // another's settings left two identical rows -- and it is checked BEFORE the delete, because the
        // delete is what makes this path destructive. See #538.
        var siblings = await this.GetByUserAsync(userId, model.ProfileNo);
        if (siblings.Any(x => x.Uid != oldUid && IsSameAlarm(x, model)))
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have an identical max battle alarm.");
        }

        await this._proxy.DeleteByUidAsync(TrackingType, userId, oldUid);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        // Quick-pick applied state stores uids captured at apply time; follow the row. See #403.
        await this._uidRemapper.RemapAsync(userId, TrackingType, oldUid, model.Uid);

        return model;
    }

    public async Task<bool> DeleteAsync(string userId, int uid)
    {
        await this._proxy.DeleteByUidAsync(TrackingType, userId, uid);
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        var uids = items.Select(x => x.Uid).ToList();

        if (uids.Count == 0)
        {
            return 0;
        }

        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);
        return uids.Count;
    }

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var body = PoracleJsonHelper.RewriteRows(json, _ => true, ("distance", distance));
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return 0;
        }

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        // If the re-create fails after delete, alarms are lost. Log for recovery.
        var uids = body.EnumerateArray().Select(PoracleJsonHelper.UidOf).OfType<int>().ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);

        try
        {
            await this._proxy.CreateAsync(TrackingType, userId, body);

            // The rows were deleted and re-made, so every uid changed. Follow any quick pick that
            // tracks them, pairing on content because the batch response is reordered. See #443.
            await BulkUidRemap.ApplyAsync(
                this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);
        }
        catch (Exception ex)
        {
            LogRecreateFailed(this._logger, ex,
                count, userId, string.Join(", ", uids));
            throw;
        }

        return count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var selected = new HashSet<int>(uids);
        var body = PoracleJsonHelper.RewriteRows(
            json,
            row => PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            ("distance", distance));
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return 0;
        }

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        var matchingUids = body.EnumerateArray().Select(PoracleJsonHelper.UidOf).OfType<int>().ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, matchingUids);

        try
        {
            await this._proxy.CreateAsync(TrackingType, userId, body);

            // The rows were deleted and re-made, so every uid changed. Follow any quick pick that
            // tracks them, pairing on content because the batch response is reordered. See #443.
            await BulkUidRemap.ApplyAsync(
                this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);
        }
        catch (Exception ex)
        {
            LogRecreateFailed(this._logger, ex,
                count, userId, string.Join(", ", matchingUids));
            throw;
        }

        return count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.Count;
    }

    public async Task<IEnumerable<MaxBattle>> BulkCreateAsync(string userId, IEnumerable<MaxBattle> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        var modelList = models.ToList();

        foreach (var model in modelList)
        {
            model.Id = userId;
        }

        var body = SerializeToElement(modelList);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        for (var i = 0; i < modelList.Count && i < result.NewUids.Count; i++)
        {
            modelList[i].Uid = (int)result.NewUids[i];
        }

        return modelList;
    }

    /// <summary>
    /// Whether two max battle alarms are the same alarm: every field a user can set, radius included.
    /// </summary>
    /// <remarks>
    /// Deliberately strict. Upstream has no key that would merge two alarms differing by radius, so
    /// refusing those would block something that does work. Only an exact repeat is refused. See #521.
    /// </remarks>
    private static bool IsSameAlarm(MaxBattle existing, MaxBattle candidate) =>
        existing.PokemonId == candidate.PokemonId
        && existing.Form == candidate.Form
        && StoredLevel(existing) == StoredLevel(candidate)
        && existing.Move == candidate.Move
        && existing.Gmax == candidate.Gmax
        && existing.Evolution == candidate.Evolution
        && existing.Distance == candidate.Distance
        && string.Equals(existing.StationId ?? string.Empty, candidate.StationId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The level PoracleNG will actually store: it forces 9000 unless the alarm tracks any boss.
    /// </summary>
    /// <remarks>
    /// Comparing the submitted level instead made every duplicate look different, because the stored row
    /// already carried the rewritten value. Mirrors trackingMaxbattle.go, which sets level = 9000 whenever
    /// pokemon_id names a specific boss.
    /// </remarks>
    private static int StoredLevel(MaxBattle alarm) => alarm.PokemonId == 9000 ? alarm.Level : 9000;

    private static List<MaxBattle> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<MaxBattle>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to re-create {Count} maxbattle alarms after bulk delete for user {UserId}. Alarms with UIDs [{Uids}] were deleted but not re-created")]
    private static partial void LogRecreateFailed(ILogger logger, Exception ex, int count, string userId, string uids);
}
