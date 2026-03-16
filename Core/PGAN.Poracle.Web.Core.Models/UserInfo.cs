namespace PGAN.Poracle.Web.Core.Models;

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public int ProfileNo { get; set; }
    public string? AvatarUrl { get; set; }
}
