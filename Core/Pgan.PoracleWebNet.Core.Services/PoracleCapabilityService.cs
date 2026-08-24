using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Resolves <see cref="PoracleCapabilityKeys"/> against the probed server profile.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately holds no cache of its own. <c>PoracleServerProfileService</c> already caches the probe
/// for five minutes and exposes <c>Invalidate()</c>, and a second cache layered on top would keep
/// serving stale capabilities after an admin hit refresh following a PoracleNG upgrade — the one
/// moment the answer is guaranteed to have changed.
/// </para>
/// <para>
/// Resolution is pure: the rules read the profile and nothing else, so this cannot fail independently
/// of the probe. A probe that failed yields <c>PoracleServerProfile.Unknown</c>, whose schema and
/// version are null, and every rule answers false against it.
/// </para>
/// </remarks>
public sealed class PoracleCapabilityService(IPoracleServerProfileService profiles) : IPoracleCapabilityService
{
    private readonly IPoracleServerProfileService _profiles = profiles;

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetSupportedAsync(CancellationToken cancellationToken = default)
    {
        var profile = await this._profiles.GetAsync(cancellationToken);

        return Resolve(profile);
    }

    /// <inheritdoc />
    public async Task<bool> SupportsAsync(string capability, CancellationToken cancellationToken = default)
    {
        var supported = await this.GetSupportedAsync(cancellationToken);

        return supported.Contains(capability);
    }

    /// <inheritdoc />
    public async Task EnsureSupportedAsync(string capability, CancellationToken cancellationToken = default)
    {
        if (await this.SupportsAsync(capability, cancellationToken))
        {
            return;
        }

        var rule = PoracleCapabilityKeys.All.FirstOrDefault(r => r.Key == capability);

        throw new PoracleUnsupportedException(capability, rule?.Requires ?? "a newer PoracleNG");
    }

    /// <summary>
    /// Applies every rule to <paramref name="profile"/>. Static and public so the startup log and the
    /// tests can resolve a hand-built profile without going near a probe.
    /// </summary>
    public static IReadOnlySet<string> Resolve(PoracleServerProfile profile)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in PoracleCapabilityKeys.All)
        {
            if (rule.IsSupported(profile))
            {
                supported.Add(rule.Key);
            }
        }

        return supported;
    }
}
