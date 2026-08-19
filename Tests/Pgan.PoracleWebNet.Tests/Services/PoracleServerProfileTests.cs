using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Reading which PoracleNG is on the other end.
/// </summary>
/// <remarks>
/// The rule every one of these encodes: not knowing is not the same as supporting. A server that does
/// not answer, a version that will not parse, a schema that cannot be read — each unlocks nothing. The
/// opposite default would offer controls that write columns which do not exist, which is the silent
/// no-op this whole thing exists to make loud.
/// </remarks>
public class PoracleServerProfileTests
{
    /// <summary>The real payload from production, 5.1.0.</summary>
    private const string HealthyResponse =
        """
        {"capabilities":{"buttons":true,"snapshots":true,"autocreate":true,"tomlDts":true,
        "buttonResponseObject":true},"status":"healthy","version":"5.1.0"}
        """;

    private readonly Mock<IPoracleSchemaVersionReader> _schema = new();

    private PoracleServerProfileService Service(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = HealthyResponse,
        Exception? throws = null,
        string apiAddress = "http://poracle:3030")
    {
        var handler = new Mock<HttpMessageHandler>();
        var setup = handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());

        if (throws is not null)
        {
            setup.ThrowsAsync(throws);
        }
        else
        {
            // A fresh response per call: the service disposes what it reads, which is correct, and a
            // single shared instance makes the second probe fail on a disposed stream rather than on
            // anything real.
            setup.ReturnsAsync(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Poracle:ApiAddress"] = apiAddress })
            .Build();

        return new PoracleServerProfileService(
            new HttpClient(handler.Object),
            this._schema.Object,
            new MemoryCache(new MemoryCacheOptions()),
            configuration,
            NullLogger<PoracleServerProfileService>.Instance);
    }

    [Fact]
    public async Task ReadsTheVersionAndTheWholeCapabilityMap()
    {
        this._schema.Setup(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5L);

        var profile = await this.Service().GetAsync();

        Assert.True(profile.Reachable);
        Assert.Equal("5.1.0", profile.Version);
        Assert.Equal(5L, profile.SchemaVersion);
        Assert.Equal(5, profile.Capabilities.Count);
        Assert.True(profile.Supports("buttons"));
    }

    [Fact]
    public async Task KeepsACapabilityItHasNeverHeardOf()
    {
        // derivedDtsTypes exists on PoracleNG's develop branch and in no release. Reading the map into a
        // fixed set of known keys would drop whatever lands next, which is the opposite of the point.
        var profile = await this.Service(body: """{"version":"5.2.0","capabilities":{"derivedDtsTypes":true}}""").GetAsync();

        Assert.True(profile.Supports("derivedDtsTypes"));
    }

    [Fact]
    public async Task ACapabilityNobodyMentionedIsOff()
    {
        // PoracleNG's own contract for the map: clients default-false on a missing key.
        var profile = await this.Service().GetAsync();

        Assert.False(profile.Supports("derivedDtsTypes"));
        Assert.False(profile.Supports("somethingInvented"));
    }

    [Fact]
    public async Task AServerThatDoesNotAnswerSupportsNothing()
    {
        var profile = await this.Service(throws: new HttpRequestException("connection refused")).GetAsync();

        Assert.False(profile.Reachable);
        Assert.Null(profile.Version);
        Assert.False(profile.Supports("buttons"));
        Assert.False(profile.IsBelowMinimum); // unknown, not old
    }

    [Fact]
    public async Task AnUnconfiguredAddressIsNotProbed()
    {
        var profile = await this.Service(apiAddress: "").GetAsync();

        Assert.False(profile.Reachable);
        this._schema.Verify(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheSchemaIsStillReadWhenTheProcessIsDown()
    {
        // A stopped PoracleNG leaves its migrated database behind, and that still says which columns
        // exist. Losing it would turn a restart into "every gated feature disappears".
        this._schema.Setup(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(8L);

        var profile = await this.Service(throws: new HttpRequestException("down")).GetAsync();

        Assert.Equal(8L, profile.SchemaVersion);
    }

    [Fact]
    public async Task AnUnreadableSchemaUnlocksNothing()
    {
        this._schema.Setup(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>())).ReturnsAsync((long?)null);

        var profile = await this.Service().GetAsync();

        Assert.Null(profile.SchemaVersion);
        Assert.False(profile.HasSchema(6));
    }

    [Fact]
    public async Task TheProbeHappensOncePerCacheWindow()
    {
        var service = this.Service();

        await service.GetAsync();
        await service.GetAsync();

        this._schema.Verify(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidatingForcesAFreshRead()
    {
        var service = this.Service();

        await service.GetAsync();
        service.Invalidate();
        await service.GetAsync();

        this._schema.Verify(s => s.GetAppliedMigrationAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("5.1.0", false)]
    [InlineData("5.2.0", false)]
    [InlineData("6.0.0", false)]
    [InlineData("5.0.9", true)]
    [InlineData("4.9.9", true)]
    public void OnlyAVersionKnownToBeOlderCountsAsTooOld(string version, bool tooOld)
    {
        var profile = new PoracleServerProfile { Version = version, Reachable = true };

        Assert.Equal(tooOld, profile.IsBelowMinimum);
    }

    [Theory]
    [InlineData("0.0.0")]      // ldflags not injected: a local build, not an ancient one
    [InlineData("")]
    [InlineData("dev")]
    [InlineData(null)]
    public void AVersionThatSaysNothingIsNotTreatedAsOld(string? version)
    {
        // Shouting "upgrade PoracleNG" at someone running a local build would teach them to ignore the
        // banner, and the banner has one job.
        var profile = new PoracleServerProfile { Version = version, Reachable = true };

        Assert.Null(profile.ParsedVersion);
        Assert.False(profile.IsBelowMinimum);
    }

    [Theory]
    [InlineData("5.2.0-rc1", 5, 2, 0)]
    [InlineData("5.1.0", 5, 1, 0)]
    public void ASuffixedVersionStillParses(string raw, int major, int minor, int build)
    {
        var profile = new PoracleServerProfile { Version = raw, Reachable = true };

        Assert.Equal(new System.Version(major, minor, build), profile.ParsedVersion);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(8, 6, true)]
    [InlineData(5, 6, false)]
    public void SchemaComparisonsAreInclusive(long applied, long required, bool satisfied)
    {
        var profile = new PoracleServerProfile { SchemaVersion = applied };

        Assert.Equal(satisfied, profile.HasSchema(required));
    }

    [Fact]
    public async Task GarbageInsteadOfHealthIsTreatedAsNoAnswer()
    {
        var profile = await this.Service(body: "<html>502 Bad Gateway</html>").GetAsync();

        Assert.False(profile.Reachable);
    }

    [Fact]
    public async Task AHealthPayloadWithNoCapabilitiesStillGivesTheVersion()
    {
        // What an older PoracleNG answers: the map arrived with 5.1.0.
        var profile = await this.Service(body: """{"status":"healthy","version":"5.0.4"}""").GetAsync();

        Assert.Equal("5.0.4", profile.Version);
        Assert.Empty(profile.Capabilities);
        Assert.True(profile.IsBelowMinimum);
    }
}
