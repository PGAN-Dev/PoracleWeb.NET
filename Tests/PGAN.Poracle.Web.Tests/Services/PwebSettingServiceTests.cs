using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class PwebSettingServiceTests
{
    private readonly Mock<IPwebSettingRepository> _repository = new();
    private readonly PwebSettingService _sut;

    public PwebSettingServiceTests() => _sut = new PwebSettingService(_repository.Object);

    [Fact]
    public async Task GetAllAsync_ReturnsSettings()
    {
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PwebSetting>
        {
            new() { Setting = "key1", Value = "val1" },
            new() { Setting = "key2", Value = "val2" }
        });

        var result = (await _sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByKeyAsync_Found()
    {
        _repository.Setup(r => r.GetByKeyAsync("key1")).ReturnsAsync(new PwebSetting { Setting = "key1", Value = "val1" });
        var result = await _sut.GetByKeyAsync("key1");
        Assert.NotNull(result);
        Assert.Equal("val1", result!.Value);
    }

    [Fact]
    public async Task GetByKeyAsync_NotFound()
    {
        _repository.Setup(r => r.GetByKeyAsync("unknown")).ReturnsAsync((PwebSetting?)null);
        Assert.Null(await _sut.GetByKeyAsync("unknown"));
    }

    [Fact]
    public async Task CreateOrUpdateAsync_Delegates()
    {
        var setting = new PwebSetting { Setting = "key1", Value = "new" };
        _repository.Setup(r => r.CreateOrUpdateAsync(setting)).ReturnsAsync(setting);
        var result = await _sut.CreateOrUpdateAsync(setting);
        Assert.Equal("new", result.Value);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue() { _repository.Setup(r => r.DeleteAsync("key1")).ReturnsAsync(true); Assert.True(await _sut.DeleteAsync("key1")); }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse() { _repository.Setup(r => r.DeleteAsync("unknown")).ReturnsAsync(false); Assert.False(await _sut.DeleteAsync("unknown")); }
}
