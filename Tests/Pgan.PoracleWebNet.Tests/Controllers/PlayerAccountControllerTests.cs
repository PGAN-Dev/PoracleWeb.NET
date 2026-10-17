using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class PlayerAccountControllerTests : ControllerTestBase
{
    private const string UserId = "123456789";
    private const string OtherUserId = "987654321";

    private readonly Mock<IPoracleAccountProxy> _proxy = new();
    private readonly Mock<IPoracleV2SchemaService> _schema = new();
    private readonly Mock<IPersonalShinyConfigService> _config = new();
    private readonly PlayerAccountController _sut;

    public PlayerAccountControllerTests()
    {
        this._schema
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleV2Capabilities { PlayerAccounts = true });
        this.SelfService(true);

        this._sut = new PlayerAccountController(this._proxy.Object, this._schema.Object, this._config.Object);
        SetupUser(this._sut, userId: UserId);
    }

    private static T? Read<T>(object? value, string property) =>
        (T?)value?.GetType().GetProperty(property)?.GetValue(value);

    private static PlayerAccount AnAccount(string playerId = "Ab9cD", string? label = "Main") =>
        new(playerId, label, DateTimeOffset.UnixEpoch);

    private void SelfService(bool on) =>
        this._config
            .Setup(c => c.GetAsync())
            .ReturnsAsync(new PersonalShinyConfig(true, on));

    private void NoServerSupport() =>
        this._schema
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleV2Capabilities { PlayerAccounts = false });

    private void NothingReachedTheProxy()
    {
        this._proxy.Verify(p => p.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        this._proxy.Verify(
            p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        this._proxy.Verify(
            p => p.RelabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        this._proxy.Verify(
            p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── reads ────────────────────────────────────────────────────

    [Fact]
    public async Task ListReturnsOwnAccounts()
    {
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnAccount()]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.List(default));

        var accounts = Assert.IsAssignableFrom<IReadOnlyList<PlayerAccount>>(ok.Value);
        Assert.Equal("Ab9cD", Assert.Single(accounts).PlayerId);
    }

    /// <summary>An admin looking at someone's account needs to see which game accounts are linked.</summary>
    [Fact]
    public async Task ListWorksWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnAccount()]);

        Assert.IsType<OkObjectResult>(await this._sut.List(default));
    }

    [Fact]
    public async Task ListStillWorksWithSelfServiceOff()
    {
        this.SelfService(false);
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnAccount()]);

        Assert.IsType<OkObjectResult>(await this._sut.List(default));
    }

    // ── trainer writes ───────────────────────────────────────────

    [Fact]
    public async Task AddRefusedWhenSelfServiceOff()
    {
        this.SelfService(false);

        var result = Assert.IsType<ObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("self_service_disabled", Read<string>(result.Value, "code"));
        Assert.Equal("Only an administrator can link game accounts on this server.", Read<string>(result.Value, "error"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task AddAllowedWhenSelfServiceOn()
    {
        this._proxy
            .Setup(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());

        var ok = Assert.IsType<OkObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", "Main"), default));

        Assert.Equal("Ab9cD", Assert.IsType<PlayerAccount>(ok.Value).PlayerId);
    }

    [Fact]
    public async Task AddRefusedWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        var result = Assert.IsType<ObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Game accounts cannot be changed while viewing another account.", Read<string>(result.Value, "error"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task RelabelAndDeleteFollowTheSameRules()
    {
        this.SelfService(false);

        var relabel = Assert.IsType<ObjectResult>(
            await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest("x"), default));
        var delete = Assert.IsType<ObjectResult>(await this._sut.Delete("Ab9cD", default));

        Assert.Equal("self_service_disabled", Read<string>(relabel.Value, "code"));
        Assert.Equal("self_service_disabled", Read<string>(delete.Value, "code"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task RelabelAndDeleteAreRefusedWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        var relabel = Assert.IsType<ObjectResult>(
            await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest("x"), default));
        var delete = Assert.IsType<ObjectResult>(await this._sut.Delete("Ab9cD", default));

        Assert.Equal(StatusCodes.Status403Forbidden, relabel.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, delete.StatusCode);
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task RelabelAndDeleteReachTheProxyWhenAllowed()
    {
        Assert.IsType<NoContentResult>(
            await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest("Alt"), default));
        Assert.IsType<NoContentResult>(await this._sut.Delete("Ab9cD", default));

        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(UserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ANullLabelRelabelsAsAnEmptyOne()
    {
        Assert.IsType<NoContentResult>(
            await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest(null), default));

        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", string.Empty, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── admin ────────────────────────────────────────────────────

    [Fact]
    public async Task AdminAddWorksWithSelfServiceOff()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        this.SelfService(false);
        this._proxy
            .Setup(p => p.AddAsync(OtherUserId, "Ab9cD", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount(label: null));

        Assert.IsType<OkObjectResult>(
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default));
    }

    [Fact]
    public async Task AdminListRelabelAndDeleteAddressTheNamedHuman()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        this._proxy
            .Setup(p => p.ListAsync(OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnAccount()]);

        Assert.IsType<OkObjectResult>(await this._sut.AdminList(OtherUserId, default));
        Assert.IsType<NoContentResult>(
            await this._sut.AdminRelabel(OtherUserId, "Ab9cD", new RelabelPlayerAccountRequest("Alt"), default));
        Assert.IsType<NoContentResult>(await this._sut.AdminDelete(OtherUserId, "Ab9cD", default));

        this._proxy.Verify(p => p.RelabelAsync(OtherUserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(OtherUserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminActionsForbidNonAdmins()
    {
        Assert.IsType<ForbidResult>(await this._sut.AdminList(OtherUserId, default));
        Assert.IsType<ForbidResult>(
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default));
        Assert.IsType<ForbidResult>(
            await this._sut.AdminRelabel(OtherUserId, "Ab9cD", new RelabelPlayerAccountRequest("x"), default));
        Assert.IsType<ForbidResult>(await this._sut.AdminDelete(OtherUserId, "Ab9cD", default));

        this.NothingReachedTheProxy();
    }

    // ── upstream answers ─────────────────────────────────────────

    [Fact]
    public async Task AdminAddForUnknownHumanAnswers404()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        this._proxy
            .Setup(p => p.AddAsync(OtherUserId, "Ab9cD", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PoracleRequestRefusedException("Human not found", StatusCodes.Status404NotFound));

        var result = Assert.IsType<ObjectResult>(
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("Human not found", Read<string>(result.Value, "error"));
    }

    [Fact]
    public async Task ADuplicateAnswers409WithPoraclesDetail()
    {
        this._proxy
            .Setup(p => p.AddAsync(UserId, "Ab9cD", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PoracleRequestRefusedException("player id already linked", StatusCodes.Status409Conflict));

        var result = Assert.IsType<ObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("player id already linked", Read<string>(result.Value, "error"));
    }

    [Fact]
    public async Task ARefusedListKeepsPoraclesStatus()
    {
        this._proxy
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PoracleRequestRefusedException("nope", StatusCodes.Status422UnprocessableEntity));

        var result = Assert.IsType<ObjectResult>(await this._sut.List(default));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
    }

    [Fact]
    public async Task EverythingAnswers501WithoutServerSupport()
    {
        this.NoServerSupport();
        SetupUser(this._sut, userId: UserId, isAdmin: true);

        var results = new[]
        {
            await this._sut.List(default),
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default),
            await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest("x"), default),
            await this._sut.Delete("Ab9cD", default),
            await this._sut.AdminList(OtherUserId, default),
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default),
            await this._sut.AdminRelabel(OtherUserId, "Ab9cD", new RelabelPlayerAccountRequest("x"), default),
            await this._sut.AdminDelete(OtherUserId, "Ab9cD", default),
        };

        foreach (var result in results)
        {
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status501NotImplemented, obj.StatusCode);
            Assert.Equal("This Poracle server does not support game accounts.", Read<string>(obj.Value, "error"));
        }

        this.NothingReachedTheProxy();
    }

    /// <summary>The 501 comes before the admin check, so the SPA can always tell.</summary>
    [Fact]
    public async Task ANonAdminOnAnUnsupportedServerGets501NotForbid()
    {
        this.NoServerSupport();

        var result = Assert.IsType<ObjectResult>(await this._sut.AdminList(OtherUserId, default));

        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }

    /// <summary>Player ids are case-sensitive; nothing may lowercase, trim or otherwise tidy them.</summary>
    [Fact]
    public async Task PlayerIdReachesTheProxyUnchanged()
    {
        this._proxy
            .Setup(p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());

        await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default);
        await this._sut.Relabel("Ab9cD", new RelabelPlayerAccountRequest("x"), default);
        await this._sut.Delete("Ab9cD", default);

        this._proxy.Verify(p => p.AddAsync(UserId, "Ab9cD", null, It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", "x", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(UserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
    }
}
