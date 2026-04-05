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
    public string? FortType
    {
        get; set;
    }
    public int IncludeEmpty
    {
        get; set;
    }
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
