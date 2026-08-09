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
        var profiles = DeserializeProfiles(json);

        return await this.WithActiveProfileAsync(userId, profiles);
    }

    /// <summary>
    /// Guarantees the profile the user is actually on appears in the list.
    /// </summary>
    /// <remarks>
    /// PoracleNG only materialises a <c>profiles</c> row when something writes one, so an account that has
    /// never renamed or added a profile has none -- while <c>humans.current_profile_no</c> still points at
    /// one and alarms hang off it. The Profiles and Profile Overview pages then rendered "no alarms across
    /// any profiles" over a full set of alarms. Synthesised rather than written, so this stays a read.
    /// See #582.
    /// </remarks>
    private async Task<IEnumerable<Profile>> WithActiveProfileAsync(string userId, List<Profile> profiles)
    {
        JsonElement? human;
        try
        {
            human = await this._humanProxy.GetHumanAsync(userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return profiles;
        }

        if (human is not { } record)
        {
            return profiles;
        }

        var activeProfileNo = record.GetIntProp("current_profile_no");
        if (profiles.Exists(p => p.ProfileNo == activeProfileNo))
        {
            return profiles;
        }

        profiles.Add(new Profile
        {
            Id = userId,
            ProfileNo = activeProfileNo,
            Name = "Default",
            Area = record.GetStringPropOrNull("area") ?? "[]",
            Latitude = record.GetDoubleProp("latitude"),
            Longitude = record.GetDoubleProp("longitude"),
        });

        return [.. profiles.OrderBy(p => p.ProfileNo)];
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
