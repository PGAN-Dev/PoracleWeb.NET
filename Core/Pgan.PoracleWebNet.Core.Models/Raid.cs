namespace Pgan.PoracleWebNet.Core.Models;

public class Raid
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public int PokemonId
    {
        get; set;
    }
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int Team
    {
        get; set;
    }
    public int Level
    {
        get; set;
    }
    public int Form
    {
        get; set;
    }
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
