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
    /// Optional refreshed JWT token. Returned by <c>/api/auth/me</c> when the JWT's
    /// <c>profileNo</c> claim is stale (e.g. PoracleNG changed the active profile via
    /// the active_hours scheduler or a bot command). Null when no resync is needed.
    /// </summary>
    public string? Token
    {
        get; set;
    }
}
