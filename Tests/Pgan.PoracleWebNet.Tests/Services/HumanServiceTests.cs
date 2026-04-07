using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class HumanServiceTests
{
    private readonly Mock<IHumanRepository> _repository = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _trackingProxy = new();
    private readonly HumanService _sut;

    public HumanServiceTests() => this._sut = new HumanService(
        this._repository.Object,
        this._humanProxy.Object,
        this._trackingProxy.Object);

    [Fact]
    public async Task GetAllAsyncReturnsHumansFromRepository()
    {
        // GetAllAsync still uses direct DB (no proxy equivalent)
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new() { Id = "u1", Name = "User1" },
            new() { Id = "u2", Name = "User2" }
        ]);

        var result = (await this._sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsHumanFromProxy()
    {
        var json = CreateHumanJson("u1", "User1");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetByIdAsync("u1");
        Assert.NotNull(result);
        Assert.Equal("u1", result!.Id);
        Assert.Equal("User1", result.Name);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullWhenProxyReturnsNull()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("unknown")).ReturnsAsync((JsonElement?)null);
        Assert.Null(await this._sut.GetByIdAsync("unknown"));
    }

    [Fact]
    public async Task GetByIdAsyncThrowsOnProxyError()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.GetByIdAsync("u1"));
    }

    [Fact]
    public async Task GetByIdAndProfileAsyncReturnsHumanWhenProfileMatches()
    {
        var json = CreateHumanJson("u1", "User1", currentProfileNo: 1);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetByIdAndProfileAsync("u1", 1);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetByIdAndProfileAsyncReturnsNullWhenProfileDoesNotMatch()
    {
        var json = CreateHumanJson("u1", "User1", currentProfileNo: 1);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetByIdAndProfileAsync("u1", 99);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsyncUsesProxy()
    {
        var human = new Human { Id = "u1", Name = "New", Type = "discord:user" };
        this._humanProxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>())).Returns(Task.CompletedTask);

        // After create, re-fetch returns the human
        var json = CreateHumanJson("u1", "New");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.CreateAsync(human);
        Assert.Equal("New", result.Name);
        this._humanProxy.Verify(p => p.CreateHumanAsync(It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncDelegatesToRepository()
    {
        // UpdateAsync still uses direct DB for general updates
        var human = new Human { Id = "u1", Name = "Updated" };
        this._repository.Setup(r => r.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.UpdateAsync(human);
        this._repository.Verify(r => r.UpdateAsync(human), Times.Once);
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueViaProxy()
    {
        var json = CreateHumanJson("u1", "User1");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);
        Assert.True(await this._sut.ExistsAsync("u1"));
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseViaProxy()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("unknown")).ReturnsAsync((JsonElement?)null);
        Assert.False(await this._sut.ExistsAsync("unknown"));
    }

    [Fact]
    public async Task ExistsAsyncThrowsOnProxyError()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.ExistsAsync("u1"));
    }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsyncUsesTrackingProxy()
    {
        // Set up tracking responses with UIDs for each alarm type
        var pokemonJson = CreateTrackingArray([1, 2]);
        var raidJson = CreateTrackingArray([3]);
        var emptyJson = CreateTrackingArray([]);

        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(pokemonJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(raidJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("quest", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("nest", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(emptyJson);

        var count = await this._sut.DeleteAllAlarmsByUserAsync("u1");

        Assert.Equal(3, count);
        this._trackingProxy.Verify(p => p.BulkDeleteByUidsAsync("pokemon", "u1", It.Is<IEnumerable<int>>(uids => uids.Count() == 2)), Times.Once);
        this._trackingProxy.Verify(p => p.BulkDeleteByUidsAsync("raid", "u1", It.Is<IEnumerable<int>>(uids => uids.Count() == 1)), Times.Once);
    }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsyncThrowsOnProxyError()
    {
        this._trackingProxy.Setup(p => p.GetByUserAsync(It.IsAny<string>(), "u1"))
            .ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.DeleteAllAlarmsByUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncDelegatesToRepository()
    {
        this._repository.Setup(r => r.DeleteUserAsync("u1")).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteUserAsync("unknown")).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteUserAsync("unknown"));
    }

    /// <summary>
    /// Creates a JsonElement representing a human record from the PoracleNG API (snake_case).
    /// </summary>
    private static JsonElement CreateHumanJson(string id, string name, int enabled = 1, int adminDisable = 0, int currentProfileNo = 1)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            name,
            type = "discord:user",
            enabled,
            admin_disable = adminDisable,
            current_profile_no = currentProfileNo,
            area = "[]",
            latitude = 0.0,
            longitude = 0.0,
            fails = 0,
            language = "en",
            community_membership = (string?)null,
        });

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Creates a JsonElement representing a tracking array response with the given UIDs.
    /// </summary>
    private static JsonElement CreateTrackingArray(int[] uids)
    {
        var items = uids.Select(uid => new { uid }).ToArray();
        var json = JsonSerializer.Serialize(items);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
