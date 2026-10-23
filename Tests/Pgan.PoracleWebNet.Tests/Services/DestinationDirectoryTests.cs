using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

public class DestinationDirectoryTests
{
    private readonly Mock<IPoracleHumanProxy> _proxy = new();

    private DestinationDirectory Create(IMemoryCache? cache = null) =>
        new(this._proxy.Object, cache ?? new MemoryCache(new MemoryCacheOptions()));

    private void SetupEach() =>
        this._proxy
            .Setup(p => p.ListHumansAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync((string? t, IReadOnlyCollection<string>? _) =>
                (IReadOnlyList<Human>)[new Human { Id = "id-" + t, Type = t }]);

    [Fact]
    public async Task ReadsEachDestinationTypeOnce()
    {
        this.SetupEach();
        var directory = this.Create();

        var first = await directory.GetAsync();
        var second = await directory.GetAsync();

        Assert.Equal(HumanKind.Destinations.Count, first.Count);
        Assert.Equal(first.Count, second.Count);
        this._proxy.Verify(
            p => p.ListHumansAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Exactly(6));
    }

    [Fact]
    public async Task NeverAsksForPeople()
    {
        this.SetupEach();

        await this.Create().GetAsync();

        this._proxy.Verify(
            p => p.ListHumansAsync("discord:user", It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Never);
        this._proxy.Verify(
            p => p.ListHumansAsync(null, It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task ADegradedReadIsNotCached()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        this._proxy
            .Setup(p => p.ListHumansAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>?>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var directory = this.Create(cache);

        await Assert.ThrowsAsync<HttpRequestException>(() => directory.GetAsync());

        this._proxy.Reset();
        this.SetupEach();
        var result = await directory.GetAsync();

        Assert.Equal(6, result.Count);
        this._proxy.Verify(
            p => p.ListHumansAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Exactly(6));
    }

    [Fact]
    public async Task InvalidateDropsTheCachedList()
    {
        this.SetupEach();
        var directory = this.Create();

        await directory.GetAsync();
        directory.Invalidate();
        await directory.GetAsync();

        this._proxy.Verify(
            p => p.ListHumansAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Exactly(12));
    }
}
