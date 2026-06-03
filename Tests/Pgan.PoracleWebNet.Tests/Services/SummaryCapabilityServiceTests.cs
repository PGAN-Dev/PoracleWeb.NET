using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Capability resolution for quest summary delivery. The flag is read from PoracleNG's effective
/// config values (<c>tracking.quest_summary_enabled</c> via <c>/api/config/values</c>). It resolves
/// to <c>false</c> when the flag can't be determined (endpoint shape changed) or on any fault, so the
/// UI stays hidden unless the bot has the feature enabled. The result is cached for 5 minutes.
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
        this._apiProxy.Setup(p => p.GetQuestSummaryEnabledAsync()).ReturnsAsync(true);

        Assert.True(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task ReturnsFalseWhenFlagDisabled()
    {
        this._apiProxy.Setup(p => p.GetQuestSummaryEnabledAsync()).ReturnsAsync(false);

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task DefaultsToFalseWhenFlagCannotBeDetermined()
    {
        // Endpoint reachable but the flag isn't present in the expected shape -> null -> hidden,
        // rather than a dead-end where nothing is ever delivered.
        this._apiProxy.Setup(p => p.GetQuestSummaryEnabledAsync()).ReturnsAsync((bool?)null);

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task DegradesToFalseWhenProxyThrows()
    {
        this._apiProxy.Setup(p => p.GetQuestSummaryEnabledAsync()).ThrowsAsync(new HttpRequestException("upstream down"));

        Assert.False(await this._sut.IsQuestSummaryEnabledAsync());
    }

    [Fact]
    public async Task CachesResultAndDoesNotReprobe()
    {
        this._apiProxy.Setup(p => p.GetQuestSummaryEnabledAsync()).ReturnsAsync(true);

        var first = await this._sut.IsQuestSummaryEnabledAsync();
        var second = await this._sut.IsQuestSummaryEnabledAsync();

        Assert.True(first);
        Assert.True(second);
        this._apiProxy.Verify(p => p.GetQuestSummaryEnabledAsync(), Times.Once);
    }
}
