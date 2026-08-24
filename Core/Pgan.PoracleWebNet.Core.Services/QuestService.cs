using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class QuestService(
    IPoracleTrackingProxy proxy,
    IFeatureGate featureGate,
    IQuestPokecoinCapabilityService pokecoinCapability,
    ILogger<QuestService> logger,
    ITrackedUidRemapper uidRemapper) : IQuestService
{
    private const string TrackingType = "quest";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IQuestPokecoinCapabilityService _pokecoinCapability = pokecoinCapability;
    private readonly ILogger<QuestService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    public async Task<IEnumerable<Quest>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Quest?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Quest> CreateAsync(string userId, Quest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
        await this.EnsureRewardTypeSupportedAsync(model.RewardType);
        model.Id = userId;

        // An Add that PoracleNG resolves into an update of an existing alarm takes that alarm over:
        // 201 Created, and the user quietly loses the one they had. See #561.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, 0, SerializeToElement(model));
        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Quest> UpdateAsync(string userId, Quest model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
        await this.EnsureRewardTypeSupportedAsync(model.RewardType);
        var oldUid = model.Uid;
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        // Refuse before writing: PoracleNG would satisfy this by merging into the other alarm and
        // the reconciler would then delete this one, losing a row the user never touched. See #531.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, oldUid, body);

        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        // PoracleNG inserts instead of upserting when the edit changes a dedup-key field,
        // leaving the pre-edit row behind as a duplicate. Drop it and report the surviving uid.
        model.Uid = await TrackingUpdateReconciler.ReconcileAsync(
            this._proxy, TrackingType, userId, oldUid, result, this._logger, body, this._uidRemapper);

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
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        // And against the rows NOT selected: at the new radius a selected row can differ from an
        // unselected sibling by exactly one updatable field, and PoracleNG then rewrites the SIBLING
        // -- an alarm the user never touched -- while the selected one keeps its old radius and the
        // response claims it was updated. See #598.
        await TrackingUpdateReconciler.EnsureBatchDoesNotTakeOverOthersAsync(
            this._proxy, TrackingType, userId, body);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

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
        // Two selected rows that differed only by radius become the same alarm once both are set to
        // the same one, and PoracleNG resolves that inside the batch -- fewer alarms than selected,
        // one left at its old radius, and a response claiming all were updated. See #580.
        TrackingUpdateReconciler.EnsureBatchDoesNotCollapse(body, TrackingType);

        // And against the rows NOT selected: at the new radius a selected row can differ from an
        // unselected sibling by exactly one updatable field, and PoracleNG then rewrites the SIBLING
        // -- an alarm the user never touched -- while the selected one keeps its old radius and the
        // response claims it was updated. See #598.
        await TrackingUpdateReconciler.EnsureBatchDoesNotTakeOverOthersAsync(
            this._proxy, TrackingType, userId, body);

        await this._proxy.CreateAsync(TrackingType, userId, body);
        // PoracleNG rewrites every row, so the uids change. Follow any quick-pick that
        // tracks them, pairing on content because the batch response is reordered. See #443.
        await BulkUidRemap.ApplyAsync(
            this._proxy, TrackingType, userId, body, this._uidRemapper, this._logger);

        return count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.Count;
    }

    public async Task<IEnumerable<Quest>> BulkCreateAsync(string userId, IEnumerable<Quest> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Quests);
        var modelList = models.ToList();

        // Checked for every row, not just the first: profile import and quick-pick apply both arrive
        // here with a heterogeneous batch, and PoracleNG refuses the whole POST if any row is bad.
        foreach (var rewardType in modelList.Select(m => m.RewardType).Distinct())
        {
            await this.EnsureRewardTypeSupportedAsync(rewardType);
        }

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
    /// Refuses a reward type this PoracleNG cannot store, before anything is written.
    /// </summary>
    /// <remarks>
    /// Pokecoins is the only gated type. PoracleNG below 5.2.0 answers 400 "Unrecognised reward_type
    /// value", which reaches the user as a generic failure naming neither cause nor fix; this says which
    /// version would be needed. It lives in the service rather than the controller so quick-pick apply
    /// and profile import are covered too -- both reach <c>BulkCreateAsync</c> without passing a quest
    /// action. See #565 for that shape.
    ///
    /// Reads and deletes are deliberately NOT gated: a pokecoin rule can exist already, set with the bot
    /// or left behind by a downgrade, and a row nobody can see is a row nobody can remove.
    /// </remarks>
    private async Task EnsureRewardTypeSupportedAsync(int rewardType)
    {
        if (rewardType != QuestRewardTypes.Pokecoins)
        {
            return;
        }

        if (await this._pokecoinCapability.ArePokecoinRewardsSupportedAsync())
        {
            return;
        }

        throw new AlarmValidationException(
            "This Poracle server does not support PokeCoin quest rewards. PoracleNG 5.2.0 or newer is required.");
    }

    private static List<Quest> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Quest>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
