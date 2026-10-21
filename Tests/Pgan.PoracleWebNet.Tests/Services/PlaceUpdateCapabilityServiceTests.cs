using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The gate on <c>PUT /api/v2/humans/{id}/locations/{label}</c>, the one operation in the v2 migration with
/// no v1 equivalent and therefore the one that needs a gate rather than a fallback. Verified live: 5.1.0
/// answers gin's plaintext 404 on the route and 5.2.1 serves it, so every supported server has it.
/// </summary>
public class PlaceUpdateCapabilityServiceTests
{
    [Theory]
    [InlineData("5.4.0")]
    [InlineData("6.0.0")]
    // A build script may stamp a suffix; the profile's parser tolerates it and so must the gate.
    [InlineData("5.4.0-rc1")]
    public async Task SupportedServersAreCapable(string version) =>
        Assert.True(await CreateSut(Reachable(version)).IsPlaceUpdateAvailableAsync());

    [Theory]
    [InlineData("5.3.0")]
    [InlineData("5.2.1")]
    [InlineData("5.1.0")]
    [InlineData("4.9.9")]
    public async Task ServersBelowTheMinimumAreNotCapable(string version) =>
        Assert.False(await CreateSut(Reachable(version)).IsPlaceUpdateAvailableAsync());

    /// <summary>
    /// PoracleNG reports 0.0.0 when the build flags are not injected. A local build is built from source
    /// newer than the minimum, so the edit button is offered rather than hidden on a guess.
    /// </summary>
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("not-a-version")]
    public async Task AReachableServerWithAnUnreadableVersionIsAssumedCurrent(string version) =>
        Assert.True(await CreateSut(Reachable(version)).IsPlaceUpdateAvailableAsync());

    [Fact]
    public async Task AnUnreachableServerIsNotCapable() =>
        Assert.False(await CreateSut(PoracleServerProfile.Unknown(DateTimeOffset.UtcNow)).IsPlaceUpdateAvailableAsync());

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
