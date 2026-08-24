using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class MuteControllerTests : ControllerTestBase
{
    private const string UserId = "123456789";

    private readonly Mock<IPoracleMuteProxy> _proxy = new();
    private readonly Mock<IMuteCapabilityService> _capability = new();
    private readonly MuteController _sut;

    public MuteControllerTests()
    {
        this._capability
            .Setup(c => c.IsMuteApiAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        this._sut = new MuteController(this._proxy.Object, this._capability.Object);
        SetupUser(this._sut, userId: UserId);
    }

    private static T? Read<T>(object? value, string property) =>
        (T?)value?.GetType().GetProperty(property)?.GetValue(value);

    private static Mute AMute(string scope = MuteScopes.Gym, string? value = "abc123") => new()
    {
        Scope = scope,
        Value = value,
        ExpiresAt = 1787575463,
        RemainingSecs = 1800,
    };

    // ──────────────────────────────────────────────────────────────
    // GET — capability and list in one answer
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReturnsCapableAndTheListForACapableServer()
    {
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute()]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetMutes(default));

        Assert.True(Read<bool>(ok.Value, "capable"));
        Assert.Single(Read<IReadOnlyList<Mute>>(ok.Value, "mutes")!);
    }

    /// <summary>
    /// On a server too old for the endpoint the SPA must be told so plainly, and no call may go out —
    /// the route does not exist there and would 404 on every alarm page load.
    /// </summary>
    [Fact]
    public async Task GetReportsIncapableWithoutCallingUpstream()
    {
        this._capability
            .Setup(c => c.IsMuteApiAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetMutes(default));

        Assert.False(Read<bool>(ok.Value, "capable"));
        this._proxy.Verify(p => p.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Reads stay open during impersonation. An admin looking at an account that gets no alerts needs to
    /// be able to see that something is quiet — that is the whole diagnosis.
    /// </summary>
    [Fact]
    public async Task GetStillWorksWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute()]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetMutes(default));

        Assert.Single(Read<IReadOnlyList<Mute>>(ok.Value, "mutes")!);
    }

    // ──────────────────────────────────────────────────────────────
    // POST — the legitimate cases
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MuteScopes.Gym, "abc123")]
    [InlineData(MuteScopes.Pokemon, "25")]
    [InlineData(MuteScopes.Area, "aberdeen")]
    [InlineData(MuteScopes.Station, "station-7")]
    public async Task EveryWritableScopeIsAccepted(string scope, string value)
    {
        this._proxy
            .Setup(p => p.CreateAsync(UserId, scope, value, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AMute(scope, value), false));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = scope, Value = value }, default));

        Assert.False(Read<bool>(ok.Value, "replaced"));
    }

    /// <summary>Saying which happened is what stops the control feeling like it might have made a second one.</summary>
    [Fact]
    public async Task ExtendingAnExistingQuietPeriodReportsReplaced()
    {
        this._proxy
            .Setup(p => p.CreateAsync(UserId, MuteScopes.Gym, "abc123", 240, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AMute(), true));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = "abc123", DurationMinutes = 240 }, default));

        Assert.True(Read<bool>(ok.Value, "replaced"));
    }

    [Fact]
    public async Task OmittingTheDurationUsesPoraclesOwnSixtyMinuteDefault()
    {
        this._proxy
            .Setup(p => p.CreateAsync(UserId, MuteScopes.Pokemon, "25", 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AMute(MuteScopes.Pokemon, "25"), false));

        Assert.IsType<OkObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Pokemon, Value = "25" }, default));

        this._proxy.Verify(
            p => p.CreateAsync(UserId, MuteScopes.Pokemon, "25", 60, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(MuteScopes.MaxDurationMinutes)]
    public async Task TheDurationBoundsThemselvesAreAccepted(int minutes)
    {
        this._proxy
            .Setup(p => p.CreateAsync(UserId, MuteScopes.Gym, "abc123", minutes, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AMute(), false));

        Assert.IsType<OkObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = "abc123", DurationMinutes = minutes }, default));
    }

    // ──────────────────────────────────────────────────────────────
    // POST — the refusals
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    // Real upstream scopes, but ones this app does not create: tracking uids collide across alarm
    // types, nothing here names a pokestop, and 'everything' is Pause Alerts' job.
    [InlineData(MuteScopes.Tracking)]
    [InlineData(MuteScopes.Pokestop)]
    [InlineData(MuteScopes.Everything)]
    public async Task UnwritableScopesAreRefused(string scope)
    {
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = scope, Value = "1" }, default));

        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACreateWithNoSubjectIsRefused(string? value) =>
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = value }, default));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(MuteScopes.MaxDurationMinutes + 1)]
    public async Task DurationsOutsideOneMinuteToOneWeekAreRefused(int minutes) =>
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = "abc123", DurationMinutes = minutes }, default));

    [Fact]
    public async Task AWriteToAServerWithoutTheEndpointIs501()
    {
        this._capability
            .Setup(c => c.IsMuteApiAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = Assert.IsType<ObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = "abc123" }, default));

        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }

    /// <summary>
    /// UserId names the INSPECTED account during impersonation, so an unguarded write would silence
    /// somebody else's alerts. The #663 shape.
    /// </summary>
    [Fact]
    public async Task AWriteWhileImpersonatingIs403AndNeverReachesUpstream()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        var result = Assert.IsType<ObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Gym, Value = "abc123" }, default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>An unknown area is the refusal that reaches a user, so upstream's sentence is passed through.</summary>
    [Fact]
    public async Task AnUpstreamRefusalIs422CarryingItsReason()
    {
        this._proxy
            .Setup(p => p.CreateAsync(UserId, MuteScopes.Area, "narnia", 60, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MuteRejectedException("unknown area: narnia"));

        var result = Assert.IsType<UnprocessableEntityObjectResult>(await this._sut.CreateMute(
            new MuteCreateRequest { Scope = MuteScopes.Area, Value = "narnia" }, default));

        Assert.Equal("unknown area: narnia", Read<string>(result.Value, "error"));
    }

    // ──────────────────────────────────────────────────────────────
    // DELETE
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingOneQuietPeriodPassesScopeAndValueThrough()
    {
        this._proxy
            .Setup(p => p.DeleteAsync(UserId, MuteScopes.Area, "Aberdeen", It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute(MuteScopes.Area, "Aberdeen")]);

        var ok = Assert.IsType<OkObjectResult>(
            await this._sut.DeleteMute(MuteScopes.Area, "Aberdeen", default));

        Assert.Single(Read<IReadOnlyList<Mute>>(ok.Value, "deleted")!);
    }

    /// <summary>
    /// Deletes accept every scope the server can hold, not only the four this app creates. A mute set
    /// from the Discord bot has to be liftable here or the management list shows rows nobody can act on.
    /// </summary>
    [Theory]
    [InlineData(MuteScopes.Pokestop, "stop-1")]
    [InlineData(MuteScopes.Tracking, "42")]
    public async Task ScopesThisAppCannotCreateCanStillBeLifted(string scope, string value)
    {
        this._proxy
            .Setup(p => p.DeleteAsync(UserId, scope, value, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute(scope, value)]);

        Assert.IsType<OkObjectResult>(await this._sut.DeleteMute(scope, value, default));
    }

    /// <summary>'everything' is the one scope with no value, and sending one would 422 upstream.</summary>
    [Fact]
    public async Task LiftingAnEverythingMuteSendsNoValue()
    {
        this._proxy
            .Setup(p => p.DeleteAsync(UserId, MuteScopes.Everything, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute(MuteScopes.Everything, null)]);

        Assert.IsType<OkObjectResult>(await this._sut.DeleteMute(MuteScopes.Everything, null, default));

        this._proxy.Verify(
            p => p.DeleteAsync(UserId, MuteScopes.Everything, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingWithNoScopeLiftsEverything()
    {
        this._proxy
            .Setup(p => p.DeleteAllAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AMute(), AMute(MuteScopes.Area, "Aberdeen")]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.DeleteMute(null, null, default));

        Assert.Equal(2, Read<IReadOnlyList<Mute>>(ok.Value, "deleted")!.Count);
    }

    /// <summary>
    /// An already-lapsed quiet period comes back as an empty deleted list, not an error — the caller
    /// asked for it to be gone and it is.
    /// </summary>
    [Fact]
    public async Task LiftingAnAlreadyLapsedQuietPeriodSucceedsWithNothingRemoved()
    {
        this._proxy
            .Setup(p => p.DeleteAsync(UserId, MuteScopes.Gym, "abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.DeleteMute(MuteScopes.Gym, "abc123", default));

        Assert.Empty(Read<IReadOnlyList<Mute>>(ok.Value, "deleted")!);
    }

    [Fact]
    public async Task AValueWithoutAScopeIsRefusedRatherThanLiftingEverything()
    {
        Assert.IsType<BadRequestObjectResult>(await this._sut.DeleteMute(null, "abc123", default));

        this._proxy.Verify(p => p.DeleteAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletingAnUnknownScopeIsRefused() =>
        Assert.IsType<BadRequestObjectResult>(await this._sut.DeleteMute("bogus", "x", default));

    [Fact]
    public async Task DeletingAValuedScopeWithNoValueIsRefused() =>
        Assert.IsType<BadRequestObjectResult>(await this._sut.DeleteMute(MuteScopes.Gym, null, default));

    [Fact]
    public async Task ADeleteWhileImpersonatingIs403()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        var result = Assert.IsType<ObjectResult>(await this._sut.DeleteMute(MuteScopes.Gym, "abc123", default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        this._proxy.Verify(
            p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ADeleteAgainstAServerWithoutTheEndpointIs501()
    {
        this._capability
            .Setup(c => c.IsMuteApiAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = Assert.IsType<ObjectResult>(await this._sut.DeleteMute(MuteScopes.Gym, "abc123", default));

        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }
}
