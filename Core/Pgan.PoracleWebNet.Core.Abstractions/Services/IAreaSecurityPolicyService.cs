namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether PoracleNG's own <c>area_security</c> feature (community-restricted area lists) is confirmed
/// switched off -- the one condition under which <c>trusted</c> on <c>/api/v2/humans/{id}/areas</c> is
/// safe to rely on regardless of whether the target server carries jfberry/PoracleNG#230's
/// community-restriction fix. See #838.
/// </summary>
/// <remarks>
/// Neither <c>/openapi.json</c> nor <c>/health</c> distinguishes a pre-#230 server from a post-#230 one:
/// <c>V2SetAreasBody</c>'s schema and the capability map are byte-identical before and after the fix,
/// verified against two live builds either side of the merge. <c>area_security.enabled</c> is the only
/// signal this application can read.
/// </remarks>
public interface IAreaSecurityPolicyService
{
    /// <summary>
    /// True only when PoracleNG explicitly reports <c>area_security.enabled == false</c>. Fails
    /// <strong>closed</strong>: unreachable, too old to expose the field, or any other uncertainty all
    /// answer false, so the caller defaults to not trusting <c>setAreas</c>'s community-restriction
    /// behaviour rather than risking a user escaping a community's allowed-area list. This is the
    /// opposite fail direction from <see cref="IUpstreamFeatureFlagService"/>, which fails open for an
    /// unrelated reason (an alarm-type outage is worse than a stale disable) -- do not merge the two.
    /// </summary>
    Task<bool> IsConfirmedDisabledAsync();
}
