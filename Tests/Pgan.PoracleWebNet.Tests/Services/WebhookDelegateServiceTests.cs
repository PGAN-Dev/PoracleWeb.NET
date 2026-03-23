using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class WebhookDelegateServiceTests
{
    private readonly Mock<IWebhookDelegateRepository> _repository = new();
    private readonly WebhookDelegateService _sut;

    public WebhookDelegateServiceTests() => this._sut = new WebhookDelegateService(this._repository.Object, Mock.Of<ILogger<WebhookDelegateService>>());

    [Fact]
    public async Task GetAllGroupedAsyncGroupsByWebhookId()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new() { WebhookId = "wh1", UserId = "u1" },
            new() { WebhookId = "wh1", UserId = "u2" },
            new() { WebhookId = "wh2", UserId = "u3" }
        ]);

        var result = await this._sut.GetAllGroupedAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("wh1"));
        Assert.True(result.ContainsKey("wh2"));
        Assert.Equal(2, result["wh1"].Length);
        Assert.Contains("u1", result["wh1"]);
        Assert.Contains("u2", result["wh1"]);
        Assert.Single(result["wh2"]);
        Assert.Contains("u3", result["wh2"]);
    }

    [Fact]
    public async Task GetAllGroupedAsyncReturnsEmptyWhenNoDelegates()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync([]);

        var result = await this._sut.GetAllGroupedAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDelegatesForWebhookAsyncReturnsUserIds()
    {
        this._repository.Setup(r => r.GetByWebhookIdAsync("wh1")).ReturnsAsync(
        [
            new() { WebhookId = "wh1", UserId = "u1" },
            new() { WebhookId = "wh1", UserId = "u2" }
        ]);

        var result = await this._sut.GetDelegatesForWebhookAsync("wh1");

        Assert.Equal(2, result.Length);
        Assert.Contains("u1", result);
        Assert.Contains("u2", result);
    }

    [Fact]
    public async Task GetManagedWebhookIdsAsyncReturnsWebhookIds()
    {
        this._repository.Setup(r => r.GetWebhookIdsByUserIdAsync("u1")).ReturnsAsync(["wh1", "wh2"]);

        var result = (await this._sut.GetManagedWebhookIdsAsync("u1")).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains("wh1", result);
        Assert.Contains("wh2", result);
    }

    [Fact]
    public async Task AddDelegateAsyncAddsAndReturnsUpdatedList()
    {
        this._repository.Setup(r => r.AddAsync("wh1", "u2"))
            .ReturnsAsync(new WebhookDelegate { WebhookId = "wh1", UserId = "u2" });
        this._repository.Setup(r => r.GetByWebhookIdAsync("wh1")).ReturnsAsync(
        [
            new() { WebhookId = "wh1", UserId = "u1" },
            new() { WebhookId = "wh1", UserId = "u2" }
        ]);

        var result = await this._sut.AddDelegateAsync("wh1", "u2");

        Assert.Equal(2, result.Length);
        Assert.Contains("u1", result);
        Assert.Contains("u2", result);
    }

    [Fact]
    public async Task RemoveDelegateAsyncRemovesAndReturnsUpdatedList()
    {
        this._repository.Setup(r => r.RemoveAsync("wh1", "u1")).ReturnsAsync(true);
        this._repository.Setup(r => r.GetByWebhookIdAsync("wh1")).ReturnsAsync(
        [
            new() { WebhookId = "wh1", UserId = "u2" }
        ]);

        var result = await this._sut.RemoveDelegateAsync("wh1", "u1");

        Assert.Single(result);
        Assert.Contains("u2", result);
        this._repository.Verify(r => r.RemoveAsync("wh1", "u1"), Times.Once);
    }
}
