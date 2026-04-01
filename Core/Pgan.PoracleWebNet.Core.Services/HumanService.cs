using System.Text.Json;

using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Hybrid service: proxy-first for single-user operations, direct DB for admin bulk operations.
/// GetByIdAsync, ExistsAsync, CreateAsync, and state mutations (start/stop/adminDisabled)
/// go through IPoracleHumanProxy. GetAllAsync, DeleteAllAlarmsByUserAsync, and DeleteUserAsync
/// remain direct DB via IHumanRepository because PoracleNG has no admin-list or admin-delete
/// endpoints yet.
/// </summary>
public partial class HumanService(
    IHumanRepository repository,
    IPoracleHumanProxy humanProxy,
    IPoracleTrackingProxy trackingProxy,
    ILogger<HumanService> logger) : IHumanService
{
    private readonly IHumanRepository _repository = repository;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly ILogger<HumanService> _logger = logger;

    /// <summary>
    /// All tracking types used by PoracleNG for alarm deletion.
    /// </summary>
    private static readonly string[] AlarmTypes = ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    // TODO: Migrate once PoracleNG adds a "get all humans" endpoint.
    // See: docs/poracleng-enhancement-requests.md
    public async Task<IEnumerable<Human>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<Human?> GetByIdAsync(string id)
    {
        try
        {
            var json = await this._humanProxy.GetHumanAsync(id);
            if (json is null)
            {
                return null;
            }

            return DeserializeHuman(json.Value);
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "GetByIdAsync", id);
            return await this._repository.GetByIdAsync(id);
        }
    }

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo)
    {
        try
        {
            var json = await this._humanProxy.GetHumanAsync(id);
            if (json is null)
            {
                return null;
            }

            var human = DeserializeHuman(json.Value);
            return human.CurrentProfileNo == profileNo ? human : null;
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "GetByIdAndProfileAsync", id);
            return await this._repository.GetByIdAndProfileAsync(id, profileNo);
        }
    }

    public async Task<Human> CreateAsync(Human human)
    {
        try
        {
            var body = SerializeHumanForCreate(human);
            await this._humanProxy.CreateHumanAsync(body);

            // Re-fetch via proxy to get the full record with defaults applied by PoracleNG
            var created = await this.GetByIdAsync(human.Id);
            return created ?? human;
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "CreateAsync", human.Id);
            return await this._repository.CreateAsync(human);
        }
    }

    public async Task<Human> UpdateAsync(Human human)
    {
        // For state mutations that map to specific proxy endpoints, use the proxy.
        // For general field updates, fall back to direct DB (no generic update endpoint in PoracleNG).
        // Callers that need specific state changes (enable/disable/pause/resume) should prefer
        // the dedicated AdminController endpoints that call the proxy directly.
        // TODO: Migrate once PoracleNG adds a generic human update endpoint.
        // See: docs/poracleng-enhancement-requests.md
        return await this._repository.UpdateAsync(human);
    }

    public async Task<bool> ExistsAsync(string id)
    {
        try
        {
            var json = await this._humanProxy.GetHumanAsync(id);
            return json is not null;
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "ExistsAsync", id);
            return await this._repository.ExistsAsync(id);
        }
    }

    public async Task<int> DeleteAllAlarmsByUserAsync(string userId)
    {
        try
        {
            var totalDeleted = 0;
            foreach (var type in AlarmTypes)
            {
                // Fetch all UIDs for this alarm type, then bulk delete them
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
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "DeleteAllAlarmsByUserAsync", userId);
            return await this._repository.DeleteAllAlarmsByUserAsync(userId);
        }
    }

    // TODO: Migrate once PoracleNG adds a user deletion endpoint.
    // See: docs/poracleng-enhancement-requests.md
    public async Task<bool> DeleteUserAsync(string userId) => await this._repository.DeleteUserAsync(userId);

    /// <summary>
    /// Deserializes a PoracleNG human JSON response (snake_case) to a Human model.
    /// </summary>
    private static Human DeserializeHuman(JsonElement json)
    {
        return new Human
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
    }

    /// <summary>
    /// Serializes a Human model to a JsonElement for the PoracleNG create endpoint.
    /// </summary>
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

    /// <summary>
    /// Extracts UIDs from a tracking JSON array response.
    /// </summary>
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "PoracleNG proxy failed for {Method}({UserId}), falling back to direct DB.")]
    private static partial void LogProxyFallback(ILogger logger, Exception ex, string method, string userId);
}
