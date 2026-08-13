using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class OidcSessionRepository(PoracleWebContext context) : IOidcSessionRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<OidcSession?> GetByHashAsync(string sessionTokenHash)
    {
        var entity = await this._context.OidcSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionTokenHash == sessionTokenHash);

        return entity is null ? null : ToModel(entity);
    }

    public async Task AddAsync(OidcSession session)
    {
        this._context.OidcSessions.Add(ToEntity(session));
        await this._context.SaveChangesAsync();
    }

    public async Task<int> TryRevokeForRotationAsync(string sessionTokenHash, string newHash)
    {
        // EF query lambdas require == null / != null (translates to IS NULL); `is null` throws.
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.SessionTokenHash == sessionTokenHash && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, "rotation")
                .SetProperty(s => s.ReplacedByHash, newHash));
    }

    public async Task<int> RevokeFamilyAsync(string familyId, string reason)
    {
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.FamilyId == familyId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason));
    }

    public async Task<int> RevokeAllForUserAsync(string userId, string reason)
    {
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason));
    }

    public async Task<int> DeleteExpiredAndStaleAsync(TimeSpan revokedRetention)
    {
        DateTime now = DateTime.UtcNow;
        DateTime revokedCutoff = now - revokedRetention;

        // Raw SQL rather than ExecuteDeleteAsync: MySql.EntityFrameworkCore emits the aliased
        // single-table form -- DELETE FROM `oidc_sessions` AS `o` WHERE ... -- and MariaDB rejects
        // that outright (1064; it wants the multi-table `DELETE o FROM ... AS o` when an alias is
        // present). Every cleanup pass since the feature shipped threw and logged a warning, so the
        // table only ever grew. Verified against MariaDB 10.8.2. Identifiers are left bare and
        // unquoted so the same statement parses on MariaDB, MySQL and the SQLite the repository
        // tests run against. See #707.
        return await this._context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM oidc_sessions WHERE expires_at < {now} OR (revoked_at IS NOT NULL AND revoked_at < {revokedCutoff})");
    }

    private static OidcSession ToModel(OidcSessionEntity e) => new()
    {
        Id = e.Id,
        SessionTokenHash = e.SessionTokenHash,
        FamilyId = e.FamilyId,
        FamilyIssuedAt = e.FamilyIssuedAt,
        UserId = e.UserId,
        EncryptedRefreshToken = e.EncryptedRefreshToken,
        ExpiresAt = e.ExpiresAt,
        CreatedUtc = e.CreatedUtc,
        RevokedAt = e.RevokedAt,
        RevokedReason = e.RevokedReason,
        ReplacedByHash = e.ReplacedByHash,
        IpAddress = e.IpAddress,
        UserAgent = e.UserAgent,
    };

    private static OidcSessionEntity ToEntity(OidcSession m) => new()
    {
        SessionTokenHash = m.SessionTokenHash,
        FamilyId = m.FamilyId,
        FamilyIssuedAt = m.FamilyIssuedAt,
        UserId = m.UserId,
        EncryptedRefreshToken = m.EncryptedRefreshToken,
        ExpiresAt = m.ExpiresAt,
        CreatedUtc = m.CreatedUtc,
        RevokedAt = m.RevokedAt,
        RevokedReason = m.RevokedReason,
        ReplacedByHash = m.ReplacedByHash,
        IpAddress = m.IpAddress,
        UserAgent = m.UserAgent,
    };
}
