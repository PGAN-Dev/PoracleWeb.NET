using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The gate on the v2 mute endpoints. They arrived in 5.2.0, so every supported PoracleNG serves them and
/// the gate is <see cref="PoracleServerProfile.IsSupported"/>. Verified live: 5.1.0 answers 404 on the
/// route and 5.2.1 serves it, and neither one's /health capability map mentions mutes.
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

    private static PoracleServerProfile Reachable(string? version) => new()
    {
        Version = version,
        Reachable = true,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("5.4.0")]
    [InlineData("5.4.1")]
    [InlineData("6.0.0")]
    // A build script may stamp a suffix; the profile's parser tolerates it and so must the gate.
    [InlineData("5.4.0-rc1")]
    public async Task SupportedServersAreCapable(string version) =>
        Assert.True(await CreateSut(Reachable(version)).IsMuteApiAvailableAsync());

    [Theory]
    [InlineData("5.3.0")]
    [InlineData("5.2.1")]
    [InlineData("5.1.0")]
    [InlineData("4.9.9")]
    public async Task ServersBelowTheMinimumAreNotCapable(string version) =>
        Assert.False(await CreateSut(Reachable(version)).IsMuteApiAvailableAsync());

    /// <summary>
    /// PoracleNG reports 0.0.0 when the build flags are not injected. A local build is built from source
    /// newer than the minimum, so it is offered the surface rather than having it hidden on a guess.
    /// </summary>
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("not-a-version")]
    [InlineData(null)]
    public async Task AReachableServerWithAnUnreadableVersionIsAssumedCurrent(string? version) =>
        Assert.True(await CreateSut(Reachable(version)).IsMuteApiAvailableAsync());

    [Fact]
    public async Task AnUnreachableServerIsNotCapable() =>
        Assert.False(await CreateSut(PoracleServerProfile.Unknown(DateTimeOffset.UtcNow)).IsMuteApiAvailableAsync());

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
    /// The gate never reads <c>Supports("mutes")</c>. 5.2.1's map is
    /// {buttons, snapshots, autocreate, tomlDts, buttonResponseObject, derivedDtsTypes} — verified on the
    /// live instance — so a capability-map gate would answer false on a server that serves the endpoint.
    /// </summary>
    [Fact]
    public async Task CapabilityMapWithoutAMutesKeyDoesNotBlockA540Server()
    {
        var profile = new PoracleServerProfile
        {
            Version = "5.4.0",
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
