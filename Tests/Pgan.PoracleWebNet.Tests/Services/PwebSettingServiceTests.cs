using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PwebSettingServiceTests
{
    private readonly Mock<IPwebSettingRepository> _repository = new();
    private readonly PwebSettingService _sut;

    public PwebSettingServiceTests() => this._sut = new PwebSettingService(this._repository.Object);

    [Fact]
    public async Task GetAllAsyncReturnsSettings()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "key1", Value = "val1" },
            new() { Setting = "key2", Value = "val2" }
        ]);

        var result = (await this._sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByKeyAsyncFound()
    {
        this._repository.Setup(r => r.GetByKeyAsync("key1")).ReturnsAsync(new PwebSetting { Setting = "key1", Value = "val1" });
        var result = await this._sut.GetByKeyAsync("key1");
        Assert.NotNull(result);
        Assert.Equal("val1", result!.Value);
    }

    [Fact]
    public async Task GetByKeyAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByKeyAsync("unknown")).ReturnsAsync((PwebSetting?)null);
        Assert.Null(await this._sut.GetByKeyAsync("unknown"));
    }

    [Fact]
    public async Task CreateOrUpdateAsyncDelegates()
    {
        var setting = new PwebSetting { Setting = "key1", Value = "new" };
        this._repository.Setup(r => r.CreateOrUpdateAsync(setting)).ReturnsAsync(setting);
        var result = await this._sut.CreateOrUpdateAsync(setting);
        Assert.Equal("new", result.Value);
    }

    [Fact]
    public async Task DeleteAsyncReturnsTrue()
    {
        this._repository.Setup(r => r.DeleteAsync("key1")).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync("key1"));
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteAsync("unknown")).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync("unknown"));
    }
}
