using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Answers whether this PoracleNG can store the costume filter.
/// </summary>
/// <remarks>
/// <para>
/// <c>monsters.costume</c> arrives at PoracleNG migration 6 and <c>raid.costume</c> at 7, both in every
/// supported release, so this answers <see cref="PoracleServerProfile.IsSupported"/> for both types. It
/// used to read the applied migration number straight out of PoracleNG's <c>schema_migrations</c> table,
/// which was the last thing besides the lure and invasion unique keys that needed PoracleWeb.NET to read
/// PoracleNG's database; 5.2.1 becoming the minimum retired both.
/// </para>
/// <para>
/// Not the <c>/health</c> capability map: it carries no costume key, and absent means false by that
/// map's own contract, so consulting <c>Supports("costume")</c> would switch the feature off everywhere.
/// </para>
/// <para>
/// Unreachable, or known to be older than the minimum, answers <see cref="CostumeCapability.None"/>. No
/// cache of its own:
/// <see cref="IPoracleServerProfileService"/> already caches the probe for five minutes and exposes
/// <c>Invalidate()</c>, and a second layer on top would keep serving the old answer after an admin hit
/// refresh following a PoracleNG upgrade — the one moment it is guaranteed to have changed.
/// </para>
/// </remarks>
public sealed class CostumeCapabilityService(IPoracleServerProfileService serverProfile) : ICostumeCapabilityService
{
    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    /// <inheritdoc />
    public async Task<CostumeCapability> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await this._serverProfile.GetAsync(cancellationToken);

            return Resolve(profile);
        }
        catch
        {
            return CostumeCapability.None;
        }
    }

    /// <inheritdoc />
    public async Task EnsureCostumeWritableAsync(
        string trackingType, int costume, CancellationToken cancellationToken = default)
    {
        if (costume == CostumeCapability.AnyCostume)
        {
            return;
        }

        var capability = await this.GetAsync(cancellationToken);

        if (capability.Supports(trackingType))
        {
            return;
        }

        throw new AlarmValidationException(
            "This Poracle server cannot store a costume filter. It needs PoracleNG "
            + $"{PoracleServerProfile.MinimumSupported} or newer.");
    }

    /// <summary>
    /// Applies the rule to a profile. Static so the tests and anything reporting on a hand-built profile
    /// resolve exactly what a live probe would, without going near one.
    /// </summary>
    public static CostumeCapability Resolve(PoracleServerProfile profile) =>
        profile.IsSupported ? new(true, true) : CostumeCapability.None;
}
