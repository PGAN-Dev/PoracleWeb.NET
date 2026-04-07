using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class CrossProfileServiceTests
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly CrossProfileService _sut;

    public CrossProfileServiceTests() => this._sut = new CrossProfileService(this._proxy.Object, this._humanProxy.Object);

    [Fact]
    public async Task GetAllProfilesOverviewAsyncReturnsProxyResult()
    {
        var expected = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 1 } },
            raid = new[] { new { uid = 2 } }
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("u1")).ReturnsAsync(expected);

        var result = await this._sut.GetAllProfilesOverviewAsync("u1");

        Assert.Equal(expected.ToString(), result.ToString());
    }

    [Fact]
    public async Task GetAllProfilesOverviewAsyncPassesUserIdToProxy()
    {
        var json = CreateJsonObject(new
        {
        });
        this._proxy.Setup(p => p.GetAllTrackingAllProfilesAsync("user42")).ReturnsAsync(json);

        await this._sut.GetAllProfilesOverviewAsync("user42");

        this._proxy.Verify(p => p.GetAllTrackingAllProfilesAsync("user42"), Times.Once);
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
