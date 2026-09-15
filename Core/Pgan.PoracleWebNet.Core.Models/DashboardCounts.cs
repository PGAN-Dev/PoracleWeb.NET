using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class DashboardCounts
{
    [JsonPropertyName("pokemon")]
    public int Monsters
    {
        get; set;
    }
    public int Raids
    {
        get; set;
    }
    public int Eggs
    {
        get; set;
    }
    public int Quests
    {
        get; set;
    }
    public int Invasions
    {
        get; set;
    }
    public int Lures
    {
        get; set;
    }
    public int Nests
    {
        get; set;
    }
    public int Gyms
    {
        get; set;
    }
    public int FortChanges
    {
        get; set;
    }
    public int MaxBattles
    {
        get; set;
    }

    /// <summary>Showcase, Kecleon and Gold Stop alarms. Upstream calls the type <c>incident</c>.</summary>
    public int PokestopEvents
    {
        get; set;
    }
}
