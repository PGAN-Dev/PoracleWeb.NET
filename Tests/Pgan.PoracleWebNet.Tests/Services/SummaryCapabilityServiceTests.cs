using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Capability resolution for quest summary delivery. The flag is read from the config proxy
/// (<c>tracking.quest_summary_enabled</c>), defaults to <c>true</c> when the field is absent from
/// a successful config, degrades to <c>false</c> on any fault, and is cached for 5 minutes.
/// </summary>
public class SummaryCapabilityServiceTests : IDisposable
{
    private readonly Mock<IPoracleApiProxy> _apiProxy = new();

    // Real MemoryCache per test instance — xUnit gives each fact a fresh class instance, so cache
    // state never leaks across tests.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SummaryCapabilityService _sut;

    public SummaryCapabilityServiceTests() => this._sut = new SummaryCapabilityService(this._apiProxy.Object, this._cache);

    public void Dispose()
    {
        this._cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ReturnsTrueWhenFlagEnabled()
    {
        this._apiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { QuestSummaryEnabled = true });

        Assert.True(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task ReturnsFalseWhenFlagDisabled()
    {
        this._apiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { QuestSummaryEnabled = false });

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task DefaultsToTrueWhenFieldAbsentFromSuccessfulConfig()
    {
        // A successful config with no tracking.quest_summary_enabled leaves PoracleConfig's default (true).
        this._apiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig());

        Assert.True(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task DegradesToFalseWhenConfigIsNull()
    {
        this._apiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task DegradesToFalseWhenProxyThrows()
    {
        this._apiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("upstream down"));

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task CachesResultAndDoesNotReprobe()
    {
        this._apiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { QuestSummaryEnabled = true });

        var first = await this._sut.IsQuestSummaryEnabledAsync();
        var second = await this._sut.IsQuestSummaryEnabledAsync();

        Assert.True(first);
        Assert.True(second);
        this._apiProxy.Verify(p => p.GetConfigAsync(), Times.Once);
    }
}
