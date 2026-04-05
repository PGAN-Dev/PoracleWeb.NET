using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class DashboardServiceTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly DashboardService _sut;

    public DashboardServiceTests() => this._sut = new DashboardService(this._proxy.Object);

    [Fact]
    public async Task GetCountsAsyncReturnsAllCounts()
    {
        var json = CreateAllTrackingJson(
            pokemon: 10, raid: 5, egg: 3, quest: 7,
            invasion: 2, lure: 4, nest: 1, gym: 6, maxbattle: 8);
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(10, result.Monsters);
        Assert.Equal(5, result.Raids);
        Assert.Equal(3, result.Eggs);
        Assert.Equal(7, result.Quests);
        Assert.Equal(2, result.Invasions);
        Assert.Equal(4, result.Lures);
        Assert.Equal(1, result.Nests);
        Assert.Equal(6, result.Gyms);
        Assert.Equal(8, result.MaxBattles);
    }

    [Fact]
    public async Task GetCountsAsyncReturnsZeroCountsWhenNoAlarms()
    {
        var json = CreateAllTrackingJson(
            pokemon: 0, raid: 0, egg: 0, quest: 0,
            invasion: 0, lure: 0, nest: 0, gym: 0, maxbattle: 0);
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(0, result.Monsters);
        Assert.Equal(0, result.Raids);
        Assert.Equal(0, result.MaxBattles);
    }

    [Fact]
    public async Task GetCountsAsyncHandlesMissingKeys()
    {
        // Empty JSON object -- no tracking types present
        using var doc = JsonDocument.Parse("{}");
        var json = doc.RootElement.Clone();
        this._proxy.Setup(p => p.GetAllTrackingAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetCountsAsync("u1", 1);

        Assert.Equal(0, result.Monsters);
        Assert.Equal(0, result.Raids);
        Assert.Equal(0, result.Eggs);
        Assert.Equal(0, result.Quests);
        Assert.Equal(0, result.Invasions);
        Assert.Equal(0, result.Lures);
        Assert.Equal(0, result.Nests);
        Assert.Equal(0, result.Gyms);
        Assert.Equal(0, result.MaxBattles);
    }

    /// <summary>
    /// Creates a JSON object with arrays of dummy items per tracking type.
    /// </summary>
    private static JsonElement CreateAllTrackingJson(
        int pokemon, int raid, int egg, int quest,
        int invasion, int lure, int nest, int gym, int maxbattle = 0)
    {
        var obj = new Dictionary<string, object[]>
        {
            ["pokemon"] = [.. Enumerable.Range(1, pokemon).Select(i => (object)new { uid = i })],
            ["raid"] = [.. Enumerable.Range(1, raid).Select(i => (object)new { uid = i })],
            ["egg"] = [.. Enumerable.Range(1, egg).Select(i => (object)new { uid = i })],
            ["quest"] = [.. Enumerable.Range(1, quest).Select(i => (object)new { uid = i })],
            ["invasion"] = [.. Enumerable.Range(1, invasion).Select(i => (object)new { uid = i })],
            ["lure"] = [.. Enumerable.Range(1, lure).Select(i => (object)new { uid = i })],
            ["nest"] = [.. Enumerable.Range(1, nest).Select(i => (object)new { uid = i })],
            ["gym"] = [.. Enumerable.Range(1, gym).Select(i => (object)new { uid = i })],
            ["maxbattle"] = [.. Enumerable.Range(1, maxbattle).Select(i => (object)new { uid = i })],
        };

        var jsonStr = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
