using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Capability resolution for pokemon edit-in-place (jfberry/PoracleNG#246). The flag is read from
/// PoracleNG's effective config values (<c>tracking.pokemon_edit</c> via <c>/api/config/values</c>).
/// It resolves to <c>false</c> when the flag can't be determined (older server, endpoint shape
/// changed) or on any fault, so the UI stays hidden unless the bot actually honours the bit. The
/// result is cached for 5 minutes.
/// </summary>
public class PokemonEditCapabilityServiceTests : IDisposable
{
    private readonly Mock<IPoracleApiProxy> _apiProxy = new();

    // Real MemoryCache per test instance — xUnit gives each fact a fresh class instance, so cache
    // state never leaks across tests.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly PokemonEditCapabilityService _sut;

    public PokemonEditCapabilityServiceTests() => this._sut = new PokemonEditCapabilityService(this._apiProxy.Object, this._cache);

    public void Dispose()
    {
        this._cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ReturnsTrueWhenFlagEnabled()
    {
        this._apiProxy.Setup(p => p.GetPokemonEditEnabledAsync()).ReturnsAsync(true);

        Assert.True(await this._sut.IsPokemonEditEnabledAsync());
    }

    [Fact]
    public async Task ReturnsFalseWhenOperatorDisabled()
    {
        // tracking.pokemon_edit = false: the key exists (PoracleNG has #246) but the operator turned
        // it off, so the bit would still be a no-op.
        this._apiProxy.Setup(p => p.GetPokemonEditEnabledAsync()).ReturnsAsync(false);

        Assert.False(await this._sut.IsPokemonEditEnabledAsync());
    }

    [Fact]
    public async Task DefaultsToFalseWhenFlagCannotBeDetermined()
    {
        // Key absent — a PoracleNG that predates #246, where the bit is storable but inert.
        this._apiProxy.Setup(p => p.GetPokemonEditEnabledAsync()).ReturnsAsync((bool?)null);

        Assert.False(await this._sut.IsPokemonEditEnabledAsync());
    }

    [Fact]
    public async Task DegradesToFalseWhenProxyThrows()
    {
        this._apiProxy.Setup(p => p.GetPokemonEditEnabledAsync()).ThrowsAsync(new HttpRequestException("upstream down"));

        Assert.False(await this._sut.IsPokemonEditEnabledAsync());
    }

    [Fact]
    public async Task CachesResultAndDoesNotReprobe()
    {
        this._apiProxy.Setup(p => p.GetPokemonEditEnabledAsync()).ReturnsAsync(true);

        var first = await this._sut.IsPokemonEditEnabledAsync();
        var second = await this._sut.IsPokemonEditEnabledAsync();

        Assert.True(first);
        Assert.True(second);
        this._apiProxy.Verify(p => p.GetPokemonEditEnabledAsync(), Times.Once);
    }
}
