using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The keys decide, then the migration number, then "enforced" -- in that order.
/// </summary>
public class NaturalKeyCapabilityServiceTests
{
    private readonly Mock<IPoracleServerProfileService> _profile = new();

    private NaturalKeyCapabilityService Sut(PoracleServerProfile profile)
    {
        this._profile.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        return new NaturalKeyCapabilityService(this._profile.Object);
    }

    /// <summary>What <c>information_schema</c> lists on each test bed, verified with SHOW INDEX.</summary>
    private static readonly string[] Schema5Keys = ["lures", "invasion", "profiles", "pweb_settings", "user_locations", "weather"];
    private static readonly string[] Schema8Keys = ["profiles", "pweb_settings", "user_locations", "weather"];

    [Theory]
    [InlineData("lure")]
    [InlineData("invasion")]
    [InlineData("incident")]
    public async Task OnPoracleNg510TheKeyIsEnforced(string type) =>
        Assert.True(await this.Sut(new PoracleServerProfile { SchemaVersion = 5, UniqueKeyedTrackingTables = Schema5Keys })
            .IsEnforcedAsync(type));

    [Theory]
    [InlineData("lure")]
    [InlineData("invasion")]
    [InlineData("incident")]
    public async Task OnPoracleNg521TheKeyIsGone(string type) =>
        Assert.False(await this.Sut(new PoracleServerProfile { SchemaVersion = 8, UniqueKeyedTrackingTables = Schema8Keys })
            .IsEnforcedAsync(type));

    [Fact]
    public async Task AKeyThatSurvivedMigration8UnderAnotherNameStillCounts()
    {
        // Migration 8 drops the keys by name; an adopted database whose key is named otherwise keeps it.
        Assert.True(await this.Sut(new PoracleServerProfile { SchemaVersion = 8, UniqueKeyedTrackingTables = ["lures"] })
            .IsEnforcedAsync("lure"));
    }

    [Theory]
    [InlineData(5L, true)]
    [InlineData(7L, true)]
    [InlineData(8L, false)]
    [InlineData(9L, false)]
    public async Task WithoutTheKeyListTheMigrationNumberDecides(long schema, bool enforced) =>
        Assert.Equal(enforced, await this.Sut(new PoracleServerProfile { SchemaVersion = schema }).IsEnforcedAsync("lure"));

    [Fact]
    public async Task KnowingNothingKeepsTheOldRefusal() =>
        Assert.True(await this.Sut(new PoracleServerProfile()).IsEnforcedAsync("invasion"));

    [Fact]
    public async Task AFailedProbeKeepsTheOldRefusal()
    {
        this._profile.Setup(p => p.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        Assert.True(await new NaturalKeyCapabilityService(this._profile.Object).IsEnforcedAsync("lure"));
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    [InlineData("nest")]
    public async Task TypesThatNeverHadAKeyAreNeverEnforced(string type) =>
        Assert.False(await this.Sut(new PoracleServerProfile { SchemaVersion = 5, UniqueKeyedTrackingTables = Schema5Keys })
            .IsEnforcedAsync(type));
}
