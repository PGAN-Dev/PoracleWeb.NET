using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The costume gate. The two numbers are not invented: the dev instance running 5.1.0 reports
/// schema 5 and has neither column, the one running 5.2.1 reports schema 8 and has both.
/// </summary>
public class CostumeCapabilityServiceTests
{
    private static PoracleServerProfile Profile(long? schema, bool reachable = true, string? version = "5.2.1") => new()
    {
        Version = version,
        SchemaVersion = schema,
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

    [Fact]
    public async Task ServerWithBothMigrationsOffersBoth()
    {
        var capability = await Sut(Profile(8)).GetAsync();

        Assert.True(capability.Pokemon);
        Assert.True(capability.Raid);
    }

    /// <summary>
    /// The reason this is two answers. A server that stopped at 6 stores a costumed pokemon rule and
    /// drops a costumed raid rule, and a single boolean would have to lie about one of them.
    /// </summary>
    [Fact]
    public async Task ServerBetweenTheTwoMigrationsOffersPokemonOnly()
    {
        var capability = await Sut(Profile(6)).GetAsync();

        Assert.True(capability.Pokemon);
        Assert.False(capability.Raid);
    }

    [Fact]
    public async Task ServerOnFiveOneOffersNeither()
    {
        var capability = await Sut(Profile(5, version: "5.1.0")).GetAsync();

        Assert.False(capability.Pokemon);
        Assert.False(capability.Raid);
    }

    // --- Failing closed ---

    [Fact]
    public async Task UnreadableSchemaOffersNeither()
    {
        var capability = await Sut(Profile(null)).GetAsync();

        Assert.False(capability.Pokemon);
        Assert.False(capability.Raid);
    }

    [Fact]
    public async Task UnreachableServerOffersNeitherEvenWhenItsDatabaseIsMigrated()
    {
        var capability = await Sut(Profile(8, reachable: false)).GetAsync();

        Assert.False(capability.Pokemon);
        Assert.False(capability.Raid);
    }

    /// <summary>
    /// A locally built PoracleNG reports 0.0.0 and gets no version out of it, which is exactly why the
    /// gate reads the migration instead. The columns are there; the filter is offered.
    /// </summary>
    [Fact]
    public async Task LocallyBuiltBinaryIsJudgedOnItsSchemaNotItsVersion()
    {
        var capability = await Sut(Profile(8, version: "0.0.0")).GetAsync();

        Assert.True(capability.Pokemon);
        Assert.True(capability.Raid);
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
        // alarm on 5.1.0.
        await Sut(Profile(5)).EnsureCostumeWritableAsync(trackingType, CostumeCapability.AnyCostume);
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    public async Task NamedCostumeIsWritableOnAMigratedServer(string trackingType)
    {
        await Sut(Profile(8)).EnsureCostumeWritableAsync(trackingType, 85);
    }

    [Theory]
    [InlineData("pokemon", 85)]
    [InlineData("raid", 85)]
    // "No costume" is a real filter, not an unset marker, so it is refused like any other.
    [InlineData("pokemon", 0)]
    [InlineData("raid", 0)]
    public async Task NamedCostumeIsRefusedOnAServerWithoutTheColumns(string trackingType, int costume)
    {
        await Assert.ThrowsAsync<AlarmValidationException>(
            () => Sut(Profile(5)).EnsureCostumeWritableAsync(trackingType, costume));
    }

    [Fact]
    public async Task RaidCostumeIsRefusedOnAServerThatOnlyHasTheMonsterColumn()
    {
        var sut = Sut(Profile(6));

        await sut.EnsureCostumeWritableAsync("pokemon", 85);
        await Assert.ThrowsAsync<AlarmValidationException>(() => sut.EnsureCostumeWritableAsync("raid", 85));
    }

    [Fact]
    public async Task RefusalNamesTheMigrationItNeeds()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => Sut(Profile(5)).EnsureCostumeWritableAsync("raid", 85));

        Assert.Contains("7", ex.Message, StringComparison.Ordinal);
    }
}
