using System.Text.Json;

using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Proxy-first service for human operations. Admin bulk operations (GetAll, DeleteUser, UpdateAsync)
/// remain direct DB via IHumanRepository because PoracleNG has no admin-list, admin-delete, or
/// generic update endpoints yet. See: docs/poracleng-enhancement-requests.md
/// </summary>
public class HumanService(
    IHumanRepository repository,
    IPoracleHumanProxy humanProxy,
    IPoracleTrackingProxy trackingProxy) : IHumanService
{
    private readonly IHumanRepository _repository = repository;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;

    private static readonly string[] AlarmTypes = ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    // TODO: Migrate once PoracleNG adds a "get all humans" endpoint.
    // See: docs/poracleng-enhancement-requests.md
    public async Task<IEnumerable<Human>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<Human?> GetByIdAsync(string id)
    {
        var json = await this._humanProxy.GetHumanAsync(id);
        return json is not null ? DeserializeHuman(json.Value) : null;
    }

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo)
    {
        var json = await this._humanProxy.GetHumanAsync(id);
        if (json is null)
        {
            return null;
        }

        var human = DeserializeHuman(json.Value);
        return human.CurrentProfileNo == profileNo ? human : null;
    }

    public async Task<Human> CreateAsync(Human human)
    {
        var body = SerializeHumanForCreate(human);
        await this._humanProxy.CreateHumanAsync(body);

        // Re-fetch via proxy to get the full record with defaults applied by PoracleNG
        var created = await this.GetByIdAsync(human.Id);
        return created ?? human;
    }

    // TODO: Migrate once PoracleNG adds a generic human update endpoint.
    // See: docs/poracleng-enhancement-requests.md
    public async Task<Human> UpdateAsync(Human human) => await this._repository.UpdateAsync(human);

    public async Task<bool> ExistsAsync(string id)
    {
        var json = await this._humanProxy.GetHumanAsync(id);
        return json is not null;
    }

    public async Task<int> DeleteAllAlarmsByUserAsync(string userId)
    {
        var totalDeleted = 0;
        foreach (var type in AlarmTypes)
        {
            var trackingJson = await this._trackingProxy.GetByUserAsync(type, userId);
            var uids = ExtractUids(trackingJson);
            if (uids.Count > 0)
            {
                await this._trackingProxy.BulkDeleteByUidsAsync(type, userId, uids);
                totalDeleted += uids.Count;
            }
        }

        return totalDeleted;
    }

    // TODO: Migrate once PoracleNG adds a user deletion endpoint.
    // See: docs/poracleng-enhancement-requests.md
    public async Task<bool> DeleteUserAsync(string userId) => await this._repository.DeleteUserAsync(userId);

    private static Human DeserializeHuman(JsonElement json) => new Human
    {
        Id = json.GetStringProp("id"),
        Name = json.GetStringPropOrNull("name"),
        Type = json.GetStringPropOrNull("type"),
        Enabled = json.GetIntProp("enabled"),
        Area = json.GetStringPropOrNull("area"),
        Latitude = json.GetDoubleProp("latitude"),
        Longitude = json.GetDoubleProp("longitude"),
        Fails = json.GetIntProp("fails"),
        Language = json.GetStringPropOrNull("language"),
        AdminDisable = json.GetIntProp("admin_disable"),
        CurrentProfileNo = json.GetIntProp("current_profile_no"),
        CommunityMembership = json.GetStringPropOrNull("community_membership"),
    };

    private static JsonElement SerializeHumanForCreate(Human human)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = human.Id,
            name = human.Name ?? human.Id,
            type = human.Type ?? "discord:user",
            enabled = human.Enabled,
            admin_disable = human.AdminDisable,
        });

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<int> ExtractUids(JsonElement trackingJson)
    {
        var uids = new List<int>();
        if (trackingJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in trackingJson.EnumerateArray())
            {
                if (item.TryGetProperty("uid", out var uidProp) && uidProp.TryGetInt32(out var uid))
                {
                    uids.Add(uid);
                }
            }
        }

        return uids;
    }
}
