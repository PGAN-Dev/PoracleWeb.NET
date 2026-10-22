namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>Why a stored rule needs attention after the move to PoracleNG 5.4.0's <c>/api/v2</c>.</summary>
public enum RuleRepairKind
{
    /// <summary>An invasion rule stored with the grunt type <c>metal</c>, which the game data calls <c>steel</c>.</summary>
    MetalGrunt,

    /// <summary>A raid stored with <c>pokemon_id</c> 0, which v2 refuses; by level is <c>9000</c>.</summary>
    RaidPokemonZero,

    /// <summary>A raid v2 reads as any boss at any level, which may be stored as 9000/9000 rather than 90/9000.</summary>
    RaidAnyBossLevel9000,

    /// <summary>A max battle v2 reads as any boss at any level, which may be stored as 9000/9000.</summary>
    MaxBattleAnyBossLevel9000,

    /// <summary>A rule v2 cannot express, so it can be reported but not edited.</summary>
    CannotEdit,
}

/// <summary>One stored rule the repair scan found, with a short before and after for the admin to read.</summary>
public sealed record RuleRepairItem(
    string HumanId,
    string Type,
    int ProfileNo,
    int Uid,
    RuleRepairKind Kind,
    string Before,
    string? After,
    string? Reason);
