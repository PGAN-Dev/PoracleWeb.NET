using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The costume gate. <c>monsters.costume</c> (migration 6) and <c>raid.costume</c> (7) are in every
/// supported PoracleNG, so a supported server offers both and anything else offers neither. The dev
/// instance running 5.1.0 had neither column; the one running 5.2.1 has both.
/// </summary>
public class CostumeCapabilityServiceTests
{
    private static PoracleServerProfile Profile(string? version = "5.4.0", bool reachable = true) => new()
    {
        Version = version,
        Reachable = reachable,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    private static CostumeCapabilityService Sut(PoracleServerProfile profile)
    {
        var profiles = new Mock<IPoracleServerProfileService>();
        profiles.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        return new CostumeCapabilityService(profiles.Object);
    }

    // --- What the server can store ---

    [Theory]
    [InlineData("5.4.0")]
    [InlineData("5.4.1")]
    public async Task ASupportedServerOffersBoth(string version)
    {
        var capability = await Sut(Profile(version)).GetAsync();

        Assert.True(capability.Pokemon);
        Assert.True(capability.Raid);
    }

    [Theory]
    [InlineData("5.1.0")]
    [InlineData("5.2.0")]
    public async Task AServerBelowTheMinimumOffersNeither(string version)
    {
        var capability = await Sut(Profile(version)).GetAsync();

        Assert.False(capability.Pokemon);
        Assert.False(capability.Raid);
    }

    /// <summary>
    /// A locally built PoracleNG reports 0.0.0. It is built from source newer than the minimum, so the
    /// filter is offered. Before 5.2.1 was the minimum this gate read the migration number out of
    /// PoracleNG's database to cover exactly this case; it no longer needs to.
    /// </summary>
    [Fact]
    public async Task ALocallyBuiltBinaryIsAssumedCurrent()
    {
        var capability = await Sut(Profile("0.0.0")).GetAsync();

        Assert.True(capability.Pokemon);
        Assert.True(capability.Raid);
    }

    // --- Failing closed ---

    [Fact]
    public async Task UnreachableServerOffersNeither()
    {
        var capability = await Sut(Profile(reachable: false)).GetAsync();

        Assert.Equal(CostumeCapability.None, capability);
    }

    [Fact]
    public async Task ProbeThatThrowsOffersNeither()
    {
        var profiles = new Mock<IPoracleServerProfileService>();
        profiles.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var capability = await new CostumeCapabilityService(profiles.Object).GetAsync();

        Assert.Equal(CostumeCapability.None, capability);
    }

    // --- Refusing a write the server would silently drop ---

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    public async Task WildcardIsWritableOnAServerWithoutTheColumns(string trackingType)
    {
        // 9000 asks for no filtering and is what PoracleNG stores for an absent key, so nothing is lost
        // by writing it to a server that has no costume column. Refusing it would ban every ordinary
        // alarm there.
        await Sut(Profile("5.1.0")).EnsureCostumeWritableAsync(trackingType, CostumeCapability.AnyCostume);
        await Sut(Profile(reachable: false)).EnsureCostumeWritableAsync(trackingType, CostumeCapability.AnyCostume);
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    public async Task NamedCostumeIsWritableOnASupportedServer(string trackingType)
    {
        await Sut(Profile()).EnsureCostumeWritableAsync(trackingType, 85);
    }

    [Theory]
    [InlineData("pokemon", 85)]
    [InlineData("raid", 85)]
    // "No costume" is a real filter, not an unset marker, so it is refused like any other.
    [InlineData("pokemon", 0)]
    [InlineData("raid", 0)]
    public async Task NamedCostumeIsRefusedOnAServerBelowTheMinimum(string trackingType, int costume)
    {
        await Assert.ThrowsAsync<AlarmValidationException>(
            () => Sut(Profile("5.1.0")).EnsureCostumeWritableAsync(trackingType, costume));
    }

    [Fact]
    public async Task RefusalNamesTheVersionItNeeds()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => Sut(Profile("5.1.0")).EnsureCostumeWritableAsync("raid", 85));

        Assert.Contains("5.4.0", ex.Message, StringComparison.Ordinal);
    }
}
