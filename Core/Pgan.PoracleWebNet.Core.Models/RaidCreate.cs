using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class RaidCreate
{
    [Range(0, int.MaxValue)]
    public int PokemonId
    {
        get; set;
    }

    [StringLength(256)]
    public string? Ping
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Distance
    {
        get; set;
    }

    [Range(0, 4)]
    public int Team { get; set; } = 4;

    // PoracleNG accepts any positive integer as a raid level, plus 9000 as the
    // "any level" wildcard. The previous [Range(0, 10)] rejected the wildcard
    // and any custom server-defined tiers (Elite at 7+, custom 8+) before they
    // could reach PoracleNG. See #259.
    [Range(0, int.MaxValue)]
    public int Level
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Form
    {
        get; set;
    }

    [Range(0, 1)]
    public int Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Move { get; set; } = 9000;

    [Range(0, int.MaxValue)]
    public int Evolution { get; set; } = 9000;

    [Range(0, 1)]
    public int Exclusive
    {
        get; set;
    }

    [StringLength(255)]
    public string? GymId
    {
        get; set;
    }

    [Range(0, 1)]
    public int RsvpChanges
    {
        get; set;
    }
}
