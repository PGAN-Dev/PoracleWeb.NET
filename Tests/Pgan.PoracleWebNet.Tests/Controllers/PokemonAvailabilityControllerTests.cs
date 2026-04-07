using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class PokemonAvailabilityControllerTests : ControllerTestBase
{
    [Fact]
    public async Task GetAvailableWhenServiceIsNullReturnsEmptyWithDisabledFlag()
    {
        var sut = new PokemonAvailabilityController(availabilityService: null);
        SetupUser(sut);

        var result = await sut.GetAvailablePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var available = ok.Value.GetType().GetProperty("available")?.GetValue(ok.Value) as int[];
        var enabled = (bool?)ok.Value.GetType().GetProperty("enabled")?.GetValue(ok.Value);

        Assert.NotNull(available);
        Assert.Empty(available);
        Assert.False(enabled);
    }

    [Fact]
    public async Task GetAvailableWhenServiceReturnsDataReturnsAvailableWithEnabledFlag()
    {
        var service = new Mock<IPokemonAvailabilityService>();
        service.Setup(s => s.GetAvailablePokemonIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { 1, 4, 7, 25 }.AsReadOnly());

        var sut = new PokemonAvailabilityController(service.Object);
        SetupUser(sut);

        var result = await sut.GetAvailablePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var available = ok.Value.GetType().GetProperty("available")?.GetValue(ok.Value);
        var enabled = (bool?)ok.Value.GetType().GetProperty("enabled")?.GetValue(ok.Value);

        Assert.NotNull(available);
        var ids = Assert.IsType<IEnumerable<int>>(available, exactMatch: false);
        Assert.Equal([1, 4, 7, 25], [.. ids]);
        Assert.True(enabled);
    }

    [Fact]
    public async Task GetAvailableWhenServiceReturnsEmptyReturnsEmptyWithEnabledFlag()
    {
        var service = new Mock<IPokemonAvailabilityService>();
        service.Setup(s => s.GetAvailablePokemonIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>().AsReadOnly());

        var sut = new PokemonAvailabilityController(service.Object);
        SetupUser(sut);

        var result = await sut.GetAvailablePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var available = ok.Value.GetType().GetProperty("available")?.GetValue(ok.Value);
        var enabled = (bool?)ok.Value.GetType().GetProperty("enabled")?.GetValue(ok.Value);

        Assert.NotNull(available);
        var ids = Assert.IsType<IEnumerable<int>>(available, exactMatch: false);
        Assert.Empty(ids);
        Assert.True(enabled);
    }
}
