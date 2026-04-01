using System.Text.Json;

using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Hybrid service: proxy-first for profile reads, direct DB as fallback.
/// Create/Update/Delete are already proxied by ProfileController via IPoracleHumanProxy;
/// this service is used for reads (GetByUser, GetByUserAndProfileNo) by
/// LocationController, UserGeofenceService, and ProfileController.GetAll.
/// </summary>
public partial class ProfileService(
    IProfileRepository repository,
    IPoracleHumanProxy humanProxy,
    ILogger<ProfileService> logger) : IProfileService
{
    private readonly IProfileRepository _repository = repository;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly ILogger<ProfileService> _logger = logger;

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId)
    {
        try
        {
            var json = await this._humanProxy.GetProfilesAsync(userId);
            var profiles = DeserializeProfiles(json);
            if (profiles.Count > 0)
            {
                return profiles;
            }
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "GetByUserAsync", userId);
        }

        return await this._repository.GetByUserAsync(userId);
    }

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo)
    {
        try
        {
            var json = await this._humanProxy.GetProfilesAsync(userId);
            var profiles = DeserializeProfiles(json);
            var match = profiles.Find(p => p.ProfileNo == profileNo);
            if (match != null)
            {
                return match;
            }
        }
        catch (Exception ex)
        {
            LogProxyFallback(this._logger, ex, "GetByUserAndProfileNoAsync", userId);
        }

        return await this._repository.GetByUserAndProfileNoAsync(userId, profileNo);
    }

    public async Task<Profile> CreateAsync(Profile profile) => await this._repository.CreateAsync(profile);

    public async Task<Profile> UpdateAsync(Profile profile) => await this._repository.UpdateAsync(profile);

    public async Task<bool> DeleteAsync(string userId, int profileNo) => await this._repository.DeleteAsync(userId, profileNo);

    /// <summary>
    /// Deserializes the PoracleNG profiles response.
    /// PoracleNG wraps the array: { "profile": [...], "status": "ok" }
    /// Note: the key is "profile" (singular), not "profiles".
    /// </summary>
    private static List<Profile> DeserializeProfiles(JsonElement json)
    {
        JsonElement profileArray;

        if (json.TryGetProperty("profile", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            profileArray = arr;
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            profileArray = json;
        }
        else
        {
            return [];
        }

        var profiles = new List<Profile>();
        foreach (var item in profileArray.EnumerateArray())
        {
            profiles.Add(new Profile
            {
                Id = item.GetStringProp("id"),
                ProfileNo = item.GetIntProp("profile_no"),
                Name = item.GetStringPropOrNull("name"),
                Area = item.GetStringPropOrNull("area") ?? "[]",
                Latitude = item.GetDoubleProp("latitude"),
                Longitude = item.GetDoubleProp("longitude"),
            });
        }

        return profiles;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PoracleNG proxy failed for {Method}({UserId}), falling back to direct DB.")]
    private static partial void LogProxyFallback(ILogger logger, Exception ex, string method, string userId);
}
