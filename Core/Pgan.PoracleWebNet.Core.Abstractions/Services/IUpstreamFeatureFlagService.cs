namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Resolves which alarm types the upstream Poracle deployment has switched off in its own config,
/// expressed as this application's <c>disable_*</c> keys.
/// </summary>
/// <remarks>
/// These act as a <em>floor</em> under the <c>disable_*</c> site settings rather than a replacement:
/// a type is off if either source says so. The site settings still gate UI Poracle has no opinion
/// about (areas, profiles, geocoding), so they cannot simply be swapped out. See #769.
/// </remarks>
public interface IUpstreamFeatureFlagService
{
    /// <summary>
    /// The <c>disable_*</c> keys Poracle forces off. Empty when Poracle is unreachable, is too old to
    /// report the flags, or genuinely disables nothing — the caller must not be able to tell those
    /// apart, because all three mean "leave the site settings in charge".
    /// </summary>
    Task<IReadOnlySet<string>> GetDisabledKeysAsync();
}
