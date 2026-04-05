namespace Pgan.PoracleWebNet.Core.Models;

public class MaxBattle
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public int PokemonId { get; set; } = 9000;
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int Gmax
    {
        get; set;
    }
    public int Level { get; set; } = 9000;
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
    public int Move { get; set; } = 9000;
    public int Evolution { get; set; } = 9000;
    public string? StationId
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }
}
