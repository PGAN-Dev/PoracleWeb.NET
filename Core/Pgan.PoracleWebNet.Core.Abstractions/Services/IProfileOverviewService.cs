using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Service for fetching cross-profile alarm overview data.
/// </summary>
public interface IProfileOverviewService
{
    /// <summary>
    /// Gets all tracking alarms across all profiles for a user, including descriptions.
    /// </summary>
    public Task<JsonElement> GetAllProfilesOverviewAsync(string userId);

    /// <summary>
    /// Duplicates a profile by creating a new profile and copying all alarms from the source.
    /// Returns the number of alarms copied.
    /// </summary>
    public Task<int> DuplicateProfileAsync(string userId, int sourceProfileNo, int newProfileNo);

    /// <summary>
    /// Imports alarms from a backup into an existing profile.
    /// Switches to the target profile, creates all alarms, then switches back.
    /// </summary>
    public Task<int> ImportAlarmsAsync(string userId, int targetProfileNo, JsonElement alarms);
}
