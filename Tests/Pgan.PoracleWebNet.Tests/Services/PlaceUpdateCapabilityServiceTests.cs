using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The version gate on <c>PUT /api/v2/humans/{id}/locations/{label}</c>, the one operation in the v2
/// migration with no v1 equivalent and therefore the one that needs a gate rather than a fallback.
/// Verified live: 5.1.0 answers gin's plaintext 404 on the route and 5.2.1 serves it.
/// </summary>
public class PlaceUpdateCapabilityServiceTests
{
    [Theory]
    [InlineData("5.2.0")]
    [InlineData("5.2.1")]
    [InlineData("6.0.0")]
    // A build script may stamp a suffix; the profile's parser tolerates it and so must the gate.
    [InlineData("5.2.0-rc1")]
    public async Task ServersFromTheFirstReleaseWithTheRouteAreCapable(string version) =>
        Assert.True(await CreateSut(Reachable(version)).IsPlaceUpdateAvailableAsync());

    [Theory]
    [InlineData("5.1.0")]
    [InlineData("4.9.9")]
    public async Task OlderServersAreNotCapable(string version) =>
        Assert.False(await CreateSut(Reachable(version)).IsPlaceUpdateAvailableAsync());

    /// <summary>
    /// PoracleNG reports 0.0.0 when the build flags are not injected, so a locally built binary is
    /// unknown rather than ancient -- and unknown must fail closed, leaving the user the
    /// delete-and-re-add flow rather than an edit button that 404s.
    /// </summary>
    [Fact]
    public async Task ALocallyBuiltServerReportingZeroIsNotCapable() =>
        Assert.False(await CreateSut(Reachable("0.0.0")).IsPlaceUpdateAvailableAsync());

    [Fact]
    public async Task AnUnreachableServerIsNotCapable() =>
        Assert.False(await CreateSut(PoracleServerProfile.Unknown(DateTimeOffset.UtcNow)).IsPlaceUpdateAvailableAsync());

    [Fact]
    public async Task AnUnparseableVersionIsNotCapable() =>
        Assert.False(await CreateSut(Reachable("not-a-version")).IsPlaceUpdateAvailableAsync());

    [Fact]
    public async Task AProfileReadThatThrowsIsNotCapable()
    {
        var serverProfile = new Mock<IPoracleServerProfileService>();
        serverProfile
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        Assert.False(await new PlaceUpdateCapabilityService(serverProfile.Object).IsPlaceUpdateAvailableAsync());
    }

    private static PlaceUpdateCapabilityService CreateSut(PoracleServerProfile profile)
    {
        var serverProfile = new Mock<IPoracleServerProfileService>();
        serverProfile
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        return new PlaceUpdateCapabilityService(serverProfile.Object);
    }

    private static PoracleServerProfile Reachable(string version) => new()
    {
        Version = version,
        Reachable = true,
        CheckedAt = DateTimeOffset.UtcNow,
    };
}
