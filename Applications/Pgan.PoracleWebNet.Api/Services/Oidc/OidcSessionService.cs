using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services.Oidc;

/// <summary>See <see cref="IOidcSessionService"/>. The opaque token is 32 bytes of CSPRNG entropy
/// (base64url); only its SHA-256 hash is stored. The provider refresh token is encrypted with
/// DataProtection (purpose-scoped) and never leaves the server.</summary>
public sealed partial class OidcSessionService : IOidcSessionService
{
    private const string ProtectorPurpose = "Pgan.PoracleWebNet.OidcRefresh.v1";

    private readonly IOidcSessionRepository _sessions;
    private readonly IDataProtector _protector;
    private readonly OidcSettings _settings;
    private readonly ILogger<OidcSessionService> _logger;

    public OidcSessionService(
        IOidcSessionRepository sessions,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<OidcSettings> oidcSettings,
        ILogger<OidcSessionService> logger)
    {
        this._sessions = sessions;
        this._protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        this._settings = oidcSettings.Value;
        this._logger = logger;
    }

    public async Task<string> IssueAsync(string userId, string idpRefreshToken, string? ipAddress, string? userAgent)
    {
        var opaque = GenerateOpaqueToken();
        var now = DateTime.UtcNow;

        await this._sessions.AddAsync(new OidcSession
        {
            SessionTokenHash = HashToken(opaque),
            FamilyId = Guid.NewGuid().ToString(),
            FamilyIssuedAt = now,
            UserId = userId,
            EncryptedRefreshToken = this._protector.Protect(idpRefreshToken),
            ExpiresAt = now.AddDays(this._settings.RefreshTokenLifetimeDays),
            CreatedUtc = now,
            IpAddress = ipAddress,
            UserAgent = userAgent,
        });

        return opaque;
    }

    public async Task<OidcRotationTicket> StartRotationAsync(string opaqueToken, string? ipAddress, string? userAgent)
    {
        var hash = HashToken(opaqueToken);
        var session = await this._sessions.GetByHashAsync(hash);
        if (session is null)
        {
            throw InvalidGrant();
        }

        // Replay: a presented-but-already-revoked token revokes the whole family.
        if (session.RevokedAt is not null)
        {
            await this._sessions.RevokeFamilyAsync(session.FamilyId, "replay_detected");
            LogReplayDetected(this._logger, HashPrefix(hash));
            throw InvalidGrant();
        }

        var now = DateTime.UtcNow;

        // Absolute cap: a family cannot be refreshed past FamilyIssuedAt + RefreshTokenLifetimeDays.
        if (session.FamilyIssuedAt.AddDays(this._settings.RefreshTokenLifetimeDays) <= now)
        {
            await this._sessions.RevokeFamilyAsync(session.FamilyId, "absolute_cap");
            throw InvalidGrant();
        }

        // Plain expiry (no family revoke).
        if (session.ExpiresAt <= now)
        {
            throw InvalidGrant();
        }

        var newOpaque = GenerateOpaqueToken();
        var newHash = HashToken(newOpaque);

        // Atomic guard: revokes the presented row only if still active. 0 ⇒ a concurrent refresh
        // already rotated it — treat as replay and revoke the family.
        var affected = await this._sessions.TryRevokeForRotationAsync(hash, newHash);
        if (affected == 0)
        {
            await this._sessions.RevokeFamilyAsync(session.FamilyId, "replay_detected");
            LogReplayDetected(this._logger, HashPrefix(hash));
            throw InvalidGrant();
        }

        string decrypted;
        try
        {
            decrypted = this._protector.Unprotect(session.EncryptedRefreshToken);
        }
        catch (CryptographicException)
        {
            await this._sessions.RevokeFamilyAsync(session.FamilyId, "decrypt_failed");
            throw InvalidGrant();
        }

        return new OidcRotationTicket
        {
            UserId = session.UserId,
            FamilyId = session.FamilyId,
            FamilyIssuedAt = session.FamilyIssuedAt,
            DecryptedRefreshToken = decrypted,
            NewOpaqueToken = newOpaque,
            NewTokenHash = newHash,
        };
    }

    public async Task CompleteRotationAsync(OidcRotationTicket ticket, string newIdpRefreshToken)
    {
        var now = DateTime.UtcNow;
        await this._sessions.AddAsync(new OidcSession
        {
            SessionTokenHash = ticket.NewTokenHash,
            FamilyId = ticket.FamilyId,
            FamilyIssuedAt = ticket.FamilyIssuedAt,
            UserId = ticket.UserId,
            EncryptedRefreshToken = this._protector.Protect(newIdpRefreshToken),
            // Fixed window: successor expires at the family's absolute cap.
            ExpiresAt = ticket.FamilyIssuedAt.AddDays(this._settings.RefreshTokenLifetimeDays),
            CreatedUtc = now,
        });
    }

    public async Task AbortRotationAsync(OidcRotationTicket ticket, string reason) =>
        await this._sessions.RevokeFamilyAsync(ticket.FamilyId, reason);

    public async Task RevokeAsync(string opaqueToken, string reason)
    {
        if (string.IsNullOrEmpty(opaqueToken))
        {
            return;
        }

        var session = await this._sessions.GetByHashAsync(HashToken(opaqueToken));
        if (session is not null)
        {
            await this._sessions.RevokeFamilyAsync(session.FamilyId, reason);
        }
    }

    public async Task RevokeAllForUserAsync(string userId, string reason) =>
        await this._sessions.RevokeAllForUserAsync(userId, reason);

    private static string GenerateOpaqueToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string HashPrefix(string hash) => hash.Length <= 8 ? hash : hash[..8];

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static UnauthorizedAccessException InvalidGrant() => new("invalid_grant");

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC refresh replay detected for session {HashPrefix}; family revoked.")]
    private static partial void LogReplayDetected(ILogger logger, string hashPrefix);
}
