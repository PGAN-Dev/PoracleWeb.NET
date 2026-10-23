namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>One fence from <c>GET /api/v2/humans/{id}/areas</c>, as far as an area write needs it.</summary>
/// <param name="Name">The fence name as PoracleNG lists it.</param>
/// <param name="UserSelectable">Whether this human may select the fence without a trusted write.</param>
/// <param name="ExemptFromAreaSecurity">The fence is outside the community system.</param>
public sealed record V2AreaInfo(string Name, bool UserSelectable, bool ExemptFromAreaSecurity);
