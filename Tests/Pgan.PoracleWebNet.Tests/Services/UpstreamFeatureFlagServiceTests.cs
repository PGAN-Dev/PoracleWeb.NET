using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Poracle's own per-type disable flags, read as <c>disable_*</c> keys. See #769.
/// </summary>
public class UpstreamFeatureFlagServiceTests
{
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /// <summary>
    /// Every test below assumes a PoracleNG new enough to serve pokestop events, with the feature on.
    /// Absent or unreadable is a different answer — see the showcase tests at the bottom — and left
    /// unstubbed it would leak <c>disable_showcase</c> into every assertion here.
    /// </summary>
    public UpstreamFeatureFlagServiceTests() =>
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync(false);

    private UpstreamFeatureFlagService CreateSut() =>
        new(this._proxy.Object, this._cache, NullLogger<UpstreamFeatureFlagService>.Instance);

    private void UpstreamHooks(params string[] hooks) =>
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { DisabledHooks = [.. hooks] });

    /// <summary>
    /// What prod serves. An empty array is a positive statement that nothing is disabled upstream,
    /// and must leave every type enabled rather than being read as "no data, assume the worst".
    /// </summary>
    [Fact]
    public async Task EmptyDisabledHooksDisablesNothing()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// The trap in the mapping table. <c>pokestop</c> reads like the parent hook for lures, invasions
    /// and quests, but nothing in the PoracleNG 5.1.0 processor consumes <c>DisablePokestop</c>, so
    /// mapping it would take three working types away for a flag that does nothing upstream.
    /// </summary>
    [Fact]
    public async Task PokestopInDisabledHooksDisablesNothing()
    {
        this.UpstreamHooks("pokestop");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>PoracleWeb has no weather alarms, so the hook has nowhere to land.</summary>
    [Fact]
    public async Task WeatherInDisabledHooksDisablesNothing()
    {
        this.UpstreamHooks("weather");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Theory]
    [InlineData("pokemon", DisableFeatureKeys.Pokemon)]
    [InlineData("raid", DisableFeatureKeys.Raids)]
    [InlineData("quest", DisableFeatureKeys.Quests)]
    [InlineData("invasion", DisableFeatureKeys.Invasions)]
    [InlineData("lure", DisableFeatureKeys.Lures)]
    [InlineData("nest", DisableFeatureKeys.Nests)]
    [InlineData("gym", DisableFeatureKeys.Gyms)]
    [InlineData("maxbattle", DisableFeatureKeys.MaxBattles)]
    public async Task EachMappedHookDisablesExactlyItsOwnKey(string hook, string expectedKey)
    {
        this.UpstreamHooks(hook);
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        var keys = await this.CreateSut().GetDisabledKeysAsync();

        Assert.Equal([expectedKey], keys);
    }

    [Fact]
    public async Task UnknownHookNamesAreIgnoredRatherThanGuessedAt()
    {
        this.UpstreamHooks("something_new_upstream");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    // --- fort changes: enforced upstream, but absent from disabledHooks ---

    [Fact]
    public async Task FortUpdateDisabledFlagDisablesFortChanges()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(true);

        Assert.Equal([DisableFeatureKeys.FortChanges], await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task UndeterminableFortUpdateFlagDisablesNothing()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// PoracleJS does not serve <c>/api/config/values</c> at all. Losing that read must not discard
    /// the hook list already in hand.
    /// </summary>
    [Fact]
    public async Task FailedFortUpdateReadKeepsTheHooksAlreadyResolved()
    {
        this.UpstreamHooks("lure");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ThrowsAsync(new HttpRequestException("no such route"));

        Assert.Equal([DisableFeatureKeys.Lures], await this.CreateSut().GetDisabledKeysAsync());
    }

    // --- degradation: the site settings must stay in sole charge ---

    /// <summary>
    /// An older Poracle or PoracleJS omits the field entirely. Absent is not "everything is off".
    /// </summary>
    [Fact]
    public async Task AbsentDisabledHooksFieldDisablesNothing()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig());
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task NullConfigDisablesNothing()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// Failing closed would let a Poracle outage disable every alarm type for every user - a far
    /// worse failure than the one this feature exists to prevent.
    /// </summary>
    [Fact]
    public async Task UnreachablePoracleDisablesNothing()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("connection refused"));
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ThrowsAsync(new HttpRequestException("connection refused"));

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task ResultIsCachedSoTheGateDoesNotCallUpstreamPerRequest()
    {
        this.UpstreamHooks("nest");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        var sut = this.CreateSut();

        await sut.GetDisabledKeysAsync();
        await sut.GetDisabledKeysAsync();
        await sut.GetDisabledKeysAsync();

        this._proxy.Verify(p => p.GetConfigAsync(), Times.Once);
        this._proxy.Verify(p => p.GetFortUpdateDisabledAsync(), Times.Once);
    }

    /// <summary>
    /// The cache is server-wide, not per-request: the service is scoped, so a fresh instance on the
    /// next request must still hit the cached value.
    /// </summary>
    [Fact]
    public async Task CacheIsSharedAcrossInstances()
    {
        this.UpstreamHooks("gym");
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);

        await this.CreateSut().GetDisabledKeysAsync();
        var second = await this.CreateSut().GetDisabledKeysAsync();

        Assert.Equal([DisableFeatureKeys.Gyms], second);
        this._proxy.Verify(p => p.GetConfigAsync(), Times.Once);
    }

    // ── disable_showcase: the one probe that fails closed ────────────────────

    /// <summary>
    /// The legitimate case. A 5.2.x server with the option present and off leaves the page available,
    /// and does not disable anything else on the way past.
    /// </summary>
    [Fact]
    public async Task ShowcaseOptionPresentAndOffLeavesPokestopEventsAvailable()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task ShowcaseOptionOnDisablesPokestopEvents()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync(true);

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// An absent option means a PoracleNG below 5.2.0, and every such server 404s the v2 incident
    /// route the page is built on. Verified: 5.1.0 has neither the option nor the route.
    /// </summary>
    [Fact]
    public async Task AbsentShowcaseOptionDisablesPokestopEvents()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// The deliberate inversion of this class's fail-open rule. Everywhere else an unreadable config
    /// leaves the site settings in charge; here it closes the gate, because a server whose config we
    /// cannot read is not one we can send v2 writes to either — and failing open would produce a page
    /// whose every call 404s.
    /// </summary>
    [Fact]
    public async Task UnreadableShowcaseOptionDisablesPokestopEvents()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ThrowsAsync(new HttpRequestException("boom"));

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// A hook list that could not be read used to abandon the whole probe. That would have skipped the
    /// showcase question entirely and left the page on for a server that cannot serve it.
    /// </summary>
    [Fact]
    public async Task FailedHookReadStillAnswersTheShowcaseQuestion()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("boom"));
        this._proxy.Setup(p => p.GetFortUpdateDisabledAsync()).ReturnsAsync(false);
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }
}
