using System.ComponentModel.DataAnnotations;

namespace Pgan.PoracleWebNet.Core.Models;

public class MonsterCreate
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

    [Range(-1, 100)]
    public int MinIv
    {
        get; set;
    }

    [Range(-1, 100)]
    public int MaxIv { get; set; } = 100;

    [Range(0, 10000)]
    public int MinCp
    {
        get; set;
    }

    [Range(0, 10000)]
    public int MaxCp { get; set; } = 9000;

    [Range(0, 55)]
    public int MinLevel
    {
        get; set;
    }

    [Range(0, 55)]
    public int MaxLevel { get; set; } = 55;

    [Range(0, int.MaxValue)]
    public int MinWeight
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int MaxWeight { get; set; } = 9000000;

    [Range(0, 15)]
    public int Atk
    {
        get; set;
    }

    [Range(0, 15)]
    public int Def
    {
        get; set;
    }

    [Range(0, 15)]
    public int Sta
    {
        get; set;
    }

    [Range(0, 15)]
    public int MaxAtk { get; set; } = 15;

    [Range(0, 15)]
    public int MaxDef { get; set; } = 15;

    [Range(0, 15)]
    public int MaxSta { get; set; } = 15;

    [Range(0, 4096)]
    public int PvpRankingWorst { get; set; } = 4096;

    /// <summary>
    /// The best (lowest, 1-based) PVP rank to alert on. 1 means no floor, which is the column's own
    /// default and what a rule with no PVP filter stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaulted, like <see cref="PvpRankingWorst"/> beside it, because 0 is not a rank. Leaving it at
    /// the language default wrote 0 into every rule created without a PVP league: 15,383 rows against
    /// 7,770 correct ones on one instance. PoracleNG never writes a 0 here -- the column defaults to 1,
    /// the bot sets 1 explicitly, and both API surfaces default to 1 when the field is absent -- so
    /// every one of them came from here, through v1's flexInt passing the value straight through.
    /// See jfberry/PoracleNG#227.
    /// </para>
    /// <para>
    /// The range deliberately still admits 0. Those 15,383 rules exist and have to stay editable;
    /// refusing the value here would fail an edit on a rule the user did not break, which is the #835
    /// shape. Editing one now writes 1 back and repairs it.
    /// </para>
    /// </remarks>
    [Range(0, 4096)]
    public int PvpRankingBest { get; set; } = 1;

    [Range(0, 10000)]
    public int PvpRankingMinCp
    {
        get; set;
    }

    // The league is a CP cap, and the dropdown offers exactly four: none, Little (500), Great (1500),
    // Ultra (2500). [Range(0, int.MaxValue)] accepted any positive integer, so a value no league uses
    // stored a PVP filter that can never match -- the one unbounded field among Best/Worst/Cap.
    // See #586.
    [AllowedValues(0, 500, 1500, 2500)]
    public int PvpRankingLeague
    {
        get; set;
    }

    [Range(0, 55)]
    public int PvpRankingCap
    {
        get; set;
    }

    // 0 base, 1 any mega, 2 Mega X, 3 Mega Y. PoracleNG reads no other value, and a fifth would rank
    // against a form that does not exist, so the rule would never match.
    [Range(0, 3)]
    public int PvpRankingEvolution
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int Form
    {
        get; set;
    }

    // 9000 = any costume, 0 = no costume, N = that costume. The range refuses only the genuinely
    // impossible rather than allowlisting the costume ids that exist today -- the next event ships a
    // new one and an allowlist would refuse it on day one.
    [Range(0, int.MaxValue)]
    public int Costume { get; set; } = 9000;

    // Seconds of remaining despawn time. A pokemon spawn lasts an hour at the very most, and a value
    // above its lifetime is a rule that can never match.
    [Range(0, 3600)]
    public int MinTime
    {
        get; set;
    }

    [Range(-1, 5)]
    public int Size { get; set; } = -1;

    [Range(0, 5)]
    public int MaxSize { get; set; } = 5;

    [Range(0, 3)]
    public int Gender
    {
        get; set;
    }

    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.
    [Range(0, 7)]
    public int Clean
    {
        get; set;
    }

    [StringLength(256)]
    public string? Template
    {
        get; set;
    }

    /// <summary>Saved-place label this alarm measures its radius from. See the domain model.</summary>
    [StringLength(64)]
    public string? OverrideLocationLabel
    {
        get; set;
    }

    /// <summary>Areas this alarm is confined to. See the domain model.</summary>
    [MaxLength(32)]
    public List<string>? OverrideAreas
    {
        get; set;
    }
}
