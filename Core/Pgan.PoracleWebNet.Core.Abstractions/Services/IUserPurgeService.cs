namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Removes everything PoracleWeb holds about a user when their account is deleted.
/// </summary>
/// <remarks>
/// The delete used to remove the <c>humans</c> row alone. Everything else stayed: alarms in the Poracle DB,
/// and geofences, webhook delegate grants, quick picks and their applied state in <c>poracle_web</c>. None of
/// it was reachable through any API surface afterwards, so it looked deleted — until the same id was created
/// again, which adopted the lot. The delegate grants are the sharp end: re-creating a webhook URL silently
/// restored impersonation rights over it, and a deleted user's geofences kept being published in the feed
/// PoracleJS reads. See #510, #511, #512.
/// </remarks>
public interface IUserPurgeService
{
    /// <summary>
    /// Erases the user's data everywhere PoracleWeb stores it. Returns false when no such user exists.
    /// </summary>
    public Task<bool> PurgeAsync(string userId);
}
