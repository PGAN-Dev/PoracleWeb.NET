namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Whether the PoracleNG behind this install can store the costume filter, answered per alarm type.
/// </summary>
/// <remarks>
/// <para>
/// Two answers rather than one because they were two migrations: <c>monsters.costume</c> lands at 6 and
/// <c>raid.costume</c> at 7. Every supported PoracleNG carries both, so today the two always agree; the
/// shape stays because the SPA reads it per type.
/// </para>
/// </remarks>
/// <param name="Pokemon">True when <c>monsters.costume</c> exists.</param>
/// <param name="Raid">True when <c>raid.costume</c> exists.</param>
public sealed record CostumeCapability(bool Pokemon, bool Raid)
{
    /// <summary>
    /// The "any costume" wildcard. PoracleNG's v1 handler defaults an absent costume to this, so a
    /// rule carrying it asks for nothing the old column was needed for and is safe on any server.
    /// </summary>
    public const int AnyCostume = 9000;

    /// <summary>
    /// Nothing offered. What an unreachable server, one known to be older than the minimum and a fault
    /// while probing all resolve to: hiding a control that would have worked is a nuisance, offering one
    /// whose value is silently discarded is the defect this exists to prevent.
    /// </summary>
    public static CostumeCapability None
    {
        get;
    } = new(false, false);

    /// <summary>True when <paramref name="trackingType"/> can store a costume on this server.</summary>
    /// <remarks>Any type other than pokemon or raid answers false — no other table has the column.</remarks>
    public bool Supports(string trackingType) => trackingType switch
    {
        "pokemon" => this.Pokemon,
        "raid" => this.Raid,
        _ => false,
    };
}
