namespace Pgan.PoracleWebNet.Core.Models;

public class GymSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string? Name
    {
        get; set;
    }
    public string? Url
    {
        get; set;
    }
    public double Lat
    {
        get; set;
    }
    public double Lon
    {
        get; set;
    }
    public int? TeamId
    {
        get; set;
    }
    public string? Area
    {
        get; set;
    }
}
