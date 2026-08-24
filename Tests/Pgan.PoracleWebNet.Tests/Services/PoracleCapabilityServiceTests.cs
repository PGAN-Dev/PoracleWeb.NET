using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The two-branch contract: PoracleWeb must work against PoracleNG's released <c>main</c> and its
/// longer-running <c>develop</c>, offering each exactly what it can store.
/// </summary>
/// <remarks>
/// <para>
/// The matrix is the point. A test that only proved "costume is refused on an old server" would pass
/// just as happily if costume were refused on every server, which is the failure that shipped three
/// times before (#548, #555, #565): a guard that validated something, ran, and did nothing useful.
/// Every capability is therefore asserted on <em>both</em> profiles, and
/// <see cref="EveryCapabilityIsInTheMatrix"/> fails the build if a new one arrives without a row.
/// </para>
/// <para>
/// The profiles below are real. 5.1.0 at schema 5 is <c>origin/main</c> as of the 5.1.0 release;
/// 5.2.1 at schema 8 is <c>origin/develop</c> carrying migrations 6 (monster costume), 7 (raid
/// costume) and 8 (drop tracking unique keys).
/// </para>
/// </remarks>
public class PoracleCapabilityServiceTests
{
    /// <summary>PoracleNG <c>origin/main</c>: the released line, and the floor this build targets.</summary>
    private static PoracleServerProfile MainRelease => new()
    {
        Version = "5.1.0",
        SchemaVersion = 5,
        Reachable = true,
        CheckedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>PoracleNG <c>origin/develop</c> with jfberry/PoracleNG#197 merged.</summary>
    private static PoracleServerProfile DevelopLine => new()
    {
        Version = "5.2.1",
        SchemaVersion = 8,
        Reachable = true,
        CheckedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>
    /// What each capability must answer on each branch. Adding a capability means adding a row here.
    /// </summary>
    public static TheoryData<string, bool, bool> Matrix => new()
    {
        // capability, available on main, available on develop
        { PoracleCapabilityKeys.MonsterCostume, false, true },
        { PoracleCapabilityKeys.RaidCostume, false, true },
        { PoracleCapabilityKeys.QuestPokecoins, false, true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ResolvesEachCapabilityPerBranch(string capability, bool onMain, bool onDevelop)
    {
        Assert.Equal(onMain, PoracleCapabilityService.Resolve(MainRelease).Contains(capability));
        Assert.Equal(onDevelop, PoracleCapabilityService.Resolve(DevelopLine).Contains(capability));
    }

    /// <summary>
    /// The guard. A capability with no matrix row is one nobody has checked against the released
    /// branch, which is how a develop-only field reaches a main install and silently no-ops.
    /// </summary>
    [Fact]
    public void EveryCapabilityIsInTheMatrix()
    {
        var covered = Matrix.Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);
        var missing = PoracleCapabilityKeys.All.Select(r => r.Key).Where(k => !covered.Contains(k)).ToList();

        Assert.True(
            missing.Count == 0,
            "Capabilities with no branch matrix row: " + string.Join(", ", missing)
                + ". Add a row to PoracleCapabilityServiceTests.Matrix stating what each PoracleNG branch answers.");
    }

    /// <summary>Every rule needs a non-empty <c>Requires</c>: it is what the 409 and the startup log say.</summary>
    [Fact]
    public void EveryCapabilityExplainsWhatItNeeds()
    {
        Assert.All(PoracleCapabilityKeys.All, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Requires)));
    }

    /// <summary>Keys have to be unique, or the resolver silently drops one.</summary>
    [Fact]
    public void CapabilityKeysAreDistinct()
    {
        var keys = PoracleCapabilityKeys.All.Select(r => r.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// An unreachable server unlocks nothing. Not a nicety: the opposite default offers every
    /// develop-only control to a main install whenever PoracleNG hiccups.
    /// </summary>
    [Fact]
    public void UnknownServerSupportsNothing()
    {
        Assert.Empty(PoracleCapabilityService.Resolve(PoracleServerProfile.Unknown(DateTimeOffset.UnixEpoch)));
    }

    /// <summary>
    /// A locally built binary reports "0.0.0", which the profile treats as unknown rather than ancient.
    /// Version-gated capabilities must stay off rather than reading 0.0.0 as "newer than nothing" —
    /// while the schema-gated pair still resolve, because the migration number was genuinely read.
    /// </summary>
    [Fact]
    public void LocallyBuiltBinaryDoesNotUnlockVersionGatedCapabilities()
    {
        var localBuild = new PoracleServerProfile
        {
            Version = "0.0.0",
            SchemaVersion = 8,
            Reachable = true,
            CheckedAt = DateTimeOffset.UnixEpoch,
        };

        var supported = PoracleCapabilityService.Resolve(localBuild);

        Assert.DoesNotContain(PoracleCapabilityKeys.QuestPokecoins, supported);
        Assert.Contains(PoracleCapabilityKeys.MonsterCostume, supported);
    }

    /// <summary>
    /// The two costume migrations land separately, so a server mid-upgrade has one and not the other.
    /// Collapsing them into a single "costume" capability would offer the raid control on schema 6.
    /// </summary>
    [Fact]
    public void CostumeCapabilitiesTrackTheirOwnMigrations()
    {
        var atSix = new PoracleServerProfile { Version = "5.2.0", SchemaVersion = 6, Reachable = true };

        var supported = PoracleCapabilityService.Resolve(atSix);

        Assert.Contains(PoracleCapabilityKeys.MonsterCostume, supported);
        Assert.DoesNotContain(PoracleCapabilityKeys.RaidCostume, supported);
    }

    /// <summary>A version above the floor keeps the capability: the gate must not be an equality check.</summary>
    [Theory]
    [InlineData("5.2.0", true)]
    [InlineData("5.2.1", true)]
    [InlineData("5.3.0", true)]
    [InlineData("6.0.0", true)]
    [InlineData("5.1.9", false)]
    [InlineData("5.1.0", false)]
    public void PokecoinsTracksTheVersionFloor(string version, bool expected)
    {
        var profile = new PoracleServerProfile { Version = version, SchemaVersion = 8, Reachable = true };

        Assert.Equal(
            expected,
            PoracleCapabilityService.Resolve(profile).Contains(PoracleCapabilityKeys.QuestPokecoins));
    }

    /// <summary>A build-script suffix must not make the version unreadable and switch the feature off.</summary>
    [Fact]
    public void PrereleaseSuffixStillSatisfiesTheFloor()
    {
        var rc = new PoracleServerProfile { Version = "5.2.0-rc1", SchemaVersion = 8, Reachable = true };

        Assert.Contains(PoracleCapabilityKeys.QuestPokecoins, PoracleCapabilityService.Resolve(rc));
    }

    [Fact]
    public async Task EnsureSupportedAsyncPassesWhenTheServerHasIt()
    {
        var sut = new PoracleCapabilityService(ProfileServiceReturning(DevelopLine));

        await sut.EnsureSupportedAsync(PoracleCapabilityKeys.QuestPokecoins);
    }

    [Fact]
    public async Task EnsureSupportedAsyncNamesWhatTheServerWouldNeed()
    {
        var sut = new PoracleCapabilityService(ProfileServiceReturning(MainRelease));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.EnsureSupportedAsync(PoracleCapabilityKeys.QuestPokecoins));

        Assert.Equal(PoracleCapabilityKeys.QuestPokecoins, ex.Capability);
        Assert.Equal("PoracleNG 5.2.0", ex.Requires);
        Assert.Contains("5.2.0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSupportedAsyncReadsThroughTheProfileService()
    {
        var sut = new PoracleCapabilityService(ProfileServiceReturning(DevelopLine));

        var supported = await sut.GetSupportedAsync();

        Assert.Equal(
            PoracleCapabilityKeys.All.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal),
            supported.OrderBy(k => k, StringComparer.Ordinal));
    }

    private static IPoracleServerProfileService ProfileServiceReturning(PoracleServerProfile profile)
    {
        var mock = new Mock<IPoracleServerProfileService>();
        mock.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        return mock.Object;
    }
}
