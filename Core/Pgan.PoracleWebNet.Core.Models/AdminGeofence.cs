namespace Pgan.PoracleWebNet.Core.Models;

public class AdminGeofence
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public double[][] Path { get; set; } = [];
    public bool UserSelectable { get; set; } = true;
    public bool DisplayInMatches { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#3399ff";
}
