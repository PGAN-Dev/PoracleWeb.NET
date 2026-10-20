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

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// The trap in the mapping table. <c>pokestop</c> reads like the parent hook for lures, invasions
    /// and quests, but nothing in the PoracleNG 5.1.0 processor consumed <c>DisablePokestop</c>, and 5.2.1
    /// deprecated it, so mapping it would take three working types away for a flag that does nothing.
    /// </summary>
    [Fact]
    public async Task PokestopInDisabledHooksDisablesNothing()
    {
        this.UpstreamHooks("pokestop");

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>PoracleWeb has no weather alarms, so the hook has nowhere to land.</summary>
    [Fact]
    public async Task WeatherInDisabledHooksDisablesNothing()
    {
        this.UpstreamHooks("weather");

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

        var keys = await this.CreateSut().GetDisabledKeysAsync();

        Assert.Equal([expectedKey], keys);
    }

    [Fact]
    public async Task UnknownHookNamesAreIgnoredRatherThanGuessedAt()
    {
        this.UpstreamHooks("something_new_upstream");

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    // --- fort: reported in disabledHooks from 5.2.1 on ---

    /// <summary>
    /// The upstream fix. <c>fort</c> joined <c>hookTypes</c> in PoracleNG 5.2.1, named to match the
    /// tracking type, so the flag arrives in the array like every other one.
    /// </summary>
    [Fact]
    public async Task FortInDisabledHooksDisablesFortChanges()
    {
        this.UpstreamHooks("fort");

        Assert.Equal([DisableFeatureKeys.FortChanges], await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// <c>general.disable_fort_update</c> used to be read from <c>/api/config/values</c> for a server too
    /// old to put <c>fort</c> in <c>disabledHooks</c> (5.1.0). 5.2.1 is the minimum, so the hook list and
    /// the showcase flag are the only two reads; a third would be a round-trip on the gate's hot path for
    /// a server that is not supported.
    /// </summary>
    [Fact]
    public async Task OnlyTheHookListAndTheShowcaseFlagAreRead()
    {
        this.UpstreamHooks();

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());

        this._proxy.Verify(p => p.GetConfigAsync(), Times.Once);
        this._proxy.Verify(p => p.GetShowcaseDisabledAsync(), Times.Once);
        this._proxy.VerifyNoOtherCalls();
    }

    // --- degradation: the site settings must stay in sole charge ---

    /// <summary>
    /// An older Poracle or PoracleJS omits the field entirely. Absent is not "everything is off".
    /// </summary>
    [Fact]
    public async Task AbsentDisabledHooksFieldDisablesNothing()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig());

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task NullConfigDisablesNothing()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);

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

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task ResultIsCachedSoTheGateDoesNotCallUpstreamPerRequest()
    {
        this.UpstreamHooks("nest");
        var sut = this.CreateSut();

        await sut.GetDisabledKeysAsync();
        await sut.GetDisabledKeysAsync();
        await sut.GetDisabledKeysAsync();

        this._proxy.Verify(p => p.GetConfigAsync(), Times.Once);
    }

    /// <summary>
    /// The cache is server-wide, not per-request: the service is scoped, so a fresh instance on the
    /// next request must still hit the cached value.
    /// </summary>
    [Fact]
    public async Task CacheIsSharedAcrossInstances()
    {
        this.UpstreamHooks("gym");

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
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync(false);

        Assert.Empty(await this.CreateSut().GetDisabledKeysAsync());
    }

    [Fact]
    public async Task ShowcaseOptionOnDisablesPokestopEvents()
    {
        this.UpstreamHooks();
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync(true);

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }

    /// <summary>
    /// An absent option means a PoracleNG below 5.2.0, which is unsupported, and every such server 404s
    /// the v2 incident route the page is built on. Verified: 5.1.0 has neither the option nor the route.
    /// </summary>
    [Fact]
    public async Task AbsentShowcaseOptionDisablesPokestopEvents()
    {
        this.UpstreamHooks();
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
        this._proxy.Setup(p => p.GetShowcaseDisabledAsync()).ReturnsAsync((bool?)null);

        Assert.Equal([DisableFeatureKeys.PokestopEvents], await this.CreateSut().GetDisabledKeysAsync());
    }
}
