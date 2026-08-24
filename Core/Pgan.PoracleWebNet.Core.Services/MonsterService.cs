using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class MonsterService(
    IPoracleTrackingProxy proxy,
    IFeatureGate featureGate,
    ITrackedUidRemapper uidRemapper) : IMonsterService
{
    private const string TrackingType = "pokemon";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;

    // profileNo is kept for interface compatibility only. PoracleNG scopes reads to the user's active
    // profile (humans.current_profile_no), and writes no longer carry profile_no at all — see
    // PoracleJsonHelper.SerializeToElement. The previous claim here, that the JWT profileNo and the
    // active profile can never diverge, was wrong: the active-hours scheduler and the bot's !profile
    // command both move it out of band, and JWTs live four hours. See #411.
    public async Task<IEnumerable<Monster>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var monsters = DeserializeMonsters(json);
        return monsters;
    }

    public async Task<Monster?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var monsters = DeserializeMonsters(json);
        return monsters.FirstOrDefault(m => m.Uid == uid);
    }

    public async Task<Monster> CreateAsync(string userId, Monster model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Pokemon);
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

    public async Task<Monster> UpdateAsync(string userId, Monster model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Pokemon);
        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730. This
        // matters MORE on the v2 path than it did on v1: the v2 PUT is a full replace, so a filter left
        // out of the body is reset to its default rather than left alone -- verified live against 5.2.1,
        // where omitting min_iv wiped a stored 90.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        // Pokemon is the one type with no collision guard on the v1 update path: PoracleNG updates it in
        // place rather than merging, so this call early-returns for pokemon edits (#606). It stays because
        // it is the guard for the create path in CreateAsync, and because deleting it here would make the
        // two paths look different for no reason. On v2 the guard is upstream's: the uid-addressed PUT
        // answers 409 rather than taking another rule over.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        var result = await this._proxy.UpdateByUidAsync(TrackingType, userId, model.Uid, body);

        // Pokemon used to be the one type whose uid survived an edit. The v2 PUT is delete-then-insert, so
        // it now rotates like the other nine, and quick-pick applied state has to follow the row or its
        // "remove" button silently deletes nothing. See #403 and #805.
        if (result.Uid > 0 && result.Uid != model.Uid)
        {
            await this._uidRemapper.RemapAsync(userId, TrackingType, model.Uid, result.Uid);
            model.Uid = result.Uid;
        }

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
        var monsters = DeserializeMonsters(json);
        var uids = monsters.Select(m => m.Uid).ToList();

        if (uids.Count == 0)
        {
            return 0;
        }

        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);
        return uids.Count;
    }

    // Fetch-modify-POST workaround: not atomic. Concurrent distance updates from the same user
    // could race, but this is acceptable — the last write wins and distance is a single scalar.
    // See: docs/poracleng-enhancement-requests.md#bulk-distance-update
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

        await this._proxy.CreateAsync(TrackingType, userId, body);
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

        await this._proxy.CreateAsync(TrackingType, userId, body);
        return count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var monsters = DeserializeMonsters(json);
        return monsters.Count;
    }

    public async Task<IEnumerable<Monster>> BulkCreateAsync(string userId, IEnumerable<Monster> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Pokemon);
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

    private static List<Monster> DeserializeMonsters(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Monster>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
