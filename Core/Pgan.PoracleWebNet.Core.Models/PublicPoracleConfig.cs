namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The subset of <see cref="PoracleConfig"/> that is safe to hand to a browser.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/config</c> used to return <see cref="PoracleConfig"/> whole, which carried the
/// Poracle admin Discord/Telegram id lists, the webhook delegation map, the internal provider URL
/// and the static map key. Upstream PoracleNG gates that same payload behind
/// <c>X-Poracle-Secret</c>; PoracleWeb republished it on an internet-facing route. The identical
/// admin list is <c>[Authorize]</c>-and-admin-gated on <c>GET /api/admin/poracle-admins</c>, so the
/// data was protected on one route and public on another.
/// </para>
/// <para>
/// This type is an allowlist, not a denylist: a new field added to <see cref="PoracleConfig"/> is
/// not exposed until it is deliberately added here. The omitted fields have no browser consumer —
/// <c>admins</c> and <c>delegateAdministration</c> are read server-side only (by
/// <c>AuthController</c> and <c>AdminController</c>), and the SPA never read <c>providerURL</c> or
/// <c>staticKey</c> beyond carrying them in its fallback object.
/// </para>
/// </remarks>
public class PublicPoracleConfig
{
    public string Locale { get; set; } = string.Empty;
    public string PoracleVersion { get; set; } = string.Empty;
    public int PvpFilterMaxRank { get; set; }
    public int PvpFilterLittleMinCp { get; set; }
    public int PvpFilterGreatMinCp { get; set; }
    public int PvpFilterUltraMinCp { get; set; }
    public bool PvpLittleLeagueAllowed { get; set; }
    public List<int> PvpCaps { get; set; } = [];
    public int DefaultPvpCap { get; set; }
    public string DefaultTemplateName { get; set; } = string.Empty;
    public string EverythingFlagPermissions { get; set; } = string.Empty;
    public int MaxDistance { get; set; }

    public static PublicPoracleConfig From(PoracleConfig source) => new()
    {
        Locale = source.Locale,
        PoracleVersion = source.PoracleVersion,
        PvpFilterMaxRank = source.PvpFilterMaxRank,
        PvpFilterLittleMinCp = source.PvpFilterLittleMinCp,
        PvpFilterGreatMinCp = source.PvpFilterGreatMinCp,
        PvpFilterUltraMinCp = source.PvpFilterUltraMinCp,
        PvpLittleLeagueAllowed = source.PvpLittleLeagueAllowed,
        PvpCaps = source.PvpCaps,
        DefaultPvpCap = source.DefaultPvpCap,
        DefaultTemplateName = source.DefaultTemplateName,
        EverythingFlagPermissions = source.EverythingFlagPermissions,
        MaxDistance = source.MaxDistance
    };
}
