using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG's <c>[tracking] max_areas</c> (jfberry/PoracleNG#346) as PoracleWeb.NET enforces it. PoracleNG lifts the
/// limit for a <c>trusted</c> write, and every area write this application sends is trusted, so without this check a
/// user held to five areas in the bot could pick any number on the web.
/// </summary>
/// <remarks>
/// The rule mirrors PoracleNG's <c>AreaLimitBlocks</c> and <c>LimitExempt</c>: a new list must fit; a list already
/// over the limit (stored before it was lowered) may be kept or shrunk, never grown; PoracleNG's admins and every
/// destination that isn't a person are exempt. Most tests here are the cases that must still pass.
/// </remarks>
public class AreaLimitTests
{
    private const string Human = "u1";
    private readonly Mock<IPoracleHumanProxy> _humans = new();
    private readonly Mock<IUserGeofenceRepository> _fences = new();
    private readonly Mock<IPoracleApiProxy> _config = new();

    public AreaLimitTests()
    {
        this.Limit(3);
        this.HumanType("discord:user");
        this._humans.Setup(p => p.GetAvailableAreasAsync(Human))
            .ReturnsAsync(new[] { "a", "b", "c", "d", "e" }.Select(n => new V2AreaInfo(n, true, false)).ToList());
        this._fences.Setup(r => r.GetByHumanIdAsync(Human)).ReturnsAsync([]);
    }

    private UserAreaPolicy Sut() => new(this._humans.Object, this._fences.Object, poracleApiProxy: this._config.Object);

    private void Limit(int max, PoracleAdmins? admins = null) =>
        this._config.Setup(c => c.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleConfig { MaxAreas = max, Admins = admins });

    private void HumanType(string type) =>
        this._humans.Setup(p => p.GetHumanAsync(Human))
            .ReturnsAsync(JsonDocument.Parse($$"""{"id":"{{Human}}","type":"{{type}}"}""").RootElement.Clone());

    private static Func<Task<IReadOnlyCollection<string>>> Was(params string[] names) =>
        () => Task.FromResult<IReadOnlyCollection<string>>(names);

    // ---- the refusals -------------------------------------------------------------------------------

    [Fact]
    public async Task NewListOverTheLimitRefused()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], previous: null));

        Assert.Contains("at most 3 areas", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrowingAListAlreadyOverTheLimitRefused()
    {
        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d", "e"], Was("a", "b", "c", "d")));
    }

    [Fact]
    public async Task SwappingANameOnAListOverTheLimitRefused()
    {
        // Same count, but "e" is new: PoracleNG allows only names from the list being replaced.
        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "e"], Was("a", "b", "c", "d")));
    }

    [Fact]
    public async Task NewAlarmOverTheLimitRefusedWithTheAlarmMessage()
    {
        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Sut().EnsureOverrideAreasWithinLimitAsync(Human, ["a", "b", "c", "d"], storedOnRule: null));

        Assert.Equal("An alarm can be limited to at most 3 areas.", ex.Message);
    }

    [Fact]
    public async Task AdminManagingSomeoneElseIsHeldToThatPersonsLimit()
    {
        // PoracleNG judges the human written to; the person signed in doesn't come into it.
        this.Limit(3, new PoracleAdmins { Discord = ["admin1"] });

        await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], previous: null));
    }

    // ---- what must still pass -----------------------------------------------------------------------

    [Fact]
    public async Task AtTheLimitAllowedWithoutReadingTheHuman()
    {
        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c"], previous: null);

        this._humans.Verify(p => p.GetHumanAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task KeepingAListAlreadyOverTheLimitAllowed()
    {
        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], Was("a", "b", "c", "d"));
    }

    [Fact]
    public async Task ShrinkingAListAlreadyOverTheLimitAllowed()
    {
        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], Was("a", "b", "c", "d", "e"));
    }

    [Fact]
    public async Task EditKeepingTheStoredOverrideListAllowed()
    {
        await this.Sut().EnsureOverrideAreasWithinLimitAsync(Human, ["A", "b_c", "d", "e"], Was("a", "b c", "d", "e"));
    }

    [Fact]
    public async Task DuplicatesAndSpellingsCountOnce()
    {
        // PoracleNG keys names lowercase with _ and space equal, and counts each key once.
        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["Old Town", "old_town", "a", "b", " A "], previous: null);
    }

    [Theory]
    [InlineData("discord:channel")]
    [InlineData("discord:thread")]
    [InlineData("webhook")]
    [InlineData("telegram:group")]
    public async Task DestinationsThatArentPeopleAreExempt(string type)
    {
        this.HumanType(type);

        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], previous: null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PoracleAdminsAreExempt(bool discord)
    {
        this.Limit(3, discord ? new PoracleAdmins { Discord = [Human] } : new PoracleAdmins { Telegram = [Human] });

        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], previous: null);
    }

    [Fact]
    public async Task NoLimitConfiguredAllowsAnyNumber()
    {
        this.Limit(0);

        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d", "e"], previous: null);
    }

    [Fact]
    public async Task UnreadableConfigFailsOpen()
    {
        this._config.Setup(c => c.GetConfigAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await this.Sut().EnsureAreaListWithinLimitAsync(Human, ["a", "b", "c", "d"], previous: null);
    }

    [Fact]
    public async Task ConfigIsReadOncePerRequest()
    {
        // A profile import checks every rule it creates.
        var sut = this.Sut();
        for (var i = 0; i < 5; i++)
        {
            await sut.EnsureOverrideAreasWithinLimitAsync(Human, ["a", "b"], storedOnRule: null);
        }

        this._config.Verify(c => c.GetConfigAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- the area writer ----------------------------------------------------------------------------

    private UserAreaWriter Writer() =>
        new(this._humans.Object, this._fences.Object, Mock.Of<IPoracleTrackingProxy>(), NullLogger<UserAreaWriter>.Instance, this.Sut());

    [Fact]
    public async Task SavingTooManyAreasRefusedBeforeAnythingIsWritten()
    {
        this._humans.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(["a"]);

        await Assert.ThrowsAsync<AlarmValidationException>(() => this.Writer().SaveSelectionAsync(Human, ["a", "b", "c", "d"]));

        this._humans.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task ResavingAListAlreadyOverTheLimitWrites()
    {
        this._humans.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(["a", "b", "c", "d"]);
        this._humans.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);

        await this.Writer().SaveSelectionAsync(Human, ["a", "b", "d"]);
        await this.Writer().SaveSelectionAsync(Human, ["a", "b", "c", "d"]);

        this._humans.Verify(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task MirroringALinkedAccountsListIsNotLimited()
    {
        // "Use Discord's" copies the same person's list from the other side, which already holds it.
        this._humans.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync([]);
        this._humans.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);

        await this.Writer().MirrorSelectionAsync(Human, ["a", "b", "c", "d"]);

        this._humans.Verify(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
    }

    [Fact]
    public async Task SwitchingOnAFenceAtTheLimitRefused()
    {
        this._humans.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(["a", "b", "c"]);

        await Assert.ThrowsAsync<AlarmValidationException>(() => this.Writer().AddToActiveProfileAsync(Human, "d"));

        this._humans.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task SwitchingOnAFenceUnderTheLimitWrites()
    {
        this._humans.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(["a", "b"]);
        this._humans.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);

        Assert.True(await this.Writer().AddToActiveProfileAsync(Human, "c"));
    }
}
