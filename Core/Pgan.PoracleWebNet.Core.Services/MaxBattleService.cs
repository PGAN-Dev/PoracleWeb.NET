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

        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        // PoracleNG 5.4.0 answers an exact repeat as "already present" and writes nothing; say so in the
        // words the SPA has always shown rather than returning an alarm that has no uid. See #521.
        if (result.AlreadyPresent > 0 && result.Inserts == 0)
        {
            throw new TrackingConflictException(
                TrackingType,
                "You already have an identical max battle alarm.");
        }

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<MaxBattle> UpdateAsync(string userId, MaxBattle model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.MaxBattles);
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        model.Uid = await TrackingV2Replacement.ApplyAsync(
            this._proxy, TrackingType, userId, oldUid, body, this._uidRemapper);

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

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var (body, skipped) = DistanceRewrite.Build(json, _ => true, distance);
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return skipped;
        }

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        // If the re-create fails after delete, alarms are lost. Log for recovery.
        var uids = body.EnumerateArray().Select(PoracleJsonHelper.UidOf).OfType<int>().ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);

        try
        {
            await this._proxy.UpsertV1Async(TrackingType, userId, body);

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

        return skipped with { Updated = count };
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        // The stored rows are rewritten in place rather than round-tripped through the typed model,
        // so fields PoracleWeb does not model survive the write-back. See #730.
        var selected = new HashSet<int>(uids);
        var (body, skipped) = DistanceRewrite.Build(
            json,
            row => PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            distance);
        var count = body.GetArrayLength();

        if (count == 0)
        {
            return skipped;
        }

        // MaxBattle is insert-only — bulk delete then re-create with updated distance.
        var matchingUids = body.EnumerateArray().Select(PoracleJsonHelper.UidOf).OfType<int>().ToList();
        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, matchingUids);

        try
        {
            await this._proxy.UpsertV1Async(TrackingType, userId, body);

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

        return skipped with { Updated = count };
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

        for (var i = 0; i < modelList.Count && i < result.RowUids.Count; i++)
        {
            modelList[i].Uid = result.RowUids[i];
        }

        return modelList;
    }

    private static List<MaxBattle> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<MaxBattle>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to re-create {Count} maxbattle alarms after bulk delete for user {UserId}. Alarms with UIDs [{Uids}] were deleted but not re-created")]
    private static partial void LogRecreateFailed(ILogger logger, Exception ex, int count, string userId, string uids);
}
