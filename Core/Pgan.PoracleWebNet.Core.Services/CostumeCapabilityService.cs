using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Answers the costume question from the applied PoracleNG migration number.
/// </summary>
/// <remarks>
/// <para>
/// Migration, not version. <c>/health</c>'s capability map covers bot and template-editor features and
/// carries no costume key — absent means false by that map's own contract, so consulting
/// <c>Supports("costume")</c> would switch the feature off everywhere, on 5.2.1 included. The version
/// string is no better: it says which release line, not which columns exist, and a locally built binary
/// reports <c>0.0.0</c>. <c>monsters.costume</c> arrives at migration 6, <c>raid.costume</c> at 7.
/// </para>
/// <para>
/// Fails closed, the same way <see cref="SummaryCapabilityService"/> does. No cache of its own:
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
            "This Poracle server cannot store a costume filter. It needs the costume column "
            + $"(PoracleNG schema {(trackingType == "raid" ? CostumeCapability.RaidSchema : CostumeCapability.MonsterSchema)}).");
    }

    /// <summary>
    /// Applies the migration rules to a profile. Static so the tests and anything reporting on a
    /// hand-built profile resolve exactly what a live probe would, without going near one.
    /// </summary>
    /// <remarks>
    /// A server that did not answer unlocks nothing, even when its database reports the migration. The
    /// number comes straight from the schema table, so it survives the process being stopped, an upgrade
    /// halfway through, or a database shared with a binary other than the one PoracleWeb writes
    /// through -- none of them a server to offer a filter on the strength of.
    /// </remarks>
    public static CostumeCapability Resolve(PoracleServerProfile profile) =>
        profile.Reachable
            ? new(profile.HasSchema(CostumeCapability.MonsterSchema), profile.HasSchema(CostumeCapability.RaidSchema))
            : CostumeCapability.None;
}
