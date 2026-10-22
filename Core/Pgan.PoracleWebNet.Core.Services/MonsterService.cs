using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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

        var result = await this._proxy.UpdateByUidAsync(TrackingType, userId, model.Uid, body);

        // Pokemon used to be the one type whose uid survived an edit. The v2 PUT is delete-then-insert, so
        // on 5.2.x it rotates like the other nine, and quick-pick applied state has to follow the row or its
        // "remove" button silently deletes nothing. Since jfberry/PoracleNG #303 the PUT keeps the uid, but
        // a failed reassignment still returns a new one, so the result is read and compared every time.
        // See #403 and #805.
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

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);

        // One v2 PATCH naming the uids and the radius: nothing else a rule holds is re-sent (#730), each keeps
        // its uid, and PoracleNG refuses a rule that would collide with another rather than merging it.
        return await DistanceRewrite.ApplyAsync(this._proxy, TrackingType, userId, json, _ => true, distance, NullLogger.Instance);
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var selected = new HashSet<int>(uids);

        return await DistanceRewrite.ApplyAsync(
            this._proxy,
            TrackingType,
            userId,
            json,
            row => PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            distance,
            NullLogger.Instance);
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

        for (var i = 0; i < modelList.Count && i < result.RowUids.Count; i++)
        {
            modelList[i].Uid = result.RowUids[i];
        }

        return modelList;
    }

    private static List<Monster> DeserializeMonsters(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Monster>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
