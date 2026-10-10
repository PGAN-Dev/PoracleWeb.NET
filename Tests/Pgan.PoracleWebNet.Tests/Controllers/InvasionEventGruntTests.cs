using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// A Pokestop event added through the invasion endpoint, on a server with a Pokestop Events page.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG stores kecleon, gold-stop and showcase in the invasion table, and on 5.2.x the invasion list
/// hides them because the Pokestop Events page shows them. So <c>POST /api/invasions</c> with
/// <c>gruntType: kecleon</c> answered 201 with a Location that 404s, and the rule never appeared on the
/// page the user had just added it from. Verified on 5.2.1 and 5.3.0: the rule is written and is listed
/// under <c>/api/pokestop-events</c>. When an event rule already existed, PoracleNG answered
/// <c>alreadyPresent</c> and the user got 200 with uid 0, which is the report that started this: the
/// invasion guard reads the filtered list, so it could not see the rule in the way.
/// </para>
/// <para>
/// The SPA offers the event grunts in the invasion dialog only where there is no Pokestop Events page,
/// so the refusal reaches only direct API calls. Quick picks, import and duplicate go through the service
/// and are untouched: the rules they write are real and are shown on the events page.
/// </para>
/// </remarks>
public class InvasionEventGruntTests : ControllerTestBase
{
    private readonly Mock<IInvasionService> _service = new();
    private readonly InvasionController _sut;

    public InvasionEventGruntTests()
    {
        this._sut = new InvasionController(this._service.Object);
        SetupUser(this._sut);
        this._service.Setup(s => s.BelongsToPokestopEventsAsync(It.Is<string?>(g => g == "kecleon" || g == "Showcase"))).ReturnsAsync(true);
    }

    [Theory]
    [InlineData("kecleon")]
    [InlineData("Showcase")]
    public async Task CreatingAnEventThroughTheInvasionEndpointPointsAtTheEventsPage(string grunt)
    {
        var result = await this._sut.Create(new InvasionCreate { GruntType = grunt, Distance = 1000 });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Pokestop Events", System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.Ordinal);
        this._service.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Invasion>()), Times.Never);
    }

    [Fact]
    public async Task EditingAnInvasionIntoAnEventIsRefusedTheSameWay()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 5)).ReturnsAsync(new Invasion { Uid = 5, GruntType = "water" });

        var result = await this._sut.Update(5, new InvasionUpdate { GruntType = "kecleon" });

        Assert.IsType<BadRequestObjectResult>(result);
        this._service.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Invasion>()), Times.Never);
    }

    [Fact]
    public async Task AnOrdinaryGruntIsStillCreated()
    {
        this._service.Setup(s => s.CreateAsync("123456789", It.IsAny<Invasion>()))
            .ReturnsAsync((string _, Invasion i) => { i.Uid = 9; return i; });

        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(new InvasionCreate { GruntType = "water", Distance = 1000 }));
    }

    // ── the service's answer ─────────────────────────────────────────────────

    [Theory]
    [InlineData("kecleon", true, true)]
    [InlineData("GOLD-STOP", true, true)]
    [InlineData("water", true, false)]
    [InlineData("kecleon", false, false)] // 5.1.0, or disable_showcase: no events page, the invasion dialog offers them
    public async Task AnEventBelongsElsewhereOnlyWhereThePageExists(string grunt, bool eventsPage, bool expected)
    {
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(eventsPage);
        var service = new InvasionService(
            new Mock<IPoracleTrackingProxy>().Object, gate.Object, NullLogger<InvasionService>.Instance,
            new Mock<ITrackedUidRemapper>().Object, NaturalKeyStub.Dropped);

        Assert.Equal(expected, await service.BelongsToPokestopEventsAsync(grunt));
    }
}
