namespace Pgan.PoracleWebNet.Core.Models;

public class FortChange
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    /// <summary>
    /// Fort type to track. Valid values: <c>pokestop</c>, <c>gym</c>, <c>everything</c>.
    /// See <see cref="FortChangeOptions.ValidFortTypes"/>.
    /// </summary>
    public string? FortType
    {
        get; set;
    }
    public int IncludeEmpty
    {
        get; set;
    }

    /// <summary>
    /// Change types to monitor. Valid values: <c>name</c>, <c>location</c>, <c>image_url</c>, <c>removal</c>, <c>new</c>.
    /// An empty list means "all changes". See <see cref="FortChangeOptions.ValidChangeTypes"/>.
    /// </summary>
    public List<string> ChangeTypes { get; set; } = [];
    public int Clean
    {
        get; set;
    }
    public string? Template
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }
}
