using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class MonsterService(IPoracleTrackingProxy proxy, IFeatureGate featureGate) : IMonsterService
{
    private const string TrackingType = "pokemon";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;

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
        // PoracleNG's POST endpoint handles updates when the body includes a uid field.
        var body = SerializeToElement(model);

        // Pokemon is the one type with no collision guard: PoracleNG updates it in place rather than
        // merging, so an edit onto another alarm's exact settings wrote a byte-identical twin -- two rows
        // on the page that cannot be told apart and must each be deleted. Creating that state directly is
        // refused, so the edit path was the only way to reach it. See #537.
        await TrackingUpdateReconciler.EnsureNoMergeIntoAnotherAlarmAsync(
            this._proxy, TrackingType, userId, model.Uid, body);

        await this._proxy.CreateAsync(TrackingType, userId, body);
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
        var monsters = DeserializeMonsters(json);
        var monsterList = monsters.ToList();

        if (monsterList.Count == 0)
        {
            return 0;
        }

        foreach (var monster in monsterList)
        {
            monster.Distance = distance;
        }

        var body = SerializeToElement(monsterList);
        await this._proxy.CreateAsync(TrackingType, userId, body);
        return monsterList.Count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var monsters = DeserializeMonsters(json);
        var matching = monsters.Where(m => uids.Contains(m.Uid)).ToList();

        if (matching.Count == 0)
        {
            return 0;
        }

        foreach (var monster in matching)
        {
            monster.Distance = distance;
        }

        var body = SerializeToElement(matching);
        await this._proxy.CreateAsync(TrackingType, userId, body);
        return matching.Count;
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
