using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Reading PoracleNG's <c>disabledHooks</c> array, which does not say the same thing on both branches.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG 5.1.0 reports <c>pokestop</c>, which nothing in its processor enforces. Its develop line
/// dropped that entry and added <c>fort</c>, which is enforced (jfberry/PoracleNG#197, filed from here
/// as #195). The map handles both without asking which branch it is talking to: <c>fort</c> is mapped
/// and simply never appears on 5.1.0, and <c>pokestop</c> is mapped to nothing and simply never appears
/// on develop.
/// </para>
/// <para>
/// That is the shape worth keeping. A version check here would be one more thing to update on every
/// PoracleNG release, for an answer the payload already gives.
/// </para>
/// </remarks>
public class PoracleDisabledHookMapTests
{
    /// <summary>Exactly what PoracleNG 5.1.0 reports when every hook is off.</summary>
    private static readonly string[] MainReleaseAllDisabled =
    [
        "pokemon", "raid", "pokestop", "invasion", "lure", "quest", "weather", "nest", "gym", "maxbattle",
    ];

    /// <summary>Exactly what PoracleNG develop reports when every hook is off.</summary>
    private static readonly string[] DevelopAllDisabled =
    [
        "pokemon", "raid", "fort", "invasion", "lure", "quest", "weather", "nest", "gym", "maxbattle",
    ];

    /// <summary>
    /// The develop-only entry. Mapping it is what makes fort changes honour Poracle's own flag on a
    /// server new enough to report it.
    /// </summary>
    [Fact]
    public void FortDisablesFortChanges()
    {
        Assert.Contains(DisableFeatureKeys.FortChanges, PoracleDisabledHookMap.ToDisableKeys(["fort"]));
    }

    /// <summary>
    /// The other half, and the one that matters for two-branch support: on 5.1.0 the name is absent, so
    /// mapping it changes nothing at all. Shipping the entry before the upstream change merges is
    /// therefore free, and needs no coordinated release.
    /// </summary>
    [Fact]
    public void FortMappingIsInertOnTheReleasedLine()
    {
        var keys = PoracleDisabledHookMap.ToDisableKeys(MainReleaseAllDisabled);

        Assert.DoesNotContain(DisableFeatureKeys.FortChanges, keys);
    }

    /// <summary>
    /// <c>pokestop</c> maps to nothing on purpose. It looks like the parent of lures, invasions and
    /// quests, and reading it that way would take three working alarm types away from a server that set
    /// a flag which does nothing. Upstream now calls it deprecated.
    /// </summary>
    [Fact]
    public void PokestopDisablesNothing()
    {
        var keys = PoracleDisabledHookMap.ToDisableKeys(["pokestop"]);

        Assert.Empty(keys);
    }

    /// <summary>
    /// Both branches, everything off: the same set of keys, minus fort on the older one. Asserting the
    /// whole set rather than one key is what catches a mapping quietly gaining or losing an entry.
    /// </summary>
    [Fact]
    public void BothBranchesResolveTheSameTypesApartFromFort()
    {
        var onMain = PoracleDisabledHookMap.ToDisableKeys(MainReleaseAllDisabled);
        var onDevelop = PoracleDisabledHookMap.ToDisableKeys(DevelopAllDisabled);

        Assert.Equal(onMain.Concat([DisableFeatureKeys.FortChanges]).OrderBy(k => k, StringComparer.Ordinal), onDevelop.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>Nothing disabled on either branch stays nothing disabled.</summary>
    [Fact]
    public void AnEmptyArrayDisablesNothing()
    {
        Assert.Empty(PoracleDisabledHookMap.ToDisableKeys([]));
        Assert.Empty(PoracleDisabledHookMap.ToDisableKeys(null));
    }

    /// <summary>An unfamiliar name from a future branch is ignored, not guessed at.</summary>
    [Fact]
    public void AnUnknownHookNameIsIgnored()
    {
        Assert.Empty(PoracleDisabledHookMap.ToDisableKeys(["showcase", "incident"]));
    }
}
