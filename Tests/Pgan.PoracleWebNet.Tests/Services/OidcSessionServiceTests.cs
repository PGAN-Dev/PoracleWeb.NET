using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Security-core tests for the OIDC refresh-session mechanics: opaque-token hashing, encrypted
/// storage of the provider refresh token, atomic rotation, and replay/cap family revocation.
/// </summary>
public class OidcSessionServiceTests
{
    private const string Purpose = "Pgan.PoracleWebNet.OidcRefresh.v1";

    private readonly Mock<IOidcSessionRepository> _repo = new();
    private readonly EphemeralDataProtectionProvider _dpProvider = new();
    private readonly OidcSettings _settings = new() { UseRefreshTokens = true, RefreshTokenLifetimeDays = 30, AccessTokenMinutes = 30 };

    private OidcSessionService CreateSut() => new(
        this._repo.Object,
        this._dpProvider,
        Options.Create(this._settings),
        new Mock<ILogger<OidcSessionService>>().Object);

    private string Protect(string value) => this._dpProvider.CreateProtector(Purpose).Protect(value);

    [Fact]
    public async Task IssueAsync_StoresHashAndEncryptedToken_AndReturnsOpaque()
    {
        OidcSession? captured = null;
        this._repo.Setup(r => r.AddAsync(It.IsAny<OidcSession>()))
            .Callback<OidcSession>(s => captured = s)
            .Returns(Task.CompletedTask);

        var sut = this.CreateSut();
        var opaque = await sut.IssueAsync("user-1", "idp-refresh-token", "1.2.3.4", "agent");

        Assert.False(string.IsNullOrWhiteSpace(opaque));
        Assert.NotNull(captured);
        // The raw opaque token is never stored — only its hash.
        Assert.NotEqual(opaque, captured!.SessionTokenHash);
        Assert.Equal(64, captured.SessionTokenHash.Length); // SHA-256 hex
        // The provider refresh token is encrypted, not stored in clear.
        Assert.NotEqual("idp-refresh-token", captured.EncryptedRefreshToken);
        Assert.Equal("idp-refresh-token", this._dpProvider.CreateProtector(Purpose).Unprotect(captured.EncryptedRefreshToken));
        Assert.Equal("user-1", captured.UserId);
        Assert.Null(captured.RevokedAt);
    }

    [Fact]
    public async Task StartRotationAsync_HappyPath_RevokesPresentedAndReturnsDecryptedToken()
    {
        var session = new OidcSession
        {
            SessionTokenHash = "hash",
            FamilyId = "fam-1",
            FamilyIssuedAt = DateTime.UtcNow.AddDays(-1),
            UserId = "user-1",
            EncryptedRefreshToken = this.Protect("idp-rt"),
            ExpiresAt = DateTime.UtcNow.AddDays(29),
        };
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(session);
        this._repo.Setup(r => r.TryRevokeForRotationAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(1);

        var sut = this.CreateSut();
        var ticket = await sut.StartRotationAsync("opaque", null, null);

        Assert.Equal("idp-rt", ticket.DecryptedRefreshToken);
        Assert.Equal("fam-1", ticket.FamilyId);
        Assert.False(string.IsNullOrWhiteSpace(ticket.NewOpaqueToken));
        Assert.Equal(64, ticket.NewTokenHash.Length);
        this._repo.Verify(r => r.TryRevokeForRotationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task StartRotationAsync_ReplayedToken_RevokesFamilyAndThrows()
    {
        var session = new OidcSession
        {
            FamilyId = "fam-1",
            UserId = "user-1",
            EncryptedRefreshToken = this.Protect("idp-rt"),
            FamilyIssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(29),
            RevokedAt = DateTime.UtcNow.AddMinutes(-5), // already revoked ⇒ replay
        };
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(session);

        var sut = this.CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.StartRotationAsync("opaque", null, null));
        this._repo.Verify(r => r.RevokeFamilyAsync("fam-1", "replay_detected"), Times.Once);
    }

    [Fact]
    public async Task StartRotationAsync_PastAbsoluteCap_RevokesFamilyAndThrows()
    {
        var session = new OidcSession
        {
            FamilyId = "fam-1",
            UserId = "user-1",
            EncryptedRefreshToken = this.Protect("idp-rt"),
            FamilyIssuedAt = DateTime.UtcNow.AddDays(-31), // beyond the 30-day cap
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(session);

        var sut = this.CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.StartRotationAsync("opaque", null, null));
        this._repo.Verify(r => r.RevokeFamilyAsync("fam-1", "absolute_cap"), Times.Once);
    }

    [Fact]
    public async Task StartRotationAsync_ConcurrentRotation_TreatedAsReplay()
    {
        var session = new OidcSession
        {
            FamilyId = "fam-1",
            UserId = "user-1",
            EncryptedRefreshToken = this.Protect("idp-rt"),
            FamilyIssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(29),
        };
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(session);
        this._repo.Setup(r => r.TryRevokeForRotationAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(0); // lost the race

        var sut = this.CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.StartRotationAsync("opaque", null, null));
        this._repo.Verify(r => r.RevokeFamilyAsync("fam-1", "replay_detected"), Times.Once);
    }

    [Fact]
    public async Task StartRotationAsync_UnknownToken_Throws()
    {
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync((OidcSession?)null);
        var sut = this.CreateSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.StartRotationAsync("opaque", null, null));
        this._repo.Verify(r => r.RevokeFamilyAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteRotationAsync_InsertsSuccessorWithCarriedToken()
    {
        OidcSession? captured = null;
        this._repo.Setup(r => r.AddAsync(It.IsAny<OidcSession>()))
            .Callback<OidcSession>(s => captured = s)
            .Returns(Task.CompletedTask);

        var ticket = new OidcRotationTicket
        {
            UserId = "user-1",
            FamilyId = "fam-1",
            FamilyIssuedAt = DateTime.UtcNow.AddDays(-2),
            DecryptedRefreshToken = "old",
            NewOpaqueToken = "new-opaque",
            NewTokenHash = "new-hash",
        };

        var sut = this.CreateSut();
        await sut.CompleteRotationAsync(ticket, "rotated-idp-rt");

        Assert.NotNull(captured);
        Assert.Equal("new-hash", captured!.SessionTokenHash);
        Assert.Equal("fam-1", captured.FamilyId);
        Assert.Equal("rotated-idp-rt", this._dpProvider.CreateProtector(Purpose).Unprotect(captured.EncryptedRefreshToken));
        // Successor expires at the family's absolute cap (fixed window).
        Assert.Equal(ticket.FamilyIssuedAt.AddDays(30), captured.ExpiresAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RevokeAsync_RevokesFamilyForKnownToken()
    {
        var session = new OidcSession { FamilyId = "fam-9" };
        this._repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(session);

        var sut = this.CreateSut();
        await sut.RevokeAsync("opaque", "logout");
        this._repo.Verify(r => r.RevokeFamilyAsync("fam-9", "logout"), Times.Once);
    }
}
