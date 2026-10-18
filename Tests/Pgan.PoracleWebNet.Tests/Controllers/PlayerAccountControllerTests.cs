using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class PlayerAccountControllerTests : ControllerTestBase
{
    private const string UserId = "123456789";
    private const string OtherUserId = "987654321";

    private static readonly PlayerAccountHandles Handles = PlayerAccountHandles.FromSecret("a test jwt secret of at least thirty-two chars");

    private readonly Mock<IPoracleAccountProxy> _proxy = new();
    private readonly Mock<IPoracleV2SchemaService> _schema = new();
    private readonly Mock<IPersonalShinyConfigService> _config = new();
    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly PlayerAccountController _sut;

    public PlayerAccountControllerTests()
    {
        this._schema
            .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleV2Capabilities { PlayerAccounts = true });
        this.SelfService(true);
        // Every human has the one account unless a test says otherwise: relabel and delete resolve a handle
        // against this list.
        this._proxy
            .Setup(p => p.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnAccount()]);

        this._sut = this.NewSut();
        SetupUser(this._sut, userId: UserId);
    }

    private PlayerAccountController NewSut() =>
        new(this._proxy.Object, this._schema.Object, this._config.Object, Handles, this._dataProtection);

    private static string H(string humanId, string playerId = "Ab9cD") => Handles.For(humanId, playerId);

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

    private void NothingWasWritten()
    {
        this._proxy.Verify(
            p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        this._proxy.Verify(
            p => p.RelabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        this._proxy.Verify(
            p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void NothingReachedTheProxy()
    {
        this._proxy.Verify(p => p.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        this.NothingWasWritten();
    }

    private static async Task<string> UndoTokenFrom(Task<IActionResult> delete) =>
        Read<string>(Assert.IsType<OkObjectResult>(await delete).Value, "undoToken")!;

    // ── reads ────────────────────────────────────────────────────

    /// <summary>The whole point: a player id is the input to someone's shiny rolls, so no response carries one.</summary>
    [Fact]
    public async Task ListAnswersHandlesAndNeverAPlayerId()
    {
        var ok = Assert.IsType<OkObjectResult>(await this._sut.List(default));

        var account = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PlayerAccountView>>(ok.Value));
        Assert.Equal(H(UserId), account.Id);
        Assert.Equal("Main", account.Label);
        Assert.DoesNotContain("Ab9cD", JsonSerializer.Serialize(ok.Value));
    }

    /// <summary>An admin looking at someone's account needs to see which game accounts are linked.</summary>
    [Fact]
    public async Task ListWorksWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        Assert.IsType<OkObjectResult>(await this._sut.List(default));
    }

    [Fact]
    public async Task ListStillWorksWithSelfServiceOff()
    {
        this.SelfService(false);

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
    public async Task AddAllowedWhenSelfServiceOnAnswersAHandle()
    {
        this._proxy
            .Setup(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());

        var ok = Assert.IsType<OkObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", "Main"), default));

        Assert.Equal(H(UserId), Assert.IsType<PlayerAccountView>(ok.Value).Id);
        Assert.DoesNotContain("Ab9cD", JsonSerializer.Serialize(ok.Value));
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
    public async Task RelabelDeleteAndRestoreFollowTheSameRules()
    {
        this.SelfService(false);

        var relabel = Assert.IsType<ObjectResult>(
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest("x"), default));
        var delete = Assert.IsType<ObjectResult>(await this._sut.Delete(H(UserId), default));
        var restore = Assert.IsType<ObjectResult>(await this._sut.Restore(new RestorePlayerAccountRequest("anything"), default));

        Assert.Equal("self_service_disabled", Read<string>(relabel.Value, "code"));
        Assert.Equal("self_service_disabled", Read<string>(delete.Value, "code"));
        Assert.Equal("self_service_disabled", Read<string>(restore.Value, "code"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task RelabelAndDeleteAreRefusedWhileImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);

        var relabel = Assert.IsType<ObjectResult>(
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest("x"), default));
        var delete = Assert.IsType<ObjectResult>(await this._sut.Delete(H(UserId), default));

        Assert.Equal(StatusCodes.Status403Forbidden, relabel.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, delete.StatusCode);
        this.NothingReachedTheProxy();
    }

    /// <summary>A handle is resolved on the server, and the proxy gets the real, case-exact player id.</summary>
    [Fact]
    public async Task RelabelAndDeleteResolveTheHandleToThePlayerId()
    {
        Assert.IsType<NoContentResult>(
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest("Alt"), default));
        Assert.IsType<OkObjectResult>(await this._sut.Delete(H(UserId), default));

        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(UserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AHandleThatMatchesNoLinkedAccountAnswers404AndWritesNothing()
    {
        // Includes another human's handle for the same player id: handles are per human.
        foreach (var handle in new[] { "not-a-handle", H(UserId, "ab9cd"), H(OtherUserId) })
        {
            var relabel = Assert.IsType<ObjectResult>(
                await this._sut.Relabel(handle, new RelabelPlayerAccountRequest("x"), default));
            var delete = Assert.IsType<ObjectResult>(await this._sut.Delete(handle, default));

            Assert.Equal(StatusCodes.Status404NotFound, relabel.StatusCode);
            Assert.Equal(StatusCodes.Status404NotFound, delete.StatusCode);
        }

        this.NothingWasWritten();
    }

    [Fact]
    public async Task ANullLabelRelabelsAsAnEmptyOne()
    {
        Assert.IsType<NoContentResult>(
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest(null), default));

        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", string.Empty, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── undo ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnlinkAnswersAnUndoTokenThatCarriesNoPlayerId()
    {
        var token = await UndoTokenFrom(this._sut.Delete(H(UserId), default));

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain("Ab9cD", token);
    }

    [Fact]
    public async Task RestoreRelinksTheUnlinkedAccountWithItsLabel()
    {
        var token = await UndoTokenFrom(this._sut.Delete(H(UserId), default));
        this._proxy
            .Setup(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());

        var ok = Assert.IsType<OkObjectResult>(await this._sut.Restore(new RestorePlayerAccountRequest(token), default));

        Assert.Equal(H(UserId), Assert.IsType<PlayerAccountView>(ok.Value).Id);
        this._proxy.Verify(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUndoTokenOnlyRestoresOntoTheHumanItWasIssuedFor()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        var token = await UndoTokenFrom(this._sut.AdminDelete(OtherUserId, H(OtherUserId), default));

        var ownRestore = Assert.IsType<BadRequestObjectResult>(await this._sut.Restore(new RestorePlayerAccountRequest(token), default));
        var elsewhere = Assert.IsType<BadRequestObjectResult>(
            await this._sut.AdminRestore("someone-else", new RestorePlayerAccountRequest(token), default));

        Assert.Equal("undo_expired", Read<string>(ownRestore.Value, "code"));
        Assert.Equal("undo_expired", Read<string>(elsewhere.Value, "code"));
        this._proxy.Verify(
            p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task ATamperedOrForeignUndoTokenIsRefused(string token)
    {
        var result = Assert.IsType<BadRequestObjectResult>(await this._sut.Restore(new RestorePlayerAccountRequest(token), default));

        Assert.Equal("undo_expired", Read<string>(result.Value, "code"));
        this.NothingWasWritten();
    }

    [Fact]
    public async Task AnUndoTokenFromAnotherServerIsRefused()
    {
        var other = new PlayerAccountController(
            this._proxy.Object, this._schema.Object, this._config.Object, Handles, new EphemeralDataProtectionProvider());
        SetupUser(other, userId: UserId);
        var foreign = await UndoTokenFrom(other.Delete(H(UserId), default));

        var result = Assert.IsType<BadRequestObjectResult>(await this._sut.Restore(new RestorePlayerAccountRequest(foreign), default));

        Assert.Equal("undo_expired", Read<string>(result.Value, "code"));
    }

    // ── administrators ───────────────────────────────────────────

    /// <summary>An administrator managing their own accounts from their own dialog is not a trainer self-service write.</summary>
    [Fact]
    public async Task AdministratorCanAddRelabelAndDeleteOwnAccountsWithSelfServiceOff()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        this.SelfService(false);
        this._proxy
            .Setup(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());

        Assert.IsType<OkObjectResult>(await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", "Main"), default));
        Assert.IsType<NoContentResult>(
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest("Alt"), default));
        Assert.IsType<OkObjectResult>(await this._sut.Delete(H(UserId), default));

        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(UserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>The impersonation check comes first, even for claims that say administrator.</summary>
    [Fact]
    public async Task ImpersonationIsRefusedBeforeTheAdministratorCheck()
    {
        SetupImpersonatingUser(this._sut, userId: UserId, isAdmin: true);
        this.SelfService(false);

        var result = Assert.IsType<ObjectResult>(
            await this._sut.Add(new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Game accounts cannot be changed while viewing another account.", Read<string>(result.Value, "error"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task AnImpersonatedSessionWithSelfServiceOffStillSaysImpersonating()
    {
        SetupImpersonatingUser(this._sut, userId: UserId);
        this.SelfService(false);

        var result = Assert.IsType<ObjectResult>(await this._sut.Delete(H(UserId), default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Game accounts cannot be changed while viewing another account.", Read<string>(result.Value, "error"));
        this.NothingReachedTheProxy();
    }

    [Fact]
    public async Task AdminAddWorksWithSelfServiceOff()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);
        this.SelfService(false);
        this._proxy
            .Setup(p => p.AddAsync(OtherUserId, "Ab9cD", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount(label: null));

        var ok = Assert.IsType<OkObjectResult>(
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default));

        Assert.Equal(H(OtherUserId), Assert.IsType<PlayerAccountView>(ok.Value).Id);
    }

    [Fact]
    public async Task AdminListRelabelDeleteAndRestoreAddressTheNamedHuman()
    {
        SetupUser(this._sut, userId: UserId, isAdmin: true);

        var list = Assert.IsType<OkObjectResult>(await this._sut.AdminList(OtherUserId, default));
        Assert.Equal(H(OtherUserId), Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PlayerAccountView>>(list.Value)).Id);
        Assert.IsType<NoContentResult>(
            await this._sut.AdminRelabel(OtherUserId, H(OtherUserId), new RelabelPlayerAccountRequest("Alt"), default));
        var token = await UndoTokenFrom(this._sut.AdminDelete(OtherUserId, H(OtherUserId), default));
        this._proxy
            .Setup(p => p.AddAsync(OtherUserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnAccount());
        Assert.IsType<OkObjectResult>(await this._sut.AdminRestore(OtherUserId, new RestorePlayerAccountRequest(token), default));

        this._proxy.Verify(p => p.RelabelAsync(OtherUserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.DeleteAsync(OtherUserId, "Ab9cD", It.IsAny<CancellationToken>()), Times.Once);
        this._proxy.Verify(p => p.AddAsync(OtherUserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminActionsForbidNonAdmins()
    {
        Assert.IsType<ForbidResult>(await this._sut.AdminList(OtherUserId, default));
        Assert.IsType<ForbidResult>(
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default));
        Assert.IsType<ForbidResult>(
            await this._sut.AdminRelabel(OtherUserId, H(OtherUserId), new RelabelPlayerAccountRequest("x"), default));
        Assert.IsType<ForbidResult>(await this._sut.AdminDelete(OtherUserId, H(OtherUserId), default));
        Assert.IsType<ForbidResult>(await this._sut.AdminRestore(OtherUserId, new RestorePlayerAccountRequest("x"), default));

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
            await this._sut.Relabel(H(UserId), new RelabelPlayerAccountRequest("x"), default),
            await this._sut.Delete(H(UserId), default),
            await this._sut.Restore(new RestorePlayerAccountRequest("x"), default),
            await this._sut.AdminList(OtherUserId, default),
            await this._sut.AdminAdd(OtherUserId, new AddPlayerAccountRequest("Ab9cD", null), default),
            await this._sut.AdminRelabel(OtherUserId, H(OtherUserId), new RelabelPlayerAccountRequest("x"), default),
            await this._sut.AdminDelete(OtherUserId, H(OtherUserId), default),
            await this._sut.AdminRestore(OtherUserId, new RestorePlayerAccountRequest("x"), default),
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

        this._proxy.Verify(p => p.AddAsync(UserId, "Ab9cD", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
