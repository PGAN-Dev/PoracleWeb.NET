using System.Text.Json;

using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Proxy-first service for profile reads. Create/Update/Delete are already proxied by
/// ProfileController via IPoracleHumanProxy; this service provides reads for
/// LocationController, UserGeofenceService, and ProfileController.GetAll.
/// IProfileRepository is kept for non-active profile operations in UserGeofenceService.
/// </summary>
public class ProfileService(
    IProfileRepository repository,
    IPoracleHumanProxy humanProxy) : IProfileService
{
    private readonly IProfileRepository _repository = repository;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId)
    {
        var json = await this._humanProxy.GetProfilesAsync(userId);
        return DeserializeProfiles(json);
    }

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo)
    {
        var json = await this._humanProxy.GetProfilesAsync(userId);
        var profiles = DeserializeProfiles(json);
        return profiles.Find(p => p.ProfileNo == profileNo);
    }

    public async Task<Profile> CreateAsync(Profile profile) => await this._repository.CreateAsync(profile);

    public async Task<Profile> UpdateAsync(Profile profile) => await this._repository.UpdateAsync(profile);

    public async Task<bool> DeleteAsync(string userId, int profileNo) => await this._repository.DeleteAsync(userId, profileNo);

    public async Task CopyAsync(string userId, int fromProfileNo, int toProfileNo) =>
        await this._humanProxy.CopyProfileAsync(userId, fromProfileNo, toProfileNo);

    /// <summary>
    /// Deserializes the PoracleNG profiles response.
    /// PoracleNG wraps the array: { "profile": [...], "status": "ok" }
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
                ActiveHours = item.GetStringPropOrNull("active_hours"),
            });
        }

        return profiles;
    }
}
