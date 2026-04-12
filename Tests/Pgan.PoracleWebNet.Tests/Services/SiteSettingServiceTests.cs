using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class SiteSettingServiceTests
{
    private readonly Mock<ISiteSettingRepository> _repository = new();
    private readonly SiteSettingService _sut;

    public SiteSettingServiceTests() => this._sut = new SiteSettingService(this._repository.Object, Mock.Of<ILogger<SiteSettingService>>());

    [Fact]
    public async Task GetAllAsyncReturnsAllSettings()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "key1", Value = "val1", Category = "general" },
            new() { Key = "key2", Value = "val2", Category = "branding" },
            new() { Key = "key3", Value = "val3", Category = "features" }
        ]);

        var result = (await this._sut.GetAllAsync()).ToList();

        Assert.Equal(3, result.Count);
        this._repository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetByCategoryAsyncFiltersByCategory()
    {
        var brandingSettings = new List<SiteSetting>
        {
            new() { Key = "custom_title", Value = "My App", Category = "branding" },
            new() { Key = "custom_logo_url", Value = "https://logo.png", Category = "branding" }
        };
        this._repository.Setup(r => r.GetByCategoryAsync("branding")).ReturnsAsync(brandingSettings);

        var result = (await this._sut.GetByCategoryAsync("branding")).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal("branding", s.Category));
        this._repository.Verify(r => r.GetByCategoryAsync("branding"), Times.Once);
    }

    [Fact]
    public async Task GetPublicAsyncReturnsOnlyPublicKeys()
    {
        this._repository.Setup(r => r.GetByKeyAsync("custom_title"))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My App", Category = "branding" });

        var result = (await this._sut.GetPublicAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("custom_title", result[0].Key);
        this._repository.Verify(r => r.GetByKeyAsync("custom_title"), Times.Once);
    }

    [Fact]
    public async Task GetPublicAsyncIncludesLoginMethodSettings()
    {
        this._repository.Setup(r => r.GetByKeyAsync("custom_title"))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My App", Category = "branding" });
        this._repository.Setup(r => r.GetByKeyAsync("enable_discord"))
            .ReturnsAsync(new SiteSetting { Key = "enable_discord", Value = "True", Category = "discord" });
        this._repository.Setup(r => r.GetByKeyAsync("enable_telegram"))
            .ReturnsAsync(new SiteSetting { Key = "enable_telegram", Value = "False", Category = "telegram" });

        var result = (await this._sut.GetPublicAsync()).ToList();

        Assert.Equal(3, result.Count);
        Assert.Contains(result, s => s.Key == "custom_title");
        Assert.Contains(result, s => s.Key == "enable_discord");
        Assert.Contains(result, s => s.Key == "enable_telegram");
    }

    [Fact]
    public async Task GetPublicAsyncIncludesSignupUrlSetting()
    {
        this._repository.Setup(r => r.GetByKeyAsync("signup_url"))
            .ReturnsAsync(new SiteSetting { Key = "signup_url", Value = "https://signup.example.com", Category = "features" });

        var result = (await this._sut.GetPublicAsync()).ToList();

        Assert.Contains(result, s => s.Key == "signup_url");
        Assert.Equal("https://signup.example.com", result.First(s => s.Key == "signup_url").Value);
    }

    [Fact]
    public async Task GetPublicAsyncSkipsMissingLoginMethodSettings()
    {
        // Only custom_title exists; enable_discord and enable_telegram are absent from DB
        this._repository.Setup(r => r.GetByKeyAsync("custom_title"))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My App", Category = "branding" });
        this._repository.Setup(r => r.GetByKeyAsync("enable_discord")).ReturnsAsync((SiteSetting?)null);
        this._repository.Setup(r => r.GetByKeyAsync("enable_telegram")).ReturnsAsync((SiteSetting?)null);

        var result = (await this._sut.GetPublicAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("custom_title", result[0].Key);
    }

    [Fact]
    public async Task GetByKeyAsyncReturnsMatchingSetting()
    {
        var expected = new SiteSetting { Key = "custom_title", Value = "My App", Category = "branding" };
        this._repository.Setup(r => r.GetByKeyAsync("custom_title")).ReturnsAsync(expected);

        var result = await this._sut.GetByKeyAsync("custom_title");

        Assert.NotNull(result);
        Assert.Equal("custom_title", result!.Key);
        Assert.Equal("My App", result.Value);
    }

    [Fact]
    public async Task GetByKeyAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByKeyAsync("unknown")).ReturnsAsync((SiteSetting?)null);

        var result = await this._sut.GetByKeyAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValueAsyncReturnsValue()
    {
        this._repository.Setup(r => r.GetByKeyAsync("custom_title"))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My App" });

        var result = await this._sut.GetValueAsync("custom_title");

        Assert.Equal("My App", result);
    }

    [Fact]
    public async Task GetValueAsyncReturnsNullWhenNotFound()
    {
        this._repository.Setup(r => r.GetByKeyAsync("unknown")).ReturnsAsync((SiteSetting?)null);

        var result = await this._sut.GetValueAsync("unknown");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public async Task GetBoolAsyncReturnsTrueForTrueString(string value)
    {
        this._repository.Setup(r => r.GetByKeyAsync("enable_feature"))
            .ReturnsAsync(new SiteSetting { Key = "enable_feature", Value = value });

        var result = await this._sut.GetBoolAsync("enable_feature");

        Assert.True(result);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("anything_else")]
    public async Task GetBoolAsyncReturnsFalseForFalseString(string value)
    {
        this._repository.Setup(r => r.GetByKeyAsync("enable_feature"))
            .ReturnsAsync(new SiteSetting { Key = "enable_feature", Value = value });

        var result = await this._sut.GetBoolAsync("enable_feature");

        Assert.False(result);
    }

    [Fact]
    public async Task GetBoolAsyncReturnsFalseWhenNotFound()
    {
        this._repository.Setup(r => r.GetByKeyAsync("unknown")).ReturnsAsync((SiteSetting?)null);

        var result = await this._sut.GetBoolAsync("unknown");

        Assert.False(result);
    }

    [Fact]
    public async Task CreateOrUpdateAsyncDelegatesToRepository()
    {
        var setting = new SiteSetting { Key = "new_key", Value = "new_value", Category = "general" };
        this._repository.Setup(r => r.CreateOrUpdateAsync(setting)).ReturnsAsync(setting);

        var result = await this._sut.CreateOrUpdateAsync(setting);

        Assert.Equal("new_value", result.Value);
        this._repository.Verify(r => r.CreateOrUpdateAsync(setting), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncDelegatesToRepository()
    {
        this._repository.Setup(r => r.DeleteAsync("key1")).ReturnsAsync(true);

        var result = await this._sut.DeleteAsync("key1");

        Assert.True(result);
        this._repository.Verify(r => r.DeleteAsync("key1"), Times.Once);
    }
}
