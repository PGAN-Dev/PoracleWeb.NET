using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ScannerControllerTests : ControllerTestBase
{
    private readonly Mock<ILogger<ScannerController>> _logger = new();

    [Fact]
    public async Task GetActiveQuestsReturnsNotFoundWhenScannerNotConfigured()
    {
        var sut = new ScannerController(this._logger.Object, scannerService: null);
        SetupUser(sut);

        var result = await sut.GetActiveQuests();

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task GetActiveQuestsReturnsOkWhenScannerConfigured()
    {
        var service = new Mock<IScannerService>();
        var quests = new List<QuestData> { new() { PokestopId = "stop1", Name = "Test Stop" } };
        service.Setup(s => s.GetActiveQuestsAsync()).ReturnsAsync(quests);
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveQuests();

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<IEnumerable<QuestData>>(ok.Value, exactMatch: false);
        Assert.Single(data);
    }

    [Fact]
    public async Task GetActiveQuestsReturnsOkWithEmptyWhenServiceThrows()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetActiveQuestsAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveQuests();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task GetActiveRaidsReturnsNotFoundWhenScannerNotConfigured()
    {
        var sut = new ScannerController(this._logger.Object, scannerService: null);
        SetupUser(sut);

        var result = await sut.GetActiveRaids();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetActiveRaidsReturnsOkWhenScannerConfigured()
    {
        var service = new Mock<IScannerService>();
        var raids = new List<RaidData> { new() { GymId = "gym1", Level = 5 } };
        service.Setup(s => s.GetActiveRaidsAsync()).ReturnsAsync(raids);
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveRaids();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(Assert.IsType<IEnumerable<RaidData>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task GetActiveRaidsReturnsOkWithEmptyWhenServiceThrows()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetActiveRaidsAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetActiveRaids();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task GetMaxBattlePokemonReturnsEmptyWhenScannerNotConfigured()
    {
        var sut = new ScannerController(this._logger.Object, scannerService: null);
        SetupUser(sut);

        var result = await sut.GetMaxBattlePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<int>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task GetMaxBattlePokemonReturnsEmptyWhenServiceThrows()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetMaxBattlePokemonIdsAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetMaxBattlePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<int>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task SearchGymsReturnsEmptyWhenScannerNotConfigured()
    {
        var sut = new ScannerController(this._logger.Object, scannerService: null);
        SetupUser(sut);

        var result = await sut.SearchGyms("abc");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    [InlineData(" a ")]
    public async Task SearchGymsReturnsEmptyWhenSearchBelowMin(string search)
    {
        var service = new Mock<IScannerService>();
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.SearchGyms(search);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
        service.Verify(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SearchGymsReturnsEmptyWhenSearchExceedsMax()
    {
        var service = new Mock<IScannerService>();
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);
        var longSearch = new string('a', 101);

        var result = await sut.SearchGyms(longSearch);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
        service.Verify(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(51, 50)]
    [InlineData(int.MaxValue, 50)]
    public async Task SearchGymsClampsLimitToValidRange(int requested, int expected)
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<GymSearchResult>());
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        await sut.SearchGyms("abc", requested);

        service.Verify(s => s.SearchGymsAsync("abc", expected), Times.Once);
    }

    [Fact]
    public async Task SearchGymsTrimsSearchBeforeQuerying()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<GymSearchResult>());
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        await sut.SearchGyms("   abc  ");

        service.Verify(s => s.SearchGymsAsync("abc", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SearchGymsReturnsEmptyWhenServiceThrows()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.SearchGyms("abc");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
    }

    [Fact]
    public async Task GetGymByIdReturnsNotFoundWhenScannerNotConfigured()
    {
        var sut = new ScannerController(this._logger.Object, scannerService: null);
        SetupUser(sut);

        var result = await sut.GetGymById("abc");

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetGymByIdReturnsNotFoundWhenIdEmpty(string? id)
    {
        var service = new Mock<IScannerService>();
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetGymById(id);

        Assert.IsType<NotFoundResult>(result);
        service.Verify(s => s.GetGymByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGymByIdReturnsNotFoundWhenIdExceedsMaxLength()
    {
        var service = new Mock<IScannerService>();
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetGymById(new string('x', 129));

        Assert.IsType<NotFoundResult>(result);
        service.Verify(s => s.GetGymByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGymByIdReturnsNotFoundWhenServiceReturnsNull()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetGymByIdAsync("abc")).ReturnsAsync((GymSearchResult?)null);
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetGymById("abc");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetGymByIdReturnsOkWhenServiceReturnsResult()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetGymByIdAsync("abc"))
            .ReturnsAsync(new GymSearchResult { Id = "abc", Name = "Test Gym" });
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetGymById("abc");

        var ok = Assert.IsType<OkObjectResult>(result);
        var gym = Assert.IsType<GymSearchResult>(ok.Value);
        Assert.Equal("abc", gym.Id);
    }

    [Fact]
    public async Task GetGymByIdReturnsNotFoundWhenServiceThrows()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetGymByIdAsync("abc")).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetGymById("abc");

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("%_\\", "\\%\\_\\\\")]
    public void EscapeLikePatternEscapesWildcardsAndBackslash(string input, string expected)
    {
        var actual = Core.Services.LikeEscape.Escape(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetMaxBattlePokemonReturnsOkWithIdsWhenServiceConfigured()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetMaxBattlePokemonIdsAsync()).ReturnsAsync(new[] { 150, 250, 384 });
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var result = await sut.GetMaxBattlePokemon();

        var ok = Assert.IsType<OkObjectResult>(result);
        var ids = Assert.IsType<IEnumerable<int>>(ok.Value, exactMatch: false).ToList();
        Assert.Equal(new[] { 150, 250, 384 }, ids);
    }

    [Fact]
    public async Task SearchGymsAcceptsUnicodeSearchAndForwardsTrimmedValue()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<GymSearchResult>());
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        // Mix of multi-byte, astral (surrogate pair), and ASCII with surrounding whitespace.
        var unicode = "  \u00e9 Caf\u00e9 \ud83d\ude00  ";

        await sut.SearchGyms(unicode);

        service.Verify(s => s.SearchGymsAsync(unicode.Trim(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task SearchGymsRejectsSearchExceedingMaxLengthAfterTrim()
    {
        var service = new Mock<IScannerService>();
        var sut = new ScannerController(this._logger.Object, service.Object);
        SetupUser(sut);

        var longPadded = "  " + new string('a', 150) + "  ";

        var result = await sut.SearchGyms(longPadded);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<IEnumerable<object>>(ok.Value, exactMatch: false));
        service.Verify(s => s.SearchGymsAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
