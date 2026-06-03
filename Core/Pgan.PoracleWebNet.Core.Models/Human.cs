namespace Pgan.PoracleWebNet.Core.Models;

public class Human
{
    public string Id { get; set; } = string.Empty;
    public string? Name
    {
        get; set;
    }
    public string? Type
    {
        get; set;
    }
    public int Enabled
    {
        get; set;
    }
    public string? Area
    {
        get; set;
    }
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public int Fails
    {
        get; set;
    }
    public string? Language
    {
        get; set;
    }
    public int AdminDisable
    {
        get; set;
    }
    public DateTime LastChecked
    {
        get; set;
    }
    public DateTime? DisabledDate
    {
        get; set;
    }
    public int CurrentProfileNo
    {
        get; set;
    }
    public string? CommunityMembership
    {
        get; set;
    }

    /// <summary>
    /// Free-text notes on the human record. PoracleJS/PoracleNG can be configured to auto-fill this
    /// with the Discord guild (server) name and channel category for channel-type users, which the
    /// admin user list surfaces to disambiguate channels that share the same name.
    /// </summary>
    public string? Notes
    {
        get; set;
    }
}
