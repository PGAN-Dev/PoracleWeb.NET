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
    [InlineData("100%", "100|%")]
    [InlineData("a_b", "a|_b")]
    [InlineData("pipe|sep", "pipe||sep")]
    [InlineData("%_|", "|%|_||")]
    [InlineData("back\\slash", "back\\slash")] // backslash is no longer special
    public void EscapeLikePatternEscapesWildcardsAndEscapeChar(string input, string expected)
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

    // ── Gym area names on multi-part areas (#1117) ──────────────────────────

    private static Mock<IKojiService> KojiServing(params AdminGeofence[] fences)
    {
        var koji = new Mock<IKojiService>();
        koji.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync([.. fences]);
        return koji;
    }

    // Two squares far apart, with a bounding box covering both, as KojiService computes it.
    private static AdminGeofence SplitArea() => new()
    {
        Name = "split",
        Multipath = [[[0, 0], [0, 1], [1, 1], [1, 0]], [[10, 10], [10, 11], [11, 11], [11, 10]]],
        MinLat = 0,
        MaxLat = 11,
        MinLon = 0,
        MaxLon = 11,
    };

    [Fact]
    public async Task AGymInTheSecondPartOfAnAreaIsNamedForIt()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.SearchGymsAsync("abc", It.IsAny<int>()))
            .ReturnsAsync([new GymSearchResult { Id = "g1", Name = "Far Gym", Lat = 10.5, Lon = 10.5 }]);
        var sut = new ScannerController(this._logger.Object, service.Object, KojiServing(SplitArea()).Object);
        SetupUser(sut);

        var result = await sut.SearchGyms("abc");

        var gym = Assert.Single(Assert.IsType<IEnumerable<GymSearchResult>>(Assert.IsType<OkObjectResult>(result).Value, exactMatch: false));
        Assert.Equal("split", gym.Area);
    }

    [Fact]
    public async Task AGymBetweenThePartsOfAnAreaIsNotNamedForIt()
    {
        // Inside the bounding box, outside both parts: the box is only a pre-filter.
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetGymByIdAsync("g1"))
            .ReturnsAsync(new GymSearchResult { Id = "g1", Name = "Between", Lat = 5, Lon = 5 });
        var sut = new ScannerController(this._logger.Object, service.Object, KojiServing(SplitArea()).Object);
        SetupUser(sut);

        var gym = Assert.IsType<GymSearchResult>(Assert.IsType<OkObjectResult>(await sut.GetGymById("g1")).Value);

        Assert.Null(gym.Area);
    }

    [Fact]
    public async Task AGymInAnOrdinaryAreaIsStillNamedForIt()
    {
        var service = new Mock<IScannerService>();
        service.Setup(s => s.GetGymByIdAsync("g1"))
            .ReturnsAsync(new GymSearchResult { Id = "g1", Name = "Near Gym", Lat = 0.5, Lon = 0.5 });
        var plain = new AdminGeofence { Name = "plain", Path = [[0, 0], [0, 1], [1, 1], [1, 0]], MinLat = 0, MaxLat = 1, MinLon = 0, MaxLon = 1 };
        var sut = new ScannerController(this._logger.Object, service.Object, KojiServing(plain).Object);
        SetupUser(sut);

        var gym = Assert.IsType<GymSearchResult>(Assert.IsType<OkObjectResult>(await sut.GetGymById("g1")).Value);

        Assert.Equal("plain", gym.Area);
    }
}
