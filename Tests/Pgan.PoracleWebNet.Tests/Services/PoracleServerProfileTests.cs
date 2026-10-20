using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Reading which PoracleNG is on the other end.
/// </summary>
/// <remarks>
/// Two rules these encode. A server that does not answer supports nothing, so nothing is offered on the
/// strength of it. A server that answers with a version nobody can read is assumed current, because the
/// only build that reports one (a local build, <c>0.0.0</c>) is built from source newer than the minimum.
/// </remarks>
public class PoracleServerProfileTests
{
    /// <summary>The real payload from production when it ran 5.1.0.</summary>
    private const string HealthyResponse =
        """
        {"capabilities":{"buttons":true,"snapshots":true,"autocreate":true,"tomlDts":true,
        "buttonResponseObject":true},"status":"healthy","version":"5.1.0"}
        """;

    private int _probes;

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
            setup.Callback(() => Interlocked.Increment(ref this._probes)).ThrowsAsync(throws);
        }
        else
        {
            // A fresh response per call: the service disposes what it reads, which is correct, and a
            // single shared instance makes the second probe fail on a disposed stream rather than on
            // anything real.
            setup.ReturnsAsync(() =>
            {
                Interlocked.Increment(ref this._probes);
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            });
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Poracle:ApiAddress"] = apiAddress })
            .Build();

        return new PoracleServerProfileService(
            new HttpClient(handler.Object),
            new MemoryCache(new MemoryCacheOptions()),
            configuration,
            NullLogger<PoracleServerProfileService>.Instance);
    }

    [Fact]
    public async Task ReadsTheVersionAndTheWholeCapabilityMap()
    {
        var profile = await this.Service().GetAsync();

        Assert.True(profile.Reachable);
        Assert.Equal("5.1.0", profile.Version);
        Assert.Equal(5, profile.Capabilities.Count);
        Assert.True(profile.Supports("buttons"));
    }

    [Fact]
    public async Task KeepsACapabilityItHasNeverHeardOf()
    {
        // derivedDtsTypes exists on PoracleNG's develop branch and in no release. Reading the map into a
        // fixed set of known keys would drop whatever lands next, which is the opposite of the point.
        var profile = await this.Service(body: """{"version":"5.2.1","capabilities":{"derivedDtsTypes":true}}""").GetAsync();

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
        Assert.False(profile.IsSupported);
        Assert.False(profile.SupportsV2Tracking);
    }

    [Fact]
    public async Task AnUnconfiguredAddressIsNotProbed()
    {
        var profile = await this.Service(apiAddress: "").GetAsync();

        Assert.False(profile.Reachable);
        Assert.Equal(0, this._probes);
    }

    [Fact]
    public async Task TheProbeHappensOncePerCacheWindow()
    {
        var service = this.Service();

        await service.GetAsync();
        await service.GetAsync();

        Assert.Equal(1, this._probes);
    }

    [Fact]
    public async Task InvalidatingForcesAFreshRead()
    {
        var service = this.Service();

        await service.GetAsync();
        service.Invalidate();
        await service.GetAsync();

        Assert.Equal(2, this._probes);
    }

    [Fact]
    public void TheMinimumIs521()
    {
        Assert.Equal(new System.Version(5, 2, 1), PoracleServerProfile.MinimumSupported);
    }

    [Theory]
    [InlineData("5.1.0", true)]
    [InlineData("5.2.0", true)]
    [InlineData("5.0.9", true)]
    [InlineData("4.9.9", true)]
    [InlineData("5.2.1", false)]
    [InlineData("5.3.0", false)]
    [InlineData("6.0.0", false)]
    public void OnlyAVersionKnownToBeOlderCountsAsTooOld(string version, bool tooOld)
    {
        var profile = new PoracleServerProfile { Version = version, Reachable = true };

        Assert.Equal(tooOld, profile.IsBelowMinimum);
        Assert.Equal(!tooOld, profile.IsSupported);
        Assert.Equal(!tooOld, profile.SupportsV2Tracking);
    }

    [Theory]
    [InlineData("0.0.0")]      // ldflags not injected: a local build, not an ancient one
    [InlineData("")]
    [InlineData("dev")]
    [InlineData(null)]
    public void AVersionThatSaysNothingIsNotTreatedAsOld(string? version)
    {
        // Shouting "upgrade PoracleNG" at someone running a local build would teach them to ignore the
        // banner, and the banner has one job. Hiding features from it would be the same mistake quieter.
        var profile = new PoracleServerProfile { Version = version, Reachable = true };

        Assert.Null(profile.ParsedVersion);
        Assert.False(profile.IsBelowMinimum);
        Assert.True(profile.IsSupported);
        Assert.True(profile.SupportsV2Tracking);
    }

    [Fact]
    public void AnUnreachableServerIsNotSupportedWhateverItLastSaid()
    {
        var profile = new PoracleServerProfile { Version = "5.3.0", Reachable = false };

        Assert.False(profile.IsSupported);
        Assert.False(profile.SupportsV2Tracking);
    }

    [Theory]
    [InlineData("5.2.0-rc1", 5, 2, 0)]
    [InlineData("5.1.0", 5, 1, 0)]
    public void ASuffixedVersionStillParses(string raw, int major, int minor, int build)
    {
        var profile = new PoracleServerProfile { Version = raw, Reachable = true };

        Assert.Equal(new System.Version(major, minor, build), profile.ParsedVersion);
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

    [Fact]
    public async Task ManyCallersArrivingOnAColdCacheProbeOnce()
    {
        // AddHttpClient registers this transient, so every caller gets its own instance and the cache is
        // the only thing they share. A dashboard load fires several version-gated tracking writes at
        // once; without the gate each of them misses the same empty cache and runs its own /health GET.
        var probes = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                // Slow enough that every caller is genuinely in flight before the first one answers.
                Interlocked.Increment(ref probes);
                await Task.Delay(50);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HealthyResponse) };
            });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Poracle:ApiAddress"] = "http://poracle:3030" })
            .Build();
        var shared = new MemoryCache(new MemoryCacheOptions());

        PoracleServerProfileService Instance() => new(
            new HttpClient(handler.Object),
            shared,
            configuration,
            NullLogger<PoracleServerProfileService>.Instance);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => Instance().GetAsync())));

        Assert.Equal(1, probes);
        Assert.All(results, profile => Assert.Equal("5.1.0", profile.Version));
    }
}
