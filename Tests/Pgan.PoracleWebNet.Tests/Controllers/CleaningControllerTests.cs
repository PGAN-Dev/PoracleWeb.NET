using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class CleaningControllerTests : ControllerTestBase
{
    private readonly Mock<ICleaningService> _service = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly CleaningController _sut;

    public CleaningControllerTests()
    {
        // Everything enabled unless a test says otherwise.
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._sut = new CleaningController(this._service.Object, this._featureGate.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetStatusKeepsTheAllCleanFlagsAndAddsCounts()
    {
        // Alarms D10. The booleans stay as they were (on only when every rule is clean); the counts are
        // added beside them so the page can say a type is partly clean.
        this._service.Setup(s => s.GetCleanCountsAsync("123456789", 1)).ReturnsAsync(new Dictionary<string, CleanCount>
        {
            ["monsters"] = new(1, 3),
            ["raids"] = new(2, 2),
            ["eggs"] = new(0, 0),
        });

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetStatus());
        var body = Assert.IsType<Dictionary<string, object>>(ok.Value);

        Assert.Equal(false, body["monsters"]);
        Assert.Equal(true, body["raids"]);
        Assert.Equal(false, body["eggs"]);
        var counts = Assert.IsType<Dictionary<string, CleanCount>>(body["counts"]);
        Assert.Equal(new CleanCount(1, 3), counts["monsters"]);
    }

    [Theory]
    [InlineData("monsters")]
    [InlineData("raids")]
    [InlineData("eggs")]
    [InlineData("quests")]
    [InlineData("invasions")]
    [InlineData("lures")]
    [InlineData("nests")]
    [InlineData("gyms")]
    public async Task ToggleCleanReturnsOkForAllAlarmTypes(string alarmType)
    {
        this._service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanRaidsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanEggsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanQuestsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanInvasionsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanLuresAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanNestsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));
        this._service.Setup(s => s.ToggleCleanGymsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(5));

        var result = await this._sut.ToggleClean(alarmType, 1);

        Assert.IsType<OkObjectResult>(result);
    }


    [Fact]
    public async Task ToggleCleanIsCaseInsensitive()
    {
        this._service.Setup(s => s.ToggleCleanMonstersAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(3));

        var result = await this._sut.ToggleClean("MONSTERS", 1);

        Assert.IsType<OkObjectResult>(result);
    }

    // --- Client input must not 500 (#418) ---

    [Theory]
    [InlineData("pokemon")]   // a natural guess for "monsters"
    [InlineData("monster")]
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task ToggleCleanRejectsAnUnknownAlarmTypeWithBadRequest(string alarmType)
    {
        var result = await this._sut.ToggleClean(alarmType, 1);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Unknown alarm type", System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
    }


    // --- Bulk toggle applied partially then 403'd (#402) ---

    [Fact]
    public async Task ToggleAllSkipsDisabledTypesInsteadOfFailingMidway()
    {
        SetupUser(this._sut);
        this._featureGate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
        this._featureGate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.Lures)).ReturnsAsync(false);
        this._service.Setup(s => s.ToggleCleanMonstersAsync(It.IsAny<string>(), It.IsAny<int>(), 1)).ReturnsAsync(new CleanToggleResult(3));

        var result = await this._sut.ToggleAll(1);

        Assert.IsType<OkObjectResult>(result);
        // The disabled type is skipped, not thrown on, so the rest still apply.
        this._service.Verify(s => s.ToggleCleanLuresAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        this._service.Verify(s => s.ToggleCleanMonstersAsync(It.IsAny<string>(), It.IsAny<int>(), 1), Times.Once);
        this._service.Verify(s => s.ToggleCleanGymsAsync(It.IsAny<string>(), It.IsAny<int>(), 1), Times.Once);
    }

    [Fact]
    public async Task ToggleCleanRejectsFortChangesWhichCannotStoreTheFlag()
    {
        SetupUser(this._sut);

        var result = await this._sut.ToggleClean("fortchanges", 1);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // enabled is a flag, not a bitmask. CleanFlags.Preserve masks with bit 1, so an even value such
    // as 2 silently DISABLED cleaning while reporting rows updated -- and the write rotates every uid,
    // deleting and reinserting for max battles. See #472.

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task ToggleCleanRejectsAnEnabledValueThatIsNotAFlag(int enabled)
    {
        var result = await this._sut.ToggleClean("monsters", enabled);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task ToggleAllRejectsAnEnabledValueThatIsNotAFlag(int enabled)
    {
        var result = await this._sut.ToggleAll(enabled);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ToggleCleanStillAcceptsBothFlagValues(int enabled)
    {
        var result = await this._sut.ToggleClean("monsters", enabled);

        Assert.IsNotType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ToggleClean_PairWarning_KeepsTheCountAndSetsTheHeader()
    {
        this._service.Setup(s => s.ToggleCleanRaidsAsync("123456789", 1, 1))
            .ReturnsAsync(new CleanToggleResult(4, new PairWarning(LinkSide.Telegram, "Couldn't reach Poracle for telegram.")));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.ToggleClean("raids", 1));

        Assert.Equal("""{"updated":4}""", System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.Contains("\"side\":\"telegram\"", this._sut.Response.Headers["X-Pair-Warning"].ToString());
    }

    [Fact]
    public async Task ToggleAll_PairWarningOnOneType_KeepsTheTotalAndSetsTheHeader()
    {
        this._service.Setup(s => s.ToggleCleanMonstersAsync(It.IsAny<string>(), It.IsAny<int>(), 1))
            .ReturnsAsync(new CleanToggleResult(3, new PairWarning(LinkSide.Telegram, "Couldn't reach Poracle for telegram.")));
        this._service.Setup(s => s.ToggleCleanRaidsAsync(It.IsAny<string>(), It.IsAny<int>(), 1)).ReturnsAsync(new CleanToggleResult(2));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.ToggleAll(1));

        Assert.Contains("\"updated\":5", System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.Contains("telegram", this._sut.Response.Headers["X-Pair-Warning"].ToString());
    }

    [Fact]
    public async Task ToggleClean_NoWarning_SetsNoHeader()
    {
        this._service.Setup(s => s.ToggleCleanRaidsAsync("123456789", 1, 1)).ReturnsAsync(new CleanToggleResult(4));

        await this._sut.ToggleClean("raids", 1);

        Assert.False(this._sut.Response.Headers.ContainsKey("X-Pair-Warning"));
    }
}
