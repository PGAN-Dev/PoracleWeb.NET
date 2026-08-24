using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The version is the only signal for pokecoins, so this is where that reading is pinned down.
/// </summary>
/// <remarks>
/// 5.2.0 widened PoracleNG's <c>validRewardTypes</c> allowlist and changed nothing else: no column, no
/// config flag, no <c>/health</c> capability key. The two live servers confirm the boundary -- 5.1.0
/// refuses reward type 8 with a 400 and 5.2.1 stores it.
/// </remarks>
public class QuestPokecoinCapabilityServiceTests
{
    private readonly Mock<IPoracleServerProfileService> _profiles = new();

    [Theory]
    [InlineData("5.2.1", true)]
    [InlineData("5.2.0", true)]
    [InlineData("6.0.0", true)]
    [InlineData("5.1.0", false)]
    [InlineData("5.0.9", false)]
    public async Task TheVersionDecides(string version, bool expected)
    {
        this.Answers(new PoracleServerProfile
        {
            Version = version,
            Reachable = true
        });

        Assert.Equal(expected, await this.Sut().ArePokecoinRewardsSupportedAsync());
    }

    /// <summary>A build with no version stamped reports 0.0.0, which says nothing about what it can store.</summary>
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnUnreadableVersionFailsClosed(string? version)
    {
        this.Answers(new PoracleServerProfile
        {
            Version = version,
            Reachable = true
        });

        Assert.False(await this.Sut().ArePokecoinRewardsSupportedAsync());
    }

    /// <summary>
    /// An unreachable server is not a new one. Offering the tab here would hand the user a control every
    /// save of which fails for a reason that has nothing to do with pokecoins.
    /// </summary>
    [Fact]
    public async Task AnUnreachableServerFailsClosed()
    {
        this.Answers(PoracleServerProfile.Unknown(DateTimeOffset.UtcNow));

        Assert.False(await this.Sut().ArePokecoinRewardsSupportedAsync());
    }

    /// <summary>
    /// A version string carrying a build suffix still parses -- self-hosters stamp 5.2.1-rc1 and the
    /// like, and treating those as unknown would switch the feature off for a server that has it.
    /// </summary>
    [Fact]
    public async Task ATaggedReleaseStillCounts()
    {
        this.Answers(new PoracleServerProfile
        {
            Version = "5.2.1-rc1",
            Reachable = true
        });

        Assert.True(await this.Sut().ArePokecoinRewardsSupportedAsync());
    }

    [Fact]
    public async Task AThrowingProbeFailsClosedRatherThanPropagating()
    {
        this._profiles
            .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        Assert.False(await this.Sut().ArePokecoinRewardsSupportedAsync());
    }

    private void Answers(PoracleServerProfile profile) =>
        this._profiles.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);

    private QuestPokecoinCapabilityService Sut() => new(this._profiles.Object);
}
