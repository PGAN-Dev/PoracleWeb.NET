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
}
