namespace Pgan.PoracleWebNet.Core.Models;

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsAdmin
    {
        get; set;
    }
    public bool AdminDisable
    {
        get; set;
    }
    public bool Enabled { get; set; } = true;
    public int ProfileNo
    {
        get; set;
    }
    public string? AvatarUrl
    {
        get; set;
    }
    public string[]? ManagedWebhooks
    {
        get; set;
    }

    /// <summary>
    /// Name of the active profile.
    /// </summary>
    /// <remarks>
    /// The SPA has always rendered this in the user menu with a "Profile {n}" fallback, and the property
    /// did not exist here -- so the fallback fired every time and the menu disagreed with the Profiles
    /// page, which looks the name up properly. See #520.
    /// </remarks>
    public string? ProfileName
    {
        get; set;
    }

    /// <summary>
    /// Optional refreshed JWT token. Returned by <c>/api/auth/me</c> when the JWT's
    /// <c>profileNo</c> claim is stale (e.g. PoracleNG changed the active profile via
    /// the active_hours scheduler or a bot command). Null when no resync is needed.
    /// </summary>
    public string? Token
    {
        get; set;
    }
}
