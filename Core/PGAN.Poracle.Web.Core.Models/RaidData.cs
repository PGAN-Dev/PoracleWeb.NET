namespace PGAN.Poracle.Web.Core.Models;

public class RaidData
{
    public string GymId { get; set; } = string.Empty;
    public string? Name
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
    public int Level
    {
        get; set;
    }
    public int PokemonId
    {
        get; set;
    }
    public int Form
    {
        get; set;
    }
    public DateTimeOffset EndTime
    {
        get; set;
    }
}
