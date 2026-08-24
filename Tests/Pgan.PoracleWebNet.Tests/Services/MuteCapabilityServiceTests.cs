using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The version gate on the v2 mute endpoints. Verified live: 5.1.0 answers 404 on the route and 5.2.1
/// serves it, and neither one's /health capability map mentions mutes.
/// </summary>
public class MuteCapabilityServiceTests
{
    private static MuteCapabilityService CreateSut(PoracleServerProfile profile)
    {
        var serverProfile = new Mock<IPoracleServerProfileService>();
        serverProfile
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        return new MuteCapabilityService(serverProfile.Object);
    }

    private static PoracleServerProfile Reachable(string version) => new()
    {
        Version = version,
        Reachable = true,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("5.2.0")]
    [InlineData("5.2.1")]
    [InlineData("5.3.0")]
    [InlineData("6.0.0")]
    // A build script may stamp a suffix; the profile's parser tolerates it and so must the gate.
    [InlineData("5.2.0-rc1")]
    public async Task ServersFromTheFirstReleaseWithTheEndpointAreCapable(string version) =>
        Assert.True(await CreateSut(Reachable(version)).IsMuteApiAvailableAsync());

    [Theory]
    [InlineData("5.1.0")]
    [InlineData("5.0.9")]
    [InlineData("4.9.9")]
    public async Task OlderServersAreNotCapable(string version) =>
        Assert.False(await CreateSut(Reachable(version)).IsMuteApiAvailableAsync());

    /// <summary>
    /// PoracleNG reports 0.0.0 when the build flags are not injected, so a locally built binary is
    /// unknown rather than ancient — and unknown must fail closed here, not open.
    /// </summary>
    [Fact]
    public async Task ALocallyBuiltServerReportingZeroIsNotCapable() =>
        Assert.False(await CreateSut(Reachable("0.0.0")).IsMuteApiAvailableAsync());

    [Fact]
    public async Task AnUnreachableServerIsNotCapable() =>
        Assert.False(await CreateSut(PoracleServerProfile.Unknown(DateTimeOffset.UtcNow)).IsMuteApiAvailableAsync());

    /// <summary>
    /// A version too mangled to parse is unknown. Reachable alone must not unlock the surface, or a
    /// server with no version stamp would offer a control that 404s on every press.
    /// </summary>
    [Fact]
    public async Task AnUnparseableVersionIsNotCapable() =>
        Assert.False(await CreateSut(Reachable("not-a-version")).IsMuteApiAvailableAsync());

    [Fact]
    public async Task AProfileReadThatThrowsIsNotCapable()
    {
        var serverProfile = new Mock<IPoracleServerProfileService>();
        serverProfile
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        Assert.False(await new MuteCapabilityService(serverProfile.Object).IsMuteApiAvailableAsync());
    }

    /// <summary>
    /// The gate reads the version, never <c>Supports("mutes")</c>. 5.2.1's map is
    /// {buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes} — verified on the
    /// live instance — so a capability-map gate would answer false on a server that serves the endpoint.
    /// </summary>
    [Fact]
    public async Task CapabilityMapWithoutAMutesKeyDoesNotBlockA521Server()
    {
        var profile = new PoracleServerProfile
        {
            Version = "5.2.1",
            Reachable = true,
            CheckedAt = DateTimeOffset.UtcNow,
            Capabilities = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["buttons"] = true,
                ["snapshots"] = true,
                ["autocreate"] = true,
                ["tomlDts"] = true,
                ["buttonResponseObject"] = true,
                ["derivedDtsTypes"] = true,
            },
        };

        Assert.False(profile.Supports("mutes"));
        Assert.True(await CreateSut(profile).IsMuteApiAvailableAsync());
    }
}
