using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class InvasionService(IPoracleTrackingProxy proxy) : IInvasionService
{
    private const string TrackingType = "invasion";
    private readonly IPoracleTrackingProxy _proxy = proxy;

    public async Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return DeserializeItems(json);
    }

    public async Task<Invasion?> GetByUidAsync(string userId, int uid)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Invasion> CreateAsync(string userId, Invasion model)
    {
        model.Id = userId;
        var body = SerializeToElement(model);
        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Invasion> UpdateAsync(string userId, Invasion model)
    {
        var body = SerializeToElement(model);
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
        var items = DeserializeItems(json);
        var itemList = items.ToList();

        if (itemList.Count == 0)
        {
            return 0;
        }

        foreach (var item in itemList)
        {
            item.Distance = distance;
        }

        var body = SerializeToElement(itemList);
        await this._proxy.CreateAsync(TrackingType, userId, body);
        return itemList.Count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        var matching = items.Where(x => uids.Contains(x.Uid)).ToList();

        if (matching.Count == 0)
        {
            return 0;
        }

        foreach (var item in matching)
        {
            item.Distance = distance;
        }

        var body = SerializeToElement(matching);
        await this._proxy.CreateAsync(TrackingType, userId, body);
        return matching.Count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);
        return items.Count();
    }

    public async Task<IEnumerable<Invasion>> BulkCreateAsync(string userId, IEnumerable<Invasion> models)
    {
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

    private static List<Invasion> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Invasion>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
