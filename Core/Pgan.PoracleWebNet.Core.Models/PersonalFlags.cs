namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Helpers for the PoracleNG pokemon rule <c>personal</c> column (PoracleNG PR #255), a bitmask:
/// bit 1 = shiny for one of the human's linked game accounts, bit 2 = event background for one of
/// them. Bits 4 and 8 are reserved for future per-player sizes. PoracleWeb.NET does not edit the
/// reserved bits, so every write goes through <see cref="Preserve"/> and sends them back as it read them.
/// PoracleNG itself masks <c>personal</c> to the bits it knows on every write (<c>db.PersonalKnownMask</c>,
/// verified live against <c>feat/personal-shiny</c> at <c>37cc7a33</c>), so today a reserved bit set by a
/// direct database write is cleared by the next edit through any surface. Sending it back costs nothing and
/// keeps it once PoracleNG defines the bit and widens that mask.
/// </summary>
public static class PersonalFlags
{
    /// <summary>Shiny bit (bit 1).</summary>
    public const int Shiny = 1;

    /// <summary>Event background bit (bit 2).</summary>
    public const int Background = 2;

    /// <summary>The bits PoracleWeb.NET understands and edits (3).</summary>
    public const int Known = Shiny | Background;

    /// <summary>True when the shiny bit (bit 1) is set.</summary>
    public static bool IsShiny(int personal) => (personal & Shiny) != 0;

    /// <summary>True when the event background bit (bit 2) is set.</summary>
    public static bool IsBackground(int personal) => (personal & Background) != 0;

    /// <summary>True when a bit outside <see cref="Known"/> is set (reserved by PoracleNG).</summary>
    public static bool HasUnknownBits(int personal) => (personal & ~Known) != 0;

    /// <summary>Composes a personal bitmask from the two known flags.</summary>
    public static int Compose(bool shiny, bool background) => (shiny ? Shiny : 0) | (background ? Background : 0);

    /// <summary>
    /// Returns <paramref name="existing"/> with only the bits in <paramref name="mask"/>
    /// replaced by the corresponding bits from <paramref name="changes"/>. Bits outside the
    /// mask are left untouched, so reserved bits set elsewhere are sent back unchanged.
    /// </summary>
    public static int Preserve(int existing, int mask, int changes) => (existing & ~mask) | (changes & mask);
}
