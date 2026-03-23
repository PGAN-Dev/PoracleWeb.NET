namespace Pgan.PoracleWebNet.Core.Models;

public class SiteSetting
{
    public int Id
    {
        get; set;
    }
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Value
    {
        get; set;
    }
    public string ValueType { get; set; } = "string";
}
