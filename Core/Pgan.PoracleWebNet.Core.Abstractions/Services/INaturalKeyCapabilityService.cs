namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Whether PoracleNG's database still refuses a second rule on a tracking type's natural key.
/// </summary>
/// <remarks>
/// Up to schema 5 (PoracleNG 5.1.0) <c>lures</c> carries <c>UNIQUE lure_tracking(id, profile_no, lure_id)</c>
/// and <c>invasion</c> carries <c>UNIQUE invasion_tracking(id, profile_no, gender, grunt_type)</c>. Migration 8
/// (5.2.x) drops both, and from then on PoracleNG stores two lure rules for one lure type as readily as two
/// raid rules for one boss. Which of the two worlds a write lands in decides whether a same-key Add is a
/// 500 waiting to happen or a legitimate second rule.
/// </remarks>
public interface INaturalKeyCapabilityService
{
    /// <summary>
    /// True when the table behind <paramref name="trackingType"/> carries a unique key besides its primary
    /// key. False for every type other than <c>lure</c>, <c>invasion</c> and <c>incident</c>, which never had one.
    /// Unknown answers true, the behaviour every release before this one had.
    /// </summary>
    Task<bool> IsEnforcedAsync(string trackingType, CancellationToken cancellationToken = default);
}
